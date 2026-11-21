// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmAppDeclarations } from '../app/tm-app-declarations';
import type { TmSettingsTabDeclaration } from '../admin/tm-settings-tab';
import type { TmWidgetTypeDeclaration } from '../dashboards/tm-widget';
import type { TmEntityDeclaration } from '../entity/tm-entity-declaration';
import type { TmClientMetadata } from '../metadata/tm-client-metadata';
import type { TmNavEntry, TmNavSection } from '../nav/tm-nav-declarations';
import type { TmShellSlot } from '../nav/tm-shell-slot';
import type { TmCodedReportDeclaration, TmCodedReportMetadata, TmCodedReportPatch } from '../reports/tm-coded-report';
import { TM_CORE_FEATURE, TM_CORE_FEATURE_KEY } from './tm-core-feature';
import { tmDeclarationKindOf } from './tm-declaration-registry';
import type { TmLazyComponent } from './tm-declaration-types';
import type { TmFeature } from './tm-feature';
import type { TmOverride } from './tm-override';
import type { TmPageDeclaration } from './tm-page';

// The merge rule (spec 0024 §2.3). The implicit `core` feature merges first,
// then the app's features in order. Within one feature the merge applies its
// `overrides` and its `hide` to what the earlier features declared, and then
// adds its own declarations; so a feature never patches or hides its own
// keys. Coded reports are the exception: every patch, from `reports` or
// `overrides` alike, merges into the server's entry of its key in feature
// order.
//
// The merge never stops at a problem: it records it, skips the offending
// declaration or patch, and goes on, so one run reports every problem. The
// runtime keeps only the merged declarations; the config check (the
// `@tellma/core/config-check` entry point) words the problems and adds the
// rules that need more than the merge knows.

/** The codes of the problems the merge itself finds. */
export type ɵTmMergeProblemCode =
  | 'duplicate-key'
  | 'dangling-override'
  | 'dangling-hide'
  | 'app-bar-overflow'
  | 'app-bar-reserved-id'
  | 'unknown-entity'
  | 'coded-report-not-registered'
  | 'coded-report-redeclared';

/** One problem the merge found, before it is worded. */
export interface ɵTmMergeProblem {
  /** What is wrong. */
  readonly code: ɵTmMergeProblemCode;
  /** The kind-qualified key of the declaration at fault, such as `page:centers/map`. */
  readonly key: string;
  /** The key of the feature that declares the declaration at fault. */
  readonly feature: string;
  /** The facts the problem's message needs, such as the member a patch may not set. */
  readonly facts: Readonly<Record<string, string | number>>;
}

/** What the merge returns: the merged declarations, and what it learned on the way. */
export interface ɵTmMergeResult {
  /** The merged declarations, with `hide` applied. */
  readonly declarations: TmAppDeclarations;
  /** The problems found, in the order the merge met them. */
  readonly problems: readonly ɵTmMergeProblem[];
  /** The id of every section any feature declared, hidden ones included. */
  readonly declaredSections: ReadonlySet<string>;
  /** The keys of feature-patched coded reports the server declares, each with its patching features in order. */
  readonly reportPatchers: ReadonlyMap<string, readonly string[]>;
  /**
   * The feature behind a declaration or one of its members.
   *
   * @param key - The declaration's kind-qualified key, such as `entity:acme.Customer`.
   * @param member - A member's name; absent for the declaration itself.
   * @returns The feature that declared `key`, or that last set `member` on it;
   * `undefined` when none did.
   */
  origin(key: string, member?: string): string | undefined;
}

/** The app-bar ids the platform reserves for its own slots. */
const RESERVED_APP_BAR_IDS: ReadonlySet<string> = new Set(['help', 'assistant']);

/** The most app-bar slots all features together may add. */
const MAX_APP_BAR_SLOTS = 3;

/** The members of a coded report only the server sets. */
const SERVER_OWNED_REPORT_MEMBERS: ReadonlySet<string> = new Set([
  'feature',
  'route',
  'securable',
  'kind',
  'titleKey',
  'descriptionKey',
  'export',
  'parameters',
]);

/** The kinds `tmOverride` and `hide` address, and the member that holds each kind's key. */
const CLIENT_KINDS = {
  page: 'path',
  section: 'id',
  entry: 'id',
  settingsTab: 'category',
  appBar: 'id',
} as const;

/** A kind `tmOverride` and `hide` address. */
type ClientKind = keyof typeof CLIENT_KINDS;

/** One declaration held by the merge: its current value, and whether `hide` dropped it. */
interface Slot<T> {
  /** The declaration, with every override so far applied. */
  value: T;
  /** Whether a later feature hid it. A hidden key stays taken. */
  hidden: boolean;
}

/**
 * Whether `kind` is a kind `tmOverride` and `hide` address.
 *
 * @param kind - The prefix of a kind-qualified key.
 * @returns Whether it names a client kind.
 */
function isClientKind(kind: string): kind is ClientKind {
  return Object.hasOwn(CLIENT_KINDS, kind);
}

/**
 * The members a declaration or patch sets: its own members whose value is
 * not `undefined`, in their order.
 *
 * @param value - The declaration or patch.
 * @returns The set members' names.
 */
function setMembers(value: object): string[] {
  return Object.keys(value).filter((member) => (value as Record<string, unknown>)[member] !== undefined);
}

/**
 * Whether a page path has a `:name` parameter.
 *
 * @param path - The page's path.
 * @returns Whether any segment is a parameter.
 */
function hasParameter(path: string): boolean {
  return path.split('/').some((segment) => segment.startsWith(':'));
}

/** The state of one merge, from the first feature to the end pass. */
class Merge {
  /** The problems found so far. */
  readonly #problems: ɵTmMergeProblem[] = [];
  /** Who declared each kind-qualified key, and who last set each `key#member`. */
  readonly #origins = new Map<string, string>();
  /** The feature keys merged so far, in order. */
  readonly #features: string[] = [];
  /** The known entities' declarations, by name. */
  readonly #entities = new Map<string, TmEntityDeclaration>();
  /** The widget type declarations, by key. */
  readonly #widgets = new Map<string, TmWidgetTypeDeclaration>();
  /** The client-kind declarations, by kind and key. */
  readonly #client: { readonly [K in ClientKind]: Map<string, Slot<Record<string, unknown>>> } = {
    page: new Map(),
    section: new Map(),
    entry: new Map(),
    settingsTab: new Map(),
    appBar: new Map(),
  };
  /** The ids of the launcher entries entities, pages and coded reports derive, each with its source's key. */
  readonly #derivedEntries = new Map<string, string>();
  /** The derived entry ids `hide` dropped. */
  readonly #hiddenDerivedEntries = new Set<string>();
  /** The merged patch of each coded report the server declares, by key. */
  readonly #reportPatches = new Map<string, Record<string, unknown>>();
  /** The features that patched each coded report, by key. */
  readonly #reportPatchers = new Map<string, string[]>();
  /** The home page's body, from the last feature that set one. */
  #home: TmLazyComponent | null = null;

  /** The Core chunk. */
  readonly #metadata: TmClientMetadata;
  /** The Core chunk's entity summaries, by name. */
  readonly #summaries: ReadonlyMap<string, TmClientMetadata['entities'][number]>;
  /** The Core chunk's coded reports, by key. */
  readonly #reports: ReadonlyMap<string, TmCodedReportMetadata>;

  /**
   * Starts a merge against the metadata's server facts.
   *
   * @param metadata - The Core chunk.
   */
  constructor(metadata: TmClientMetadata) {
    this.#metadata = metadata;
    this.#summaries = new Map(metadata.entities.map((summary) => [summary.name, summary]));
    this.#reports = new Map(metadata.codedReports.map((report) => [report.key, report]));
    // Every coded report the server declares derives a launcher entry under
    // its key, patched or not, so its key is taken before any feature merges.
    for (const report of metadata.codedReports) {
      this.#derivedEntries.set(report.key, `report:${report.key}`);
    }
  }

  /**
   * Records a problem.
   *
   * @param code - What is wrong.
   * @param key - The kind-qualified key at fault.
   * @param feature - The feature at fault.
   * @param facts - The facts the message needs.
   */
  #problem(code: ɵTmMergeProblemCode, key: string, feature: string, facts: Readonly<Record<string, string | number>> = {}): void {
    this.#problems.push({ code, key, feature, facts });
  }

  /**
   * Records who declared a key, and who set each of its members.
   *
   * @param key - The kind-qualified key.
   * @param feature - The declaring feature.
   * @param value - The declaration.
   */
  #declared(key: string, feature: string, value: object): void {
    this.#origins.set(key, feature);
    this.#patched(key, feature, setMembers(value));
  }

  /**
   * Records who last set some of a key's members.
   *
   * @param key - The kind-qualified key.
   * @param feature - The patching feature.
   * @param members - The members it set.
   */
  #patched(key: string, feature: string, members: readonly string[]): void {
    for (const member of members) {
      this.#origins.set(`${key}#${member}`, feature);
    }
  }

  // ---------------------------------------------------------------------------
  // One feature
  // ---------------------------------------------------------------------------

  /**
   * Merges one feature: its overrides and its `hide` against the earlier
   * features, then its own declarations.
   *
   * @param feature - The feature.
   */
  feature(feature: TmFeature): void {
    // A feature key may appear once; a repeat would merge every declaration twice.
    if (this.#features.includes(feature.key)) {
      this.#problem('duplicate-key', `feature:${feature.key}`, feature.key);
      return;
    }
    this.#features.push(feature.key);

    (feature.overrides ?? []).forEach((patch, index) => this.#override(patch, index, feature.key));
    for (const key of feature.hide ?? []) {
      this.#hide(key, feature.key);
    }
    this.#declarations(feature);
    if (feature.home !== undefined) {
      this.#home = feature.home;
    }
  }

  /**
   * Adds one feature's own declarations.
   *
   * @param feature - The feature.
   */
  #declarations(feature: TmFeature): void {
    const key = feature.key;

    // --- Sections first, so the feature's own entries may name them ---
    for (const section of feature.sections ?? []) {
      this.#addClient('section', section.id, section, key);
    }

    // --- Entities: the server decides which exist ---
    for (const entity of feature.entities ?? []) {
      this.#addEntity(entity, key);
    }

    // --- Pages, each with its derived launcher entry ---
    for (const page of feature.pages ?? []) {
      if (this.#addClient('page', page.path, page, key) && !hasParameter(page.path)) {
        this.#derive(page.path, `page:${page.path}`, key);
      }
    }

    // --- Explicit entries, after the derived ones they must not collide with ---
    for (const entry of feature.entries ?? []) {
      this.#addClient('entry', entry.id, entry, key);
    }

    // --- Coded report patches, widget types, settings tabs and app-bar slots ---
    for (const report of feature.reports ?? []) {
      this.#patchReport(report, key);
    }
    for (const widget of feature.widgets ?? []) {
      if (this.#widgets.has(widget.key)) {
        this.#problem('duplicate-key', `widget:${widget.key}`, key);
        continue;
      }
      this.#widgets.set(widget.key, widget);
      this.#declared(`widget:${widget.key}`, key, widget);
    }
    for (const tab of feature.settingsTabs ?? []) {
      this.#addClient('settingsTab', tab.category, tab, key);
    }
    for (const slot of feature.appBar ?? []) {
      this.#addClient('appBar', slot.id, slot, key);
    }
  }

  /**
   * Adds a section, entry, page, settings tab or app-bar slot.
   *
   * @param kind - The declaration's kind.
   * @param id - Its key.
   * @param value - The declaration.
   * @param feature - The declaring feature.
   * @returns Whether it was added; a taken key is `duplicate-key`.
   */
  #addClient(kind: ClientKind, id: string, value: object, feature: string): boolean {
    const slots = this.#client[kind];
    const derivedFrom = kind === 'entry' ? this.#derivedEntries.get(id) : undefined;
    if (slots.has(id) || derivedFrom !== undefined) {
      this.#problem('duplicate-key', `${kind}:${id}`, feature, derivedFrom === undefined ? {} : { takenBy: derivedFrom });
      return false;
    }
    slots.set(id, { value: value as Record<string, unknown>, hidden: false });
    this.#declared(`${kind}:${id}`, feature, value);
    return true;
  }

  /**
   * Adds an entity declaration, with its derived launcher entry when the
   * entity has one.
   *
   * @param entity - The declaration.
   * @param feature - The declaring feature.
   */
  #addEntity(entity: TmEntityDeclaration, feature: string): void {
    const key = `entity:${entity.name}`;
    const summary = this.#summaries.get(entity.name);
    if (summary === undefined) {
      this.#problem('unknown-entity', key, feature, { entity: entity.name });
      return;
    }
    if (this.#entities.has(entity.name)) {
      this.#problem('duplicate-key', key, feature);
      return;
    }
    this.#entities.set(entity.name, entity);
    this.#declared(key, feature, entity);
    // An entity the launcher can open derives an entry under its name.
    if (summary.segment !== null && summary.operations.includes('query')) {
      this.#derive(entity.name, key, feature);
    }
  }

  /**
   * Takes the id of a derived launcher entry.
   *
   * @param id - The entry's id: its source's key.
   * @param source - The source's kind-qualified key.
   * @param feature - The source's feature.
   */
  #derive(id: string, source: string, feature: string): void {
    const taken = this.#derivedEntries.get(id) ?? (this.#client.entry.has(id) ? `entry:${id}` : undefined);
    if (taken !== undefined) {
      this.#problem('duplicate-key', `entry:${id}`, feature, { derivedFrom: source, takenBy: taken });
      return;
    }
    this.#derivedEntries.set(id, source);
  }

  // ---------------------------------------------------------------------------
  // Overrides
  // ---------------------------------------------------------------------------

  /**
   * Applies one entry of a feature's `overrides`.
   *
   * @param patch - The entry.
   * @param index - Its place in the list, for an entry no declaration function made.
   * @param feature - The patching feature.
   */
  #override(patch: unknown, index: number, feature: string): void {
    switch (tmDeclarationKindOf(patch)) {
      case 'entity':
        this.#overrideEntity(patch as TmEntityDeclaration, feature);
        return;
      case 'page': {
        const page = patch as TmPageDeclaration;
        this.#overrideClient('page', page.path, page, feature);
        return;
      }
      case 'report':
        this.#patchReport(patch as TmCodedReportDeclaration, feature);
        return;
      case 'widget':
        this.#overrideWidget(patch as TmWidgetTypeDeclaration, feature);
        return;
      case 'override':
        this.#overrideByKey(patch as TmOverride, feature);
        return;
      case undefined:
        // A copy of a declaration, or any value no declaration function made.
        this.#problem('dangling-override', `override:${index}`, feature, { reason: 'not-a-declaration' });
    }
  }

  /**
   * Applies a `tmOverride`.
   *
   * @param patch - The override.
   * @param feature - The patching feature.
   */
  #overrideByKey(patch: TmOverride, feature: string): void {
    const separator = patch.key.indexOf(':');
    const kind = separator < 0 ? '' : patch.key.slice(0, separator);
    if (!isClientKind(kind)) {
      this.#problem('dangling-override', patch.key, feature, { reason: 'unknown-kind' });
      return;
    }
    const id = patch.key.slice(separator + 1);
    // A derived entry has no declaration of its own: its source is patched instead.
    if (kind === 'entry' && this.#derivedEntries.has(id)) {
      this.#problem('dangling-override', patch.key, feature, { reason: 'derived', source: this.#derivedEntries.get(id) ?? '' });
      return;
    }
    this.#overrideClient(kind, id, patch.members, feature);
  }

  /**
   * Patches a section, entry, page, settings tab or app-bar slot.
   *
   * @param kind - The declaration's kind.
   * @param id - Its key.
   * @param members - The patch, its key member included or not.
   * @param feature - The patching feature.
   */
  #overrideClient(kind: ClientKind, id: string, members: object, feature: string): void {
    const key = `${kind}:${id}`;
    const slot = this.#client[kind].get(id);
    if (slot === undefined || slot.hidden) {
      this.#problem('dangling-override', key, feature, { reason: slot === undefined ? 'missing' : 'hidden' });
      return;
    }
    // The key member names the declaration; a patch may restate it, never change it.
    const keyMember = CLIENT_KINDS[kind];
    const changed = (members as Record<string, unknown>)[keyMember];
    if (changed !== undefined && changed !== id) {
      this.#problem('dangling-override', key, feature, { reason: 'key-member', member: keyMember });
      return;
    }
    const applied = setMembers(members).filter((member) => member !== keyMember);
    slot.value = this.#apply(slot.value, members, applied);
    this.#patched(key, feature, applied);
  }

  /**
   * Patches an earlier entity declaration with a later `tmEntity` value.
   *
   * @param patch - The later declaration.
   * @param feature - The patching feature.
   */
  #overrideEntity(patch: TmEntityDeclaration, feature: string): void {
    const key = `entity:${patch.name}`;
    const earlier = this.#entities.get(patch.name);
    if (earlier === undefined) {
      if (this.#summaries.has(patch.name)) {
        this.#problem('dangling-override', key, feature, { reason: 'missing' });
      } else {
        this.#problem('unknown-entity', key, feature, { entity: patch.name });
      }
      return;
    }
    const applied = setMembers(patch).filter((member) => member !== 'name');
    this.#entities.set(patch.name, this.#apply(earlier, patch, applied));
    this.#patched(key, feature, applied);
  }

  /**
   * Patches an earlier widget type declaration with a later `tmWidget` value.
   *
   * @param patch - The later declaration.
   * @param feature - The patching feature.
   */
  #overrideWidget(patch: TmWidgetTypeDeclaration, feature: string): void {
    const key = `widget:${patch.key}`;
    const earlier = this.#widgets.get(patch.key);
    if (earlier === undefined) {
      this.#problem('dangling-override', key, feature, { reason: 'missing' });
      return;
    }
    const applied = setMembers(patch).filter((member) => member !== 'key');
    this.#widgets.set(patch.key, this.#apply(earlier, patch, applied));
    this.#patched(key, feature, applied);
  }

  /**
   * Merges one `tmCodedReport` patch into the server's entry of its key.
   *
   * @param patch - The patch.
   * @param feature - The patching feature.
   */
  #patchReport(patch: TmCodedReportDeclaration, feature: string): void {
    const key = `report:${patch.key}`;
    if (!this.#reports.has(patch.key)) {
      this.#problem('coded-report-not-registered', key, feature);
      return;
    }
    const patchers = this.#reportPatchers.get(patch.key) ?? [];
    patchers.push(feature);
    this.#reportPatchers.set(patch.key, patchers);

    // A member the server owns is refused; every other set member applies.
    const applied: string[] = [];
    for (const member of setMembers(patch)) {
      if (member === 'key') {
        continue;
      }
      if (SERVER_OWNED_REPORT_MEMBERS.has(member)) {
        this.#problem('coded-report-redeclared', key, feature, { member });
      } else {
        applied.push(member);
      }
    }
    this.#reportPatches.set(patch.key, this.#apply(this.#reportPatches.get(patch.key) ?? {}, patch, applied));
    this.#patched(key, feature, applied);
  }

  /**
   * A frozen copy of a declaration with some of a patch's members replacing
   * its own whole: arrays are replaced, never concatenated.
   *
   * @param earlier - The declaration so far.
   * @param patch - The patch.
   * @param members - The patch's members to apply.
   * @returns The patched copy.
   */
  #apply<T extends object>(earlier: T, patch: object, members: readonly string[]): T {
    const merged = { ...earlier } as Record<string, unknown>;
    for (const member of members) {
      merged[member] = (patch as Record<string, unknown>)[member];
    }
    return Object.freeze(merged) as unknown as T;
  }

  // ---------------------------------------------------------------------------
  // Hide
  // ---------------------------------------------------------------------------

  /**
   * Applies one key of a feature's `hide`.
   *
   * @param key - The kind-qualified key to drop.
   * @param feature - The hiding feature.
   */
  #hide(key: string, feature: string): void {
    const separator = key.indexOf(':');
    const kind = separator < 0 ? '' : key.slice(0, separator);
    if (!isClientKind(kind)) {
      this.#problem('dangling-hide', key, feature, { reason: 'unknown-kind' });
      return;
    }
    const id = key.slice(separator + 1);
    // A derived entry is dropped from the launcher alone; its source stays.
    if (kind === 'entry' && this.#derivedEntries.has(id)) {
      if (this.#hiddenDerivedEntries.has(id)) {
        this.#problem('dangling-hide', key, feature, { reason: 'hidden' });
        return;
      }
      this.#hiddenDerivedEntries.add(id);
      return;
    }
    const slot = this.#client[kind].get(id);
    if (slot === undefined || slot.hidden) {
      this.#problem('dangling-hide', key, feature, { reason: slot === undefined ? 'missing' : 'hidden' });
      return;
    }
    slot.hidden = true;
  }

  // ---------------------------------------------------------------------------
  // The end pass
  // ---------------------------------------------------------------------------

  /**
   * Ends the merge, once every feature merged.
   *
   * @returns The merged declarations, the problems found, and their provenance.
   */
  finish(): ɵTmMergeResult {
    const declarations = this.#result();
    const origins = this.#origins;
    return Object.freeze({
      declarations,
      problems: Object.freeze([...this.#problems]),
      declaredSections: new Set(this.#client.section.keys()),
      reportPatchers: new Map([...this.#reportPatchers].map(([key, patchers]) => [key, Object.freeze([...patchers])])),
      origin: (key: string, member?: string) => origins.get(member === undefined ? key : `${key}#${member}`),
    });
  }

  /**
   * The merged declarations, once every feature merged.
   *
   * @returns The declarations, with `hide` applied.
   */
  #result(): TmAppDeclarations {
    const visible = <T>(kind: ClientKind): T[] =>
      [...this.#client[kind].values()].filter((slot) => !slot.hidden).map((slot) => slot.value as T);

    // --- Hidden sections take every entry under them out of the launcher ---
    const hiddenSections = new Set(
      [...this.#client.section].filter(([, slot]) => slot.hidden).map(([id]) => id),
    );
    const hiddenEntries = new Set(this.#hiddenDerivedEntries);
    for (const [id, slot] of this.#client.entry) {
      if (slot.hidden || hiddenSections.has(String(slot.value['section']))) {
        hiddenEntries.add(id);
      }
    }
    const pages = visible<TmPageDeclaration>('page');
    const reports = this.#metadata.codedReports.map(
      (report): TmCodedReportMetadata & TmCodedReportPatch =>
        Object.freeze({ ...report, ...this.#reportPatches.get(report.key) }),
    );
    const derivedSources: { readonly id: string; readonly section: unknown }[] = [
      ...[...this.#entities.values()].map((entity) => ({ id: entity.name, section: entity['section'] })),
      ...pages.map((page) => ({ id: page.path, section: page.section })),
      ...reports.map((report) => ({ id: report.key, section: report.section })),
    ];
    for (const source of derivedSources) {
      if (typeof source.section === 'string' && hiddenSections.has(source.section) && this.#derivedEntries.has(source.id)) {
        hiddenEntries.add(source.id);
      }
    }

    return Object.freeze({
      entities: new Map(this.#entities),
      pages: Object.freeze(pages),
      reports: Object.freeze(reports),
      widgets: Object.freeze([...this.#widgets.values()]),
      sections: Object.freeze(visible<TmNavSection>('section')),
      entries: Object.freeze(visible<TmNavEntry>('entry').filter((entry) => !hiddenEntries.has(entry.id))),
      hiddenEntries: hiddenEntries as ReadonlySet<string>,
      settingsTabs: Object.freeze(visible<TmSettingsTabDeclaration>('settingsTab')),
      appBar: Object.freeze(this.#appBar(visible<TmShellSlot>('appBar'))),
      features: Object.freeze([...this.#features]),
      home: this.#home,
    });
  }

  /**
   * The app-bar slots the platform allows: no feature takes a reserved id,
   * and the slots beyond the third are dropped.
   *
   * @param slots - The visible slots, in feature order.
   * @returns The allowed slots.
   */
  #appBar(slots: readonly TmShellSlot[]): TmShellSlot[] {
    const allowed: TmShellSlot[] = [];
    let added = 0;
    for (const slot of slots) {
      const feature = this.#origins.get(`appBar:${slot.id}`) ?? '';
      if (RESERVED_APP_BAR_IDS.has(slot.id)) {
        // Only the platform's own feature may fill a reserved slot.
        if (feature !== TM_CORE_FEATURE_KEY) {
          this.#problem('app-bar-reserved-id', `appBar:${slot.id}`, feature);
          continue;
        }
      } else if (++added > MAX_APP_BAR_SLOTS) {
        this.#problem('app-bar-overflow', `appBar:${slot.id}`, feature, { max: MAX_APP_BAR_SLOTS });
        continue;
      }
      allowed.push(slot);
    }
    return allowed;
  }
}

/**
 * Merges an app's features into its declarations: the platform's `core`
 * feature first, then `features` in order. It never throws: a problem is
 * recorded, the declaration or patch at fault is skipped, and the merge goes
 * on. Lazy members are copied, never called.
 *
 * @param features - The app's features, packs first and the distribution last.
 * @param metadata - The Core chunk: the server's entities and coded reports.
 * @param core - The platform's implicit feature.
 * @returns The merged declarations, the problems found, and their provenance.
 */
export function ɵtmMergeAppDeclarations(
  features: readonly TmFeature[],
  metadata: TmClientMetadata,
  core: TmFeature = TM_CORE_FEATURE,
): ɵTmMergeResult {
  const merge = new Merge(metadata);
  for (const feature of [core, ...features]) {
    merge.feature(feature);
  }
  return merge.finish();
}
