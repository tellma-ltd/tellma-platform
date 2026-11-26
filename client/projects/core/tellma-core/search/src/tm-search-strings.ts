// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The search page's English strings, under `tm.search` (spec 0030 §1). The
// host registers them with `TmLocalization` when it first runs, so they ship
// in the route's chunk rather than in the primary's English table; a locale
// pack's table holds their translations under the same keys. The copy of
// the results region's states is entity-neutral (§5.3), so no plural label
// is lower-cased mid-sentence.

/**
 * The search page's English strings, under `tm.search`. Internal: the host
 * registers them.
 */
export const ɵTM_SEARCH_STRINGS_EN = {
  // The label a user's expression column takes, whose header shows the expression itself.
  expressionColumn: 'Expression',
  // The toolbar's controls and the grid's row-end button.
  refresh: 'Refresh',
  more: 'More',
  moreActions: 'More actions',
  chooseColumns: 'Choose columns',
  loading: 'Loading results',
  // The search field: its name, the hint that describes it, and its clear button's name (clearSearch).
  search: 'Search',
  searchHint: 'Arrow keys move through the results. Enter opens one.',
  // The row context menu's own items, and what copying a row's link says.
  openInNewTab: 'Open in new tab',
  copyLink: 'Copy link',
  linkCopied: 'Link copied',
  linkCopyFailed: "The link couldn't be copied",
  // What the live region says once a result settles.
  results: '{count, plural, one {# result} other {# results}}',
  resultsAtLeast: '{count}+ results',
  offline: "You're offline",
  // The results region's states.
  noData: 'Nothing here yet',
  noDataMessage: 'Add one to get started.',
  noMatches: 'No matches',
  noMatchesMessage: 'Clear the filters or widen the search.',
  clearFilters: 'Clear filters',
  clearSearch: 'Clear search',
  invalidFilter: 'This filter expression is not valid',
  invalidLink: "This link's filter is not valid",
  calculation: "A column or filter can't be calculated for some rows",
  divideByZero: 'It divides by zero.',
  overflow: 'A result is too large.',
  failed: "The results couldn't be loaded.",
  // The page actions without a control of their own.
  query: 'Search',
  open: 'Open',
} as const satisfies TmStringTree;
