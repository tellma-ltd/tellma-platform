// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The problems the config check reports (spec 0024 §3.6, Appendix A), and
// their wording. Each check records a code, a kind-qualified key and the
// facts that explain it; this file turns the facts into the message a
// developer reads in the build's output or the development error.

/** What a configuration problem is: one of the codes the config check reports. */
export type TmConfigProblemCode =
  | 'duplicate-key'
  | 'dangling-override'
  | 'dangling-hide'
  | 'unknown-section'
  | 'app-bar-overflow'
  | 'app-bar-reserved-id'
  | 'invalid-path'
  | 'reserved-path'
  | 'invalid-report-key'
  | 'coded-report-not-registered'
  | 'coded-report-redeclared'
  | 'coded-report-source'
  | 'feature-not-composed'
  | 'unknown-entity'
  | 'language-missing'
  | 'language-not-offered'
  | 'default-language-mismatch'
  | 'calendar-missing'
  | 'string-key-collision'
  | 'unknown-setting-category'
  | 'widget-missing'
  | 'widget-not-registered'
  | 'unknown-path'
  | 'unknown-action'
  | 'operation-missing'
  | 'entity-not-routable'
  | 'label-missing';

/** One problem of an app's configuration against the client metadata. */
export interface TmConfigProblem {
  /** What is wrong. */
  readonly code: TmConfigProblemCode;
  /** `warning` for a problem that prints and passes; every other problem fails the build. */
  readonly severity: 'error' | 'warning';
  /** The kind-qualified key of the thing at fault, such as `page:centers/map`. */
  readonly key: string;
  /** The key of the feature at fault, such as `acme`. */
  readonly feature: string;
  /** What is wrong, in a sentence a developer can act on. */
  readonly message: string;
}

/** The facts a problem's message needs, by name. */
export type TmProblemFacts = Readonly<Record<string, string | number>>;

/**
 * The part of a kind-qualified key after its kind.
 *
 * @param key - The key, such as `page:credit-review`.
 * @returns The part after the first colon, such as `credit-review`.
 */
function idOf(key: string): string {
  return key.slice(key.indexOf(':') + 1);
}

/**
 * One fact as text, empty when it is absent.
 *
 * @param facts - The facts.
 * @param name - The fact's name.
 * @returns The fact.
 */
function fact(facts: TmProblemFacts, name: string): string {
  return String(facts[name] ?? '');
}

/** Writes the message of each code from the problem's key and facts. */
const MESSAGES: { readonly [C in TmConfigProblemCode]: (key: string, facts: TmProblemFacts) => string } = {
  // --- The merge rule ---
  'duplicate-key': (key, facts) => {
    if (facts['derivedFrom'] !== undefined) {
      return `The launcher entry '${key}' that '${fact(facts, 'derivedFrom')}' derives is already taken by '${fact(facts, 'takenBy')}'.`;
    }
    if (facts['takenBy'] !== undefined) {
      return `'${key}' is already taken by the launcher entry that '${fact(facts, 'takenBy')}' derives.`;
    }
    return `'${key}' is declared more than once; a later feature changes an earlier declaration only through overrides and hide.`;
  },
  'dangling-override': (key, facts) => {
    switch (facts['reason']) {
      case 'hidden':
        return `The override of '${key}' names a declaration that an earlier feature hid.`;
      case 'derived':
        return `'${key}' is the launcher entry that '${fact(facts, 'source')}' derives; patch '${fact(facts, 'source')}' instead.`;
      case 'key-member':
        return `The override of '${key}' changes its key member '${fact(facts, 'member')}'; a declaration's key cannot change.`;
      case 'unknown-kind':
        return `'${key}' names no kind an override patches: page, section, entry, settingsTab or appBar.`;
      case 'not-a-declaration':
        return `Entry ${idOf(key)} of overrides was not made by tmEntity, tmPage, tmCodedReport, tmWidget or tmOverride.`;
      default:
        return `The override of '${key}' names no declaration of an earlier feature.`;
    }
  },
  'dangling-hide': (key, facts) => {
    switch (facts['reason']) {
      case 'hidden':
        return `'${key}' is hidden already.`;
      case 'unknown-kind':
        return `'${key}' names no kind hide drops: page, section, entry, settingsTab or appBar.`;
      default:
        return `hide names '${key}', which no earlier feature declares.`;
    }
  },
  'unknown-section': (key, facts) => `'${key}' names the section '${fact(facts, 'section')}', which no feature declares.`,
  'app-bar-overflow': (key, facts) =>
    `'${key}' is beyond the ${fact(facts, 'max')} app-bar slots all features together may add, so it is dropped.`,
  'app-bar-reserved-id': (key) => `'${key}' takes an id the platform reserves for its own app-bar slot, so it is dropped.`,

  // --- Paths and coded reports ---
  'invalid-path': (key, facts) => {
    const segment = fact(facts, 'segment');
    switch (facts['reason']) {
      case 'empty':
        return `The path of '${key}' has an empty segment.`;
      case 'first-parameter':
        return `The path of '${key}' starts with the parameter '${segment}'; its first segment must be a literal.`;
      case 'new':
        return `The path of '${key}' has the segment 'new', which the platform reserves for create pages.`;
      case 'duplicate-parameter':
        return `The path of '${key}' names the parameter '${segment}' twice.`;
      case 'section-with-parameter':
        return `'${key}' has a parameter and a section; a page with a parameter has no launcher entry, so it takes no section.`;
      default:
        return `The segment '${segment}' of '${key}' is neither a kebab-case literal nor a camelCase ':name' parameter.`;
    }
  },
  'reserved-path': (key, facts) =>
    facts['reason'] === 'entity'
      ? `'${key}' starts with '${fact(facts, 'segment')}', the route segment of the entity '${fact(facts, 'entity')}', which another feature declares.`
      : `'${key}' starts with '${fact(facts, 'segment')}', a word the platform's routes reserve.`,
  'invalid-report-key': (key) => `The coded report key '${idOf(key)}' is not kebab-case with at least one hyphen.`,
  'coded-report-not-registered': (key) => `No coded report the server declares has the key '${idOf(key)}'.`,
  'coded-report-redeclared': (key, facts) =>
    `A patch of '${key}' sets '${fact(facts, 'member')}', which the server declares; the patch's other members apply.`,
  'coded-report-source': (key, facts) => {
    switch (facts['reason']) {
      case 'route-with-run':
        return `'${key}' has a server route, so no patch may give it 'run'.`;
      case 'both':
        return `'${key}' has no server route, and its patches give it both 'run' and 'component'; give it exactly one.`;
      default:
        return `'${key}' has no server route, so a patch must give it 'run' or 'component'.`;
    }
  },

  // --- Server facts ---
  'feature-not-composed': (key) => `The server composes no feature '${idOf(key)}'.`,
  'unknown-entity': (key) => `The metadata has no entity '${idOf(key)}'.`,
  'language-missing': (key) => `The server offers the language '${idOf(key)}', but the app's languages have no definition for it.`,
  'language-not-offered': (key) => `The app defines the language '${idOf(key)}', which the server does not offer.`,
  'default-language-mismatch': (_key, facts) =>
    `The app's first language is '${fact(facts, 'client')}', but the server's default is '${fact(facts, 'server')}'.`,
  'calendar-missing': (key) =>
    `The server registers the calendar '${idOf(key)}', which the platform lacks and the app's calendars do not define.`,
  'string-key-collision': (key, facts) =>
    `The ${fact(facts, 'language')} string pack has the key '${idOf(key)}', which collides with the client string scopes 'tm' and 'tmUi'.`,
  'unknown-setting-category': (key) => `'${key}' names no setting category the server declares.`,
  'widget-missing': (key) => `The server registers the widget type '${idOf(key)}', which no feature declares.`,
  'widget-not-registered': (key) => `'${key}' declares a widget type the server does not register.`,

  // --- Entity declarations ---
  'unknown-path': (key, facts) => `The ${fact(facts, 'member')} path '${fact(facts, 'path')}' of '${key}' does not resolve.`,
  'unknown-action': (key, facts) => `'${key}' declares the action '${fact(facts, 'action')}', which the entity's metadata does not list.`,
  'operation-missing': (key, facts) =>
    `'${key}' sets '${fact(facts, 'member')}', which needs the '${fact(facts, 'operation')}' operation the entity lacks.`,
  'entity-not-routable': (key, facts) =>
    facts['reason'] === 'no-segment'
      ? `'${key}' sets a section, but the entity has no route segment.`
      : `'${key}' sets a section, but the entity has no 'query' operation.`,
  'label-missing': (key, facts) =>
    `No string resource declares '${idOf(key)}', so the emit wrote the fallback '${fact(facts, 'fallback')}'.`,
};

/**
 * Words one problem.
 *
 * @param code - What is wrong.
 * @param key - The kind-qualified key at fault.
 * @param feature - The feature at fault.
 * @param facts - The facts that explain it.
 * @returns The problem, with its severity and message.
 */
export function tmConfigProblem(code: TmConfigProblemCode, key: string, feature: string, facts: TmProblemFacts = {}): TmConfigProblem {
  return Object.freeze({
    code,
    severity: code === 'label-missing' ? 'warning' : 'error',
    key,
    feature,
    message: MESSAGES[code](key, facts),
  });
}
