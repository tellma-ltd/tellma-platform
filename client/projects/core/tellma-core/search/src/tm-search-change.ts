// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmColumnInput, TmColumnPreference, TmSearchQuery } from '@tellma/core';

import { tmSearchCanonical, tmSearchPlanColumns, tmSearchUrl } from './tm-search-canonical';
import {
  tmSearchDefaultColumns,
  tmSearchEffective,
  tmSearchPageForSize,
  tmSearchSameColumns,
  tmSearchWidthView,
  type TmSearchSetting,
  type TmSearchState,
  type TmSearchViewPreference,
  type TmSearchWidth,
} from './tm-search-model';
import { tmSearchColumnsToPreference, tmSearchViewHalf, tmSearchViewList } from './tm-search-preferences';

// How a query change reaches the URL, the history and the preferences
// (spec 0030 §4.4, §14). Only a page change pushes a history entry; every
// other change replaces the current one. A change of the text, a filter, the
// tab, Active only or the order goes back to the first page; a page-size
// change keeps the first visible row; tree to list and back go to the first
// page. The columns, the page size and the view are the user's preferences,
// so their changes come back as preference writes.

/** How a change reaches the browser's history. */
export type TmSearchHistory = 'push' | 'replace';

/** The preferences a change writes: each one present is set, `null` columns deleting the key. */
export interface TmSearchPreferenceWrites {
  /** The shown columns; `null` when they are the code default again. */
  readonly columns?: readonly TmColumnPreference[] | null;
  /** The view halves and the list choice. */
  readonly view?: TmSearchViewPreference;
  /** The page size. */
  readonly pageSize?: number;
}

/** A change's outcome: the new state, its URL, how the URL reaches history, and the preferences to write. */
export interface TmSearchTransition {
  /** The new state. */
  readonly state: TmSearchState;
  /** The new state's canonical URL; empty in picker mode. */
  readonly canonical: string;
  /** How the URL reaches history; `null` when the URL is unchanged or the page has none. */
  readonly history: TmSearchHistory | null;
  /** The preferences to write. */
  readonly preferences: TmSearchPreferenceWrites;
}

/** The fields whose change sends the page back to the first page. */
const RESET_FIELDS: readonly (keyof TmSearchQuery)[] = ['tab', 'q', 'columnFilters', 'filter', 'inactive', 'custom', 'order'];

/** Every field of a query, but the columns, which change by their own rule. */
const QUERY_FIELDS: readonly (keyof TmSearchQuery)[] = [...RESET_FIELDS, 'page', 'size', 'view', 'list'];

/** A query's mutable form. */
type TmMutableQuery = { -readonly [K in keyof TmSearchQuery]: TmSearchQuery[K] };

/**
 * Applies a change of the query state under the history and paging rules.
 * A field the patch holds as `undefined` goes back to its default. The
 * page goes back to the first on a change of the text, the tab, a filter,
 * Active only, a custom filter or the order, and on tree to list and back;
 * a page-size change keeps the first visible row; a patch that names the
 * page sets it as given. A patch with `cols` changes the shown columns as
 * {@link tmSearchSetColumns} does.
 *
 * @param setting - The page's setting.
 * @param state - The current state.
 * @param patch - The fields to change.
 * @returns The transition.
 *
 * @example
 * ```ts
 * tmSearchUpdate(setting, state, { q: 'jed' });            // page 1, a replace
 * tmSearchUpdate(setting, state, { page: 2 });             // a push
 * tmSearchUpdate(setting, state, { columnFilters: undefined, filter: undefined }); // Clear all
 * ```
 */
export function tmSearchUpdate(setting: TmSearchSetting, state: TmSearchState, patch: Partial<TmSearchQuery>): TmSearchTransition {
  // --- The columns, when the patch names them ---
  const columns =
    'cols' in patch
      ? shownColumns(setting, patch.cols === undefined ? tmSearchDefaultColumns(setting.descriptor, setting.declaration) : patch.cols)
      : state.columns;

  // --- The patched query, before the paging rules ---
  const next: TmMutableQuery = { ...state.query };
  for (const field of QUERY_FIELDS) {
    if (field in patch) {
      // A present field set to undefined goes back to its default.
      (next as Record<string, unknown>)[field] = patch[field];
    }
  }
  const provisional = tmSearchCanonical(setting, next, columns);
  const changed = (field: keyof TmSearchQuery): boolean => !sameValue(state.query[field], provisional[field]);

  // --- The paging rules, unless the patch names the page ---
  const before = tmSearchEffective(setting, state.query);
  const after = tmSearchEffective(setting, provisional);
  if (!('page' in patch)) {
    if (RESET_FIELDS.some(changed) || before.tree !== after.tree) {
      next.page = undefined;
    } else if (before.size !== after.size) {
      next.page = tmSearchPageForSize(before.page, before.size, after.size);
    }
  }
  const query = tmSearchCanonical(setting, next, columns);
  return transition(setting, state, { ...state, query, ...columnsState(setting, state, columns) }, query, patch);
}

/**
 * Applies a whole query state, as the anchor does when the text returns to
 * it: no paging rule, a replace, and no preference written.
 *
 * @param setting - The page's setting.
 * @param state - The current state.
 * @param query - The query to apply.
 * @returns The transition.
 */
export function tmSearchApply(setting: TmSearchSetting, state: TmSearchState, query: TmSearchQuery): TmSearchTransition {
  const next = { ...state, query: tmSearchCanonical(setting, query, state.columns) };
  return { state: next, canonical: tmSearchUrl(setting, next), history: urlHistory(setting, state, next, 'replace'), preferences: {} };
}

/**
 * Changes the shown columns: the page is kept, the URL replaced when its
 * `cols` change, and in page mode the column preference written, deleted
 * when the columns are the code default again. A drill-through list keeps
 * its columns in its payload and never writes the preference.
 *
 * @param setting - The page's setting.
 * @param state - The current state.
 * @param columns - The new shown columns.
 * @returns The transition.
 */
export function tmSearchSetColumns(setting: TmSearchSetting, state: TmSearchState, columns: readonly TmColumnInput<string>[]): TmSearchTransition {
  return columnsTransition(setting, state, shownColumns(setting, columns));
}

/**
 * Follows a resize that crossed `md`: the view is the new width class's
 * preference half, else its default, and the URL is replaced when its
 * `view` changes. Nothing else changes and no preference is written.
 *
 * @param setting - The page's setting, at the new width.
 * @param state - The current state.
 * @param from - The width class before the resize.
 * @returns The transition.
 */
export function tmSearchResize(setting: TmSearchSetting, state: TmSearchState, from: TmSearchWidth): TmSearchTransition {
  if (from === setting.width) {
    return { state, canonical: tmSearchUrl(setting, state), history: null, preferences: {} };
  }
  const view = setting.preferences.view[setting.width] ?? tmSearchWidthView(setting.width);
  const next = { ...state, query: tmSearchCanonical(setting, { ...state.query, view }, state.columns) };
  // The URL before the resize printed the view against the old width's default.
  const previous = tmSearchUrl({ ...setting, width: from }, state);
  const canonical = tmSearchUrl(setting, next);
  return { state: next, canonical, history: setting.mode === 'picker' || canonical === previous ? null : 'replace', preferences: {} };
}

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

/**
 * The columns a change shows: planned against the entity, each key in its
 * canonical case; the code default when none is left.
 *
 * @param setting - The page's setting.
 * @param columns - The requested columns.
 * @returns The columns.
 */
function shownColumns(setting: TmSearchSetting, columns: readonly TmColumnInput<string>[]): readonly TmColumnInput<string>[] {
  const source = setting.mode === 'list' ? 'url' : 'preference';
  const planned = tmSearchPlanColumns(setting, columns, source);
  return planned.length > 0 ? planned : tmSearchPlanColumns(setting, tmSearchDefaultColumns(setting.descriptor, setting.declaration), 'declaration');
}

/**
 * The column members of a state that shows a column list.
 *
 * @param setting - The page's setting.
 * @param state - The current state.
 * @param columns - The shown columns.
 * @returns The columns and their source.
 */
function columnsState(setting: TmSearchSetting, state: TmSearchState, columns: readonly TmColumnInput<string>[]): Pick<TmSearchState, 'columns' | 'columnsFrom'> {
  if (columns === state.columns) {
    return { columns, columnsFrom: state.columnsFrom };
  }
  if (tmSearchSameColumns(columns, tmSearchDefaultColumns(setting.descriptor, setting.declaration))) {
    return { columns, columnsFrom: 'declaration' };
  }
  return { columns, columnsFrom: setting.mode === 'list' ? 'url' : 'preference' };
}

/**
 * A change of the shown columns alone.
 *
 * @param setting - The page's setting.
 * @param state - The current state.
 * @param columns - The new columns, planned.
 * @returns The transition.
 */
function columnsTransition(setting: TmSearchSetting, state: TmSearchState, columns: readonly TmColumnInput<string>[]): TmSearchTransition {
  const next: TmSearchState = { ...state, query: tmSearchCanonical(setting, state.query, columns), ...columnsState(setting, state, columns) };
  return transition(setting, state, next, next.query, {});
}

/**
 * A change's transition: its URL, its history, and the preferences it
 * writes.
 *
 * @param setting - The page's setting.
 * @param state - The state before.
 * @param next - The state after.
 * @param query - The state's query after.
 * @param patch - The patch, which names the fields the user changed.
 * @returns The transition.
 */
function transition(setting: TmSearchSetting, state: TmSearchState, next: TmSearchState, query: TmSearchQuery, patch: Partial<TmSearchQuery>): TmSearchTransition {
  const before = tmSearchEffective(setting, state.query);
  const after = tmSearchEffective(setting, query);

  // --- Only a change of the page alone pushes ---
  const onlyPage = 'page' in patch && QUERY_FIELDS.every((field) => field === 'page' || sameValue(state.query[field], query[field]));
  const history = urlHistory(setting, state, next, onlyPage && tmSearchSameColumns(state.columns, next.columns) ? 'push' : 'replace');

  // --- The preferences the change writes ---
  const preferences: { -readonly [K in keyof TmSearchPreferenceWrites]: TmSearchPreferenceWrites[K] } = {};
  if (!tmSearchSameColumns(state.columns, next.columns) && setting.mode === 'page') {
    preferences.columns = next.columnsFrom === 'declaration' ? null : tmSearchColumnsToPreference(next.columns);
  }
  if (before.size !== after.size) {
    preferences.pageSize = after.size;
  }
  let view = setting.preferences.view;
  if (before.view !== after.view) {
    view = tmSearchViewHalf(view, setting.width, after.view, tmSearchWidthView(setting.width));
  }
  if ((state.query.list === true) !== (query.list === true)) {
    view = tmSearchViewList(view, query.list === true);
  }
  if (view !== setting.preferences.view) {
    preferences.view = view;
  }
  return { state: next, canonical: tmSearchUrl(setting, next), history, preferences };
}

/**
 * How a state change reaches history: not at all when the URL is
 * unchanged or the page has none.
 *
 * @param setting - The page's setting.
 * @param state - The state before.
 * @param next - The state after.
 * @param kind - How a changed URL reaches history.
 * @returns The history, or `null`.
 */
function urlHistory(setting: TmSearchSetting, state: TmSearchState, next: TmSearchState, kind: TmSearchHistory): TmSearchHistory | null {
  return setting.mode === 'picker' || tmSearchUrl(setting, state) === tmSearchUrl(setting, next) ? null : kind;
}

/**
 * Whether two values of a query field are equal: by value for the lists
 * and the records.
 *
 * @param a - One value.
 * @param b - The other.
 * @returns Whether they are equal.
 */
function sameValue(a: unknown, b: unknown): boolean {
  return a === b || JSON.stringify(a) === JSON.stringify(b);
}
