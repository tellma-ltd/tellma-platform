// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmUrls, type TmColumnInput, type TmSearchQuery, type TmSearchSnapshot } from '@tellma/core';
import type { TmColumnSource } from '@tellma/core/entity';

import { tmSearchCanonical, tmSearchPlanColumns, tmSearchQuiet, tmSearchUrl } from './tm-search-canonical';
import {
  TM_SEARCH_DEFAULT_PAGE_SIZE,
  tmSearchDefaultColumns,
  tmSearchPageSizes,
  tmSearchSameColumns,
  tmSearchWidthView,
  type TmSearchSetting,
  type TmSearchState,
} from './tm-search-model';
import { tmSearchColumnsFromPreference } from './tm-search-preferences';

// Where a search page's query state comes from when it opens (spec 0030
// §4.2): a snapshot that supersedes the URL, the URL, the tab's page state,
// then the user's preferences and the code defaults for what is left. A
// drill-through list (§17) reads its fixed filter and its columns from the
// `#d=` payload and keeps its page state per payload; a picker's advanced
// search has no URL and no page state. Back and Forward skip the page state
// (§4.4), and the snapshot that is this page's restores the selection
// whatever the query's source (§15.2).

/** Where the query state a page opened with came from. */
export type TmSearchStateSource = 'supersedes' | 'url' | 'page-state' | 'defaults';

/** What a search page opens over. */
export interface TmSearchOpening {
  /** The URL's query parameters. */
  readonly params?: URLSearchParams;
  /** The raw `location.hash`, with its `#`, never the router's decoded fragment. */
  readonly hash?: string;
  /** This page's snapshot in the tab's page state. */
  readonly snapshot?: TmSearchSnapshot | null;
  /** In picker mode, the text the field held. */
  readonly text?: string;
  /**
   * How the page came to this URL: `open` (the default) for a navigation
   * that opens the page, `history` for Back or Forward, which skips the
   * page state unless its snapshot supersedes the URL.
   */
  readonly navigation?: 'open' | 'history';
}

/** The query state a page opens with, and where it came from. */
export interface TmSearchResolution {
  /** The state. */
  readonly state: TmSearchState;
  /**
   * The state's canonical URL, which replaces the address bar's when they
   * differ; empty, for nothing to write, in picker mode and for a list's
   * invalid payload.
   */
  readonly canonical: string;
  /** Where the state came from. */
  readonly source: TmSearchStateSource;
  /**
   * This page's snapshot in the page state, whatever the query's source:
   * the selection and the expansion restore from it. Its query applied
   * when the source is `supersedes` or `page-state`. `null` when the page
   * has none, or in list mode none under the same payload.
   */
  readonly snapshot: TmSearchSnapshot | null;
  /** In list mode, whether the `#d=` payload is missing or malformed; the state then reads nothing. */
  readonly payloadInvalid: boolean;
}

/** The parameters of the query state; any of them, or a `#d=` payload, makes a URL define the whole query. */
const STATE_PARAMETERS: ReadonlySet<string> = new Set(['tab', 'q', 'inactive', 'order', 'cols', 'page', 'size', 'view', 'list']);

/** Where a state's absent fields come from: the user's preferences, or the code defaults with the view's preference. */
type TmStateOrigin = 'url' | 'snapshot';

/** A drill-through list's payload as a state reads it: its fixed filter, and whether it is missing or malformed. */
interface TmListOf {
  /** The fixed filter; `null` for none; absent outside list mode. */
  readonly filter?: string | null;
  /** Set when the payload is missing or malformed. */
  readonly invalid?: true;
}

/**
 * Resolves the query state a search page opens with, from the first of
 * these that applies:
 *
 * 1. the page state's snapshot, when it supersedes the URL's canonical
 *    query, whole;
 * 2. the URL, when it carries any query-state parameter or a `#d=`
 *    payload, its absent fields from steps 4 and 5;
 * 3. the page state's snapshot, whole, unless the page came here by Back or
 *    Forward;
 * 4. the preferences, for the columns, the page size, the view and the list;
 * 5. the code defaults.
 *
 * Every value the entity cannot have drops, as the canonical query drops
 * it, so the canonical URL may differ from the address bar's. Each drop is
 * reported once, under the source of the state that applies. A list whose
 * `#d=` payload is missing or malformed takes no snapshot and prints no URL.
 *
 * @param setting - The page's setting.
 * @param opening - The URL, the page state and how the page came here.
 * @returns The state, its canonical URL and its source.
 */
export function tmSearchResolve(setting: TmSearchSetting, opening: TmSearchOpening): TmSearchResolution {
  // --- A picker: its own state, from the field's text ---
  if (setting.mode === 'picker') {
    const state = stateOf(setting, opening.text === undefined ? {} : { q: opening.text }, 'url');
    return { state, canonical: '', source: 'defaults', snapshot: null, payloadInvalid: false };
  }

  // --- The URL's query: a page's, or a list's query and payload ---
  const urls = setting.injector.get(TmUrls);
  const params = opening.params ?? new URLSearchParams();
  const hash = opening.hash ?? '';
  let query: TmSearchQuery;
  let list: TmListOf = {};
  if (setting.mode === 'list') {
    const parsed = urls.parseList(params, hash);
    list = parsed.payload === null ? { filter: null, invalid: true } : { filter: parsed.payload.filter ?? null };
    // A list's columns ride its payload, never its query.
    query = { ...parsed.query, cols: parsed.payload?.cols };
  } else {
    query = urls.parseSearch(params, hash);
  }
  // The URL's canonical form, which step 1 compares against; planned
  // quietly, as its drops are reported only when the URL's state applies.
  const urlCanonical = tmSearchUrl(setting, stateOf(tmSearchQuiet(setting), query, 'url', list));

  // --- The page state's snapshot, when it is this page's ---
  const snapshot = opening.snapshot && belongs(setting, opening.snapshot, list) ? opening.snapshot : null;
  const resolved = (state: TmSearchState, source: TmSearchStateSource): TmSearchResolution => ({
    state,
    canonical: tmSearchUrl(setting, state),
    source,
    snapshot,
    payloadInvalid: list.invalid === true,
  });

  // --- The steps, in order ---
  if (snapshot?.supersedes !== undefined && snapshot.supersedes === urlCanonical) {
    return resolved(stateOf(setting, snapshot.query, 'snapshot', list), 'supersedes');
  }
  if (carriesState(setting, params, hash)) {
    return resolved(stateOf(setting, query, 'url', list), 'url');
  }
  // Back and Forward re-resolve from steps 1 and 2 only, so Back steps through pages.
  if (snapshot !== null && opening.navigation !== 'history') {
    return resolved(stateOf(setting, snapshot.query, 'snapshot', list), 'page-state');
  }
  return resolved(stateOf(setting, query, 'url', list), 'defaults');
}

/**
 * Whether a URL carries query state: any of its parameters, or on a search
 * page a `#d=` payload. A drill-through list's payload is its identity, so
 * only its query parameters count.
 *
 * @param setting - The page's setting.
 * @param params - The URL's query parameters.
 * @param hash - The raw `location.hash`.
 * @returns Whether it does.
 */
function carriesState(setting: TmSearchSetting, params: URLSearchParams, hash: string): boolean {
  for (const name of params.keys()) {
    if (STATE_PARAMETERS.has(name) || name.startsWith('f.') || name.startsWith('x.')) {
      return true;
    }
  }
  return setting.mode === 'page' && hash.startsWith('#d=');
}

/**
 * Whether a snapshot is this page's: its entity's, and in list mode under
 * the same fixed filter of a valid payload.
 *
 * @param setting - The page's setting.
 * @param snapshot - The snapshot.
 * @param list - In list mode, the URL's payload.
 * @returns Whether it is.
 */
function belongs(setting: TmSearchSetting, snapshot: TmSearchSnapshot, list: TmListOf): boolean {
  if (snapshot.entity.toLowerCase() !== setting.descriptor.name.toLowerCase()) {
    return false;
  }
  if (setting.mode !== 'list') {
    return true;
  }
  const at = snapshot.canonical.indexOf('#');
  const payload = setting.injector.get(TmUrls).parseList(new URLSearchParams(), at < 0 ? '' : snapshot.canonical.slice(at)).payload;
  return list.invalid !== true && payload !== null && (payload.filter ?? null) === list.filter;
}

/**
 * The state a query opens with. From a URL, the page size, the view and the
 * list choice it leaves out come from the preferences. A URL cannot tell a
 * value left out from one at its code default, so a value at its code
 * default, and one the entity does not offer, read as left out: a canonical
 * URL then resolves to the state it was printed from. A snapshot's query is
 * canonical, so what it leaves out is at its code default, but for the
 * view: the snapshot may come from another width class, so a view it leaves
 * out is the current class's preference half, as after a resize. Either way
 * the columns it leaves out are the preference's, else the code default.
 *
 * @param setting - The page's setting.
 * @param query - The query.
 * @param origin - Where the query came from.
 * @param list - In list mode, the URL's payload.
 * @returns The state.
 */
function stateOf(setting: TmSearchSetting, query: TmSearchQuery, origin: TmStateOrigin, list: TmListOf = {}): TmSearchState {
  const { preferences, descriptor, width } = setting;
  const sized = query.size !== undefined && query.size !== TM_SEARCH_DEFAULT_PAGE_SIZE && tmSearchPageSizes(descriptor).includes(query.size);
  const viewed = query.view !== undefined && query.view !== tmSearchWidthView(width);
  const filled: TmSearchQuery =
    origin === 'snapshot'
      ? { ...query, view: query.view ?? preferences.view[width] }
      : {
          ...query,
          size: sized ? query.size : preferences.pageSize,
          view: viewed ? query.view : preferences.view[width],
          list: query.list ?? preferences.view.list,
        };
  const { columns, from } = columnsOf(setting, query.cols);
  return {
    query: tmSearchCanonical(setting, filled, columns),
    columns,
    columnsFrom: from,
    ...(setting.mode === 'list' && { listFilter: list.filter ?? null }),
    ...(list.invalid && { payloadInvalid: true }),
  };
}

/**
 * The columns a state shows: the query's when they differ from the code
 * default, else in page mode the preference's, else the code default. A picker always shows the code
 * default. Each source's keys are planned once, so a key that does not
 * resolve or that the user may not read drops and is reported under its
 * source, and the rest take their canonical case.
 *
 * @param setting - The page's setting.
 * @param cols - The query's columns.
 * @returns The columns and their source.
 */
function columnsOf(setting: TmSearchSetting, cols: readonly string[] | undefined): { columns: readonly TmColumnInput<string>[]; from: TmColumnSource } {
  const { mode, declaration, preferences } = setting;
  const defaults = tmSearchDefaultColumns(setting.descriptor, declaration);
  if (mode !== 'picker' && cols !== undefined) {
    const columns = tmSearchPlanColumns(setting, cols, 'url');
    // Columns at the code default read as left out, as the canonical URL leaves them out.
    if (columns.length > 0 && !tmSearchSameColumns(columns, defaults)) {
      return { columns, from: 'url' };
    }
  }
  if (mode === 'page' && preferences.columns !== null) {
    const columns = tmSearchPlanColumns(setting, tmSearchColumnsFromPreference(declaration, preferences.columns), 'preference');
    if (columns.length > 0) {
      return { columns, from: 'preference' };
    }
  }
  return { columns: tmSearchPlanColumns(setting, defaults, 'declaration'), from: 'declaration' };
}
