// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The entry points of @tellma/core and the one graph their static imports
// follow (spec 0024 §1). The table lists every entry point the spec places,
// including those whose owners have not shipped yet, so the graph never needs
// to change shape when one arrives.

/** How an entry point loads: with the app, on demand, or as shared wiring. */
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
 * - `/private` is shared wiring between the package's own entry points: it
 *   may import the primary, and every entry point but the primary may import
 *   it.
 * - A lazy entry point may import every eager one and `/private`, plus the
 *   lazy ones it lists. No eager entry point imports a lazy one.
 */
export const TM_CORE_ENTRY_POINTS: Readonly<Record<string, TmEntryPointRule>> = Object.freeze({
  '.': { load: 'eager', imports: [] },
  './attention': { load: 'eager', imports: ['.'] },
  './shell': { load: 'eager', imports: ['.', './attention'] },
  './private': { load: 'shared', imports: ['.'] },
  './realtime': { load: 'lazy', imports: [] },
  './support': { load: 'lazy', imports: [] },
  './entity': { load: 'lazy', imports: [] },
  './sharing': { load: 'lazy', imports: [] },
  './search': { load: 'lazy', imports: ['./entity'] },
  './details': { load: 'lazy', imports: ['./entity'] },
  './admin': { load: 'lazy', imports: [] },
  './attention-pages': { load: 'lazy', imports: [] },
  './reports': { load: 'lazy', imports: [] },
  './report-studio': { load: 'lazy', imports: [] },
  './dashboards': { load: 'lazy', imports: [] },
});

/**
 * The entry points `from` may import statically: those it lists, and for a
 * lazy entry point every eager one and `/private`, and for any entry point
 * other than the primary `/private`.
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
  if (rule.load === 'lazy') {
    for (const [id, other] of Object.entries(table)) {
      if (other.load === 'eager') {
        allowed.add(id);
      }
    }
  }
  if (from !== '.' && from !== './private' && './private' in table) {
    allowed.add('./private');
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
