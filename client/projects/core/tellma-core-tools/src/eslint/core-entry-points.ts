// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The entry points of @tellma/core and the one graph their static imports
// follow (spec 0024 §1). The table lists every entry point the spec places,
// including those whose owners have not shipped yet, so the graph never needs
// to change shape when one arrives.

/** How an entry point loads: with the app, on demand, or with whichever entry point imports it first. */
export type TmEntryPointLoad = 'eager' | 'lazy' | 'shared';

/** One entry point of `@tellma/core`. */
export interface TmEntryPointRule {
  /** How the entry point loads. */
  readonly load: TmEntryPointLoad;
  /** The entry points it may import statically, besides the ones its kind allows (see `allowedImports`). */
  readonly imports: readonly string[];
}

/** The package whose entry points the table describes. */
export const TM_CORE_PACKAGE = '@tellma/core';

/**
 * The entry points of `@tellma/core`, keyed `.` for the primary and
 * `./<folder>` for a secondary one.
 *
 * - The primary imports no other entry point; `/attention` imports only the
 *   primary; `/shell` the primary and `/attention`.
 * - A shared entry point loads with whichever code imports it first: every
 *   entry point but the primary and the shared ones may import it, so the
 *   primary's first frame never carries it. `/private` is the package's own
 *   wiring between its entry points and may import the primary;
 *   `/search-normalize` is the text folding the launcher and the lazy pages
 *   match with, and imports none.
 * - A lazy entry point may import every eager and shared one, plus the lazy
 *   ones it lists. No eager entry point imports a lazy one.
 */
export const TM_CORE_ENTRY_POINTS: Readonly<Record<string, TmEntryPointRule>> = Object.freeze({
  '.': { load: 'eager', imports: [] },
  './attention': { load: 'eager', imports: ['.'] },
  './shell': { load: 'eager', imports: ['.', './attention'] },
  './private': { load: 'shared', imports: ['.'] },
  // Spec 0027's search normalization, which spec 0027 §1 places in the
  // primary; nothing on the first frame matches text, so it loads with the
  // code that does. The eager entry points may import it, but the size gate's
  // staticGraphExcludes keeps every one of them (`/shell` and `/attention`)
  // from reaching it statically.
  './search-normalize': { load: 'shared', imports: [] },
  './realtime': { load: 'lazy', imports: [] },
  // The support dump reads the page contexts' snapshots.
  './support': { load: 'lazy', imports: ['./page-snapshot'] },
  './entity': { load: 'lazy', imports: [] },
  './sharing': { load: 'lazy', imports: [] },
  // The page hosts load the tab's memory with them: the search page's state
  // and tree expansion, the details page's drafts and state, the stock
  // pages' and the report studio's drafts, a report's state. The home page,
  // in `/dashboards`, lists the recent pages.
  './search': { load: 'lazy', imports: ['./entity', './memory'] },
  './details': { load: 'lazy', imports: ['./entity', './memory'] },
  './admin': { load: 'lazy', imports: ['./memory'] },
  './attention-pages': { load: 'lazy', imports: [] },
  './reports': { load: 'lazy', imports: ['./memory'] },
  './report-studio': { load: 'lazy', imports: ['./memory'] },
  './dashboards': { load: 'lazy', imports: ['./recents'] },
  './config-check': { load: 'lazy', imports: [] },
  // Spec 0025's tab memory, recent pages and leader election, which spec 0025
  // §1 places in the primary; they load with the page hosts and after first
  // render instead.
  './memory': { load: 'lazy', imports: [] },
  './recents': { load: 'lazy', imports: [] },
  './leader': { load: 'lazy', imports: [] },
  // Spec 0026's page-context snapshot, which spec 0026 §1 places in the
  // primary; nothing on the first frame reads it, so the support dump and a
  // future assistant load it with import().
  './page-snapshot': { load: 'lazy', imports: [] },
});

/**
 * The entry points `from` may import statically: those it lists, for a
 * lazy entry point every eager one, and for any entry point other than the
 * primary and the shared ones every shared one.
 */
export function tmAllowedImports(
  table: Readonly<Record<string, TmEntryPointRule>>,
  from: string,
): ReadonlySet<string> {
  const rule = table[from];
  if (rule === undefined) {
    return new Set();
  }
  const allowed = new Set(rule.imports);
  // A shared entry point gets none of the others, so two shared ones can
  // never import each other in a cycle.
  const getsShared = from !== '.' && rule.load !== 'shared';
  for (const [id, other] of Object.entries(table)) {
    if ((rule.load === 'lazy' && other.load === 'eager') || (getsShared && other.load === 'shared')) {
      allowed.add(id);
    }
  }
  return allowed;
}

/**
 * The problems of an entry-point table, empty when it is sound: an import of
 * an entry point the table lacks, an eager or shared entry point that may
 * import a lazy one, and any cycle the allowed imports would allow.
 */
export function tmCheckEntryPoints(table: Readonly<Record<string, TmEntryPointRule>>): string[] {
  const problems: string[] = [];
  const ids = Object.keys(table);

  // Every edge must name a known entry point, and only lazy code may import lazy code.
  for (const id of ids) {
    for (const target of tmAllowedImports(table, id)) {
      if (!(target in table)) {
        problems.push(`'${id}' imports '${target}', which is not an entry point.`);
      } else if (table[id]?.load !== 'lazy' && table[target]?.load === 'lazy') {
        problems.push(`'${id}' loads with the app but imports the lazy '${target}'.`);
      }
    }
  }

  // Depth-first search for a cycle among the allowed edges.
  const state = new Map<string, 'visiting' | 'done'>();
  const path: string[] = [];
  const visit = (id: string): void => {
    if (state.get(id) === 'done') {
      return;
    }
    if (state.get(id) === 'visiting') {
      problems.push(`The imports form a cycle: ${[...path.slice(path.indexOf(id)), id].join(' → ')}.`);
      return;
    }
    state.set(id, 'visiting');
    path.push(id);
    for (const target of tmAllowedImports(table, id)) {
      if (target in table) {
        visit(target);
      }
    }
    path.pop();
    state.set(id, 'done');
  };
  ids.forEach(visit);
  return problems;
}
