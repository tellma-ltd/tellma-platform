// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type TmAppConfig,
  type TmAppDeclarations,
  type TmClientMetadata,
  type TmEntitiesChunk,
  ɵtmMergeAppDeclarations,
  type ɵTmMergeResult,
} from '@tellma/core';

import { type TmConfigProblem, type TmConfigProblemCode, tmConfigProblem, type TmProblemFacts } from './tm-config-problem';
import { tmEntityFindings, TmPathResolver } from './tm-entity-checks';

// The config check (spec 0024 §3.6): one function that checks an app's
// configuration against the client metadata, in the build and at startup in
// development. It merges the features as the app would, keeps the merge's
// problems, and adds the rules that need the rest of the metadata: languages,
// calendars, string packs, paths, sections, coded reports, settings tabs,
// widget types, entity declarations and label fallbacks. It reads the config
// as plain data and never calls a lazy member.

/** The calendars the platform ships an entry point for. */
const PLATFORM_CALENDARS: ReadonlySet<string> = new Set(['gc', 'uq', 'et']);

/** A kebab-case literal path segment. */
const LITERAL_SEGMENT = /^[a-z][a-z0-9]*(-[a-z0-9]+)*$/;

/** A `:name` path parameter. */
const PARAMETER_SEGMENT = /^:[a-z][a-zA-Z0-9]*$/;

/** A coded report key: kebab-case with at least one hyphen. */
const REPORT_KEY = /^[a-z][a-z0-9]*(-[a-z0-9]+)+$/;

/** The segment the platform reserves for create pages. */
const NEW_SEGMENT = 'new';

/** The client string scopes a server string key may not start with. */
const CLIENT_STRING_SCOPES = ['tm.', 'tmUi.'] as const;

/** What every check reads, gathered once. */
interface CheckContext {
  /** The config being checked. */
  readonly config: TmAppConfig;
  /** The Core chunk. */
  readonly metadata: TmClientMetadata;
  /** The merge of the config's features. */
  readonly merge: ɵTmMergeResult;
  /** The merged declarations. */
  readonly declarations: TmAppDeclarations;
  /** Records a problem. */
  readonly report: (code: TmConfigProblemCode, key: string, feature: string, facts?: TmProblemFacts) => void;
}

/**
 * The feature a namespaced server key belongs to: the part before its first
 * dot, such as `acme` for `acme.Customer`.
 *
 * @param name - The key.
 * @param fallback - The feature when the key has no dot.
 * @returns The feature's key.
 */
function namespaceOf(name: string, fallback: string): string {
  const dot = name.indexOf('.');
  return dot > 0 ? name.slice(0, dot) : fallback;
}

// -----------------------------------------------------------------------------
// Server facts: features, languages, calendars and string packs
// -----------------------------------------------------------------------------

/**
 * Checks the features, languages, calendars and string packs against what
 * the server composes and offers.
 *
 * @param context - The check's context.
 * @param strings - The loaded string packs, by language.
 */
function checkServerFacts(context: CheckContext, strings: Readonly<Record<string, Readonly<Record<string, string>>>>): void {
  const { config, metadata, report } = context;
  const distribution = metadata.distribution.slug;

  // --- Every client feature is one the server composes ---
  const composed = new Set(metadata.features.map((feature) => feature.key));
  for (const feature of config.features) {
    if (feature.key !== 'core' && !composed.has(feature.key)) {
      report('feature-not-composed', `feature:${feature.key}`, feature.key);
    }
  }

  // --- Languages: one definition per offered language, the default first ---
  const offered = new Set(metadata.languages.map((language) => language.code));
  const defined = new Set<string>();
  for (const language of config.languages) {
    if (defined.has(language.code)) {
      report('duplicate-key', `language:${language.code}`, distribution);
      continue;
    }
    defined.add(language.code);
    if (!offered.has(language.code)) {
      report('language-not-offered', `language:${language.code}`, distribution);
    }
  }
  for (const language of metadata.languages) {
    if (!defined.has(language.code)) {
      report('language-missing', `language:${language.code}`, distribution);
    }
  }
  const clientDefault = config.languages[0]?.code;
  const serverDefault = metadata.languages[0]?.code;
  if (clientDefault !== undefined && serverDefault !== undefined && clientDefault !== serverDefault) {
    report('default-language-mismatch', `language:${clientDefault}`, distribution, { client: clientDefault, server: serverDefault });
  }

  // --- Calendars: the platform's, or one the distribution defines ---
  const calendars = new Set([...PLATFORM_CALENDARS, ...(config.calendars ?? []).map((calendar) => calendar.code)]);
  for (const code of metadata.calendars) {
    if (!calendars.has(code)) {
      report('calendar-missing', `calendar:${code}`, distribution);
    }
  }

  // --- String packs never take a client scope's keys ---
  for (const [language, pack] of Object.entries(strings)) {
    for (const key of Object.keys(pack)) {
      if (CLIENT_STRING_SCOPES.some((scope) => key.startsWith(scope))) {
        report('string-key-collision', `string:${key}`, distribution, { language });
      }
    }
  }
}

// -----------------------------------------------------------------------------
// Routes: page paths and patched coded-report keys
// -----------------------------------------------------------------------------

/**
 * The words the platform's routes reserve as a first segment.
 *
 * @param metadata - The Core chunk.
 * @returns The routing words and `new`.
 */
function reservedWords(metadata: TmClientMetadata): ReadonlySet<string> {
  const routing = metadata.routing;
  return new Set([
    ...routing.serverFirstSegments,
    ...routing.serverTenantSegments,
    ...routing.spaFirstSegments,
    ...routing.spaTenantSegments,
    NEW_SEGMENT,
  ]);
}

/**
 * Checks one page's path against the grammar.
 *
 * @param context - The check's context.
 * @param path - The page's path.
 * @param section - The page's launcher section.
 * @returns Whether the path keeps the grammar.
 */
function checkPathGrammar(context: CheckContext, path: string, section: unknown): boolean {
  const key = `page:${path}`;
  const feature = context.merge.origin(key) ?? '';
  const segments = path.split('/');
  const parameters = new Set<string>();
  for (const [index, segment] of segments.entries()) {
    let reason: string | null = null;
    if (segment === '') {
      reason = 'empty';
    } else if (PARAMETER_SEGMENT.test(segment)) {
      if (index === 0) {
        reason = 'first-parameter';
      } else if (parameters.has(segment)) {
        reason = 'duplicate-parameter';
      }
      parameters.add(segment);
    } else if (!LITERAL_SEGMENT.test(segment)) {
      reason = 'segment';
    } else if (segment === NEW_SEGMENT && index > 0) {
      // A first segment `new` is a reserved word, checked with the others.
      reason = 'new';
    }
    if (reason !== null) {
      context.report('invalid-path', key, feature, { reason, segment });
      return false;
    }
  }
  if (parameters.size > 0 && section !== undefined) {
    context.report('invalid-path', key, context.merge.origin(key, 'section') ?? feature, { reason: 'section-with-parameter' });
  }
  return true;
}

/**
 * Checks the pages' paths and the patched coded reports' keys.
 *
 * @param context - The check's context.
 */
function checkRoutes(context: CheckContext): void {
  const { metadata, merge, declarations, report } = context;
  const reserved = reservedWords(metadata);
  const entitiesBySegment = new Map(
    metadata.entities.flatMap((entity) => (entity.segment === null ? [] : [[entity.segment, entity.name] as const])),
  );

  // --- Pages: the grammar, then the first segment ---
  for (const page of declarations.pages) {
    if (!checkPathGrammar(context, page.path, page.section)) {
      continue;
    }
    const key = `page:${page.path}`;
    const feature = merge.origin(key) ?? '';
    const segment = page.path.split('/')[0] ?? '';
    const entity = entitiesBySegment.get(segment);
    if (reserved.has(segment)) {
      report('reserved-path', key, feature, { reason: 'routing', segment });
    } else if (entity !== undefined && merge.origin(`entity:${entity}`) !== feature) {
      // An entity's segment is the page's only when the page's own feature
      // declares the entity; the entity's routes exist even when no feature does.
      const declaredBy = merge.origin(`entity:${entity}`) ?? '';
      report('reserved-path', key, feature, { reason: 'entity', segment, entity, declaredBy });
    }
  }

  // --- Patched coded-report keys; the server checks the unpatched ones ---
  const reportFeatures = new Map(metadata.codedReports.map((entry) => [entry.key, entry.feature]));
  for (const [reportKey, patchers] of merge.reportPatchers) {
    const key = `report:${reportKey}`;
    const feature = patchers[0] ?? '';
    if (!REPORT_KEY.test(reportKey)) {
      report('invalid-report-key', key, feature);
      continue;
    }
    const entity = entitiesBySegment.get(reportKey);
    const entityFeature = entity === undefined ? undefined : merge.origin(`entity:${entity}`);
    if (reserved.has(reportKey)) {
      report('reserved-path', key, feature, { reason: 'routing', segment: reportKey });
    } else if (entity !== undefined && entityFeature !== undefined && entityFeature !== reportFeatures.get(reportKey)) {
      report('reserved-path', key, feature, { reason: 'entity', segment: reportKey, entity, declaredBy: entityFeature });
    }
  }
}

// -----------------------------------------------------------------------------
// Declarations: sections, coded reports, settings tabs and widget types
// -----------------------------------------------------------------------------

/**
 * Checks every `section` member against the declared sections.
 *
 * @param context - The check's context.
 */
function checkSections(context: CheckContext): void {
  const { merge, declarations, report } = context;
  const holders: (readonly [key: string, section: unknown])[] = [
    ...[...declarations.entities.values()].map((entity) => [`entity:${entity.name}`, entity['section']] as const),
    ...declarations.pages.map((page) => [`page:${page.path}`, page.section] as const),
    ...declarations.reports.map((entry) => [`report:${entry.key}`, entry.section] as const),
    ...declarations.entries.map((entry) => [`entry:${entry.id}`, entry.section] as const),
  ];
  for (const [key, section] of holders) {
    if (typeof section === 'string' && !merge.declaredSections.has(section)) {
      report('unknown-section', key, merge.origin(key, 'section') ?? '', { section });
    }
  }
}

/**
 * Checks that each coded report has one source: the server's route, or one
 * of the client's `run` and `component`.
 *
 * @param context - The check's context.
 */
function checkReportSources(context: CheckContext): void {
  const { merge, declarations, report } = context;
  for (const entry of declarations.reports) {
    const key = `report:${entry.key}`;
    const hasRun = entry.run !== undefined;
    const hasComponent = entry.component !== undefined;
    if (entry.route !== undefined) {
      if (hasRun) {
        report('coded-report-source', key, merge.origin(key, 'run') ?? entry.feature, { reason: 'route-with-run' });
      }
    } else if (hasRun === hasComponent) {
      const patchers = merge.reportPatchers.get(entry.key) ?? [];
      const feature = merge.origin(key, 'run') ?? merge.origin(key, 'component') ?? patchers.at(-1) ?? entry.feature;
      report('coded-report-source', key, feature, { reason: hasRun ? 'both' : 'neither' });
    }
  }
}

/**
 * Checks the settings tabs and widget types against the server's.
 *
 * @param context - The check's context.
 */
function checkServerKeys(context: CheckContext): void {
  const { metadata, merge, declarations, report } = context;

  // --- Settings tabs replace the body of a category the server declares ---
  const categories = new Set(metadata.settingCategories.map((category) => category.name));
  for (const tab of declarations.settingsTabs) {
    if (!categories.has(tab.category)) {
      const key = `settingsTab:${tab.category}`;
      report('unknown-setting-category', key, merge.origin(key) ?? '');
    }
  }

  // --- Widget types: one declaration per server type, and no other ---
  const registered = new Set(metadata.widgetTypes);
  const declared = new Set(declarations.widgets.map((widget) => widget.key));
  for (const type of metadata.widgetTypes) {
    if (!declared.has(type)) {
      report('widget-missing', `widget:${type}`, namespaceOf(type, metadata.distribution.slug));
    }
  }
  for (const widget of declarations.widgets) {
    if (!registered.has(widget.key)) {
      const key = `widget:${widget.key}`;
      report('widget-not-registered', key, merge.origin(key) ?? '');
    }
  }
}

// -----------------------------------------------------------------------------
// Entities and labels
// -----------------------------------------------------------------------------

/**
 * Checks every merged entity declaration against the Entities chunk.
 *
 * @param context - The check's context.
 * @param entities - The Entities chunk.
 */
function checkEntities(context: CheckContext, entities: TmEntitiesChunk): void {
  const resolver = new TmPathResolver(entities);
  for (const declaration of context.declarations.entities.values()) {
    const key = `entity:${declaration.name}`;
    for (const finding of tmEntityFindings(declaration, resolver)) {
      context.report(finding.code, key, context.merge.origin(key, finding.member) ?? '', finding.facts);
    }
  }
}

/**
 * Warns once for every label key the emit wrote a fallback for.
 *
 * @param context - The check's context.
 * @param entities - The Entities chunk.
 */
function checkLabels(context: CheckContext, entities: TmEntitiesChunk): void {
  const { metadata, report } = context;
  const distribution = metadata.distribution.slug;
  const seen = new Set<string>();
  const warn = (labelKey: string | undefined, fallback: string | undefined, feature: string): void => {
    if (labelKey !== undefined && fallback !== undefined && !seen.has(labelKey)) {
      seen.add(labelKey);
      report('label-missing', `label:${labelKey}`, feature, { fallback });
    }
  };

  // --- Entities, their members and their actions ---
  for (const summary of metadata.entities) {
    warn(summary.labelKey, summary.labelFallback, namespaceOf(summary.name, distribution));
  }
  for (const entity of entities.entities) {
    const feature = namespaceOf(entity.name, distribution);
    warn(entity.labelKey, entity.labelFallback, feature);
    for (const member of [...entity.properties, ...entity.navigations, ...entity.children]) {
      warn(member.labelKey, member.labelFallback, feature);
    }
    for (const action of entity.actions) {
      warn(action.labelKey, action.labelFallback, feature);
      for (const field of action.fields ?? []) {
        warn(field.labelKey, field.labelFallback, feature);
      }
    }
  }
  for (const enumeration of entities.enums) {
    for (const value of enumeration.values) {
      warn(value.labelKey, value.labelFallback, namespaceOf(enumeration.name, distribution));
    }
  }

  // --- Securables and job handlers ---
  for (const securable of metadata.securables) {
    warn(securable.resourceLabelKey, securable.resourceLabelFallback, securable.feature);
    warn(securable.actionLabelKey, securable.actionLabelFallback, securable.feature);
    warn(securable.featureLabelKey, securable.featureLabelFallback, securable.feature);
  }
  for (const handler of metadata.jobHandlers) {
    warn(handler.labelKey, handler.labelFallback, namespaceOf(handler.key, distribution));
  }
}

/**
 * Checks an app's configuration against the client metadata: the merge rule,
 * the page paths, the server facts every declaration names, the entity
 * declarations and the label fallbacks. It reads the config as plain data:
 * no lazy member is called and nothing is instantiated, so it runs in Node.
 *
 * Every problem but `label-missing` is an error; `label-missing` is a
 * warning that prints and passes.
 *
 * @param config - The app's configuration, as `provideTellma` received it.
 * @param metadata - The Core chunk.
 * @param entities - The Entities chunk.
 * @param strings - The loaded string packs by language, when the caller has
 * them; their keys are checked against the client string scopes.
 * @returns Every problem, merge problems first; empty when the config is sound.
 *
 * @example
 * ```ts
 * const problems = tmCheckAppConfig(config, core, entities);
 * const errors = problems.filter((problem) => problem.severity === 'error');
 * ```
 */
export function tmCheckAppConfig(
  config: TmAppConfig,
  metadata: TmClientMetadata,
  entities: TmEntitiesChunk,
  strings: Readonly<Record<string, Readonly<Record<string, string>>>> = {},
): readonly TmConfigProblem[] {
  const problems: TmConfigProblem[] = [];
  const merge = ɵtmMergeAppDeclarations(config.features, metadata);
  const context: CheckContext = {
    config,
    metadata,
    merge,
    declarations: merge.declarations,
    report: (code, key, feature, facts) => problems.push(tmConfigProblem(code, key, feature, facts)),
  };

  // The merge's own problems first, in the order the merge met them.
  for (const problem of merge.problems) {
    context.report(problem.code, problem.key, problem.feature, problem.facts);
  }
  checkServerFacts(context, strings);
  checkRoutes(context);
  checkSections(context);
  checkReportSources(context);
  checkServerKeys(context);
  checkEntities(context, entities);
  checkLabels(context, entities);
  return Object.freeze(problems);
}
