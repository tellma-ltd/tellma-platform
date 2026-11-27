// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The Filters popup's own English strings, under `tm.searchFilters`. The
// filter panels register them as their chunk first runs, so they ship with
// the panels rather than with the search route; the page's strings the
// popup shares (its title, the tabs' name) stay under `tm.search`. A locale
// pack's table holds their translations under the same keys.

/**
 * The Filters popup's English strings, under `tm.searchFilters`. Internal:
 * the filter panels register them.
 */
export const ɵTM_SEARCH_FILTERS_STRINGS_EN = {
  // The sections: the expression's name, Active only, the options filters' first option, the chips' name, and Add column filter.
  filterExpression: 'Filter expression',
  activeOnly: 'Active only',
  any: 'Any',
  appliedFilters: 'Applied filters',
  addColumnFilter: 'Add column filter',
  // An order on a column the page does not show, as its chip reads.
  orderedBy: 'Ordered by {label},{direction, select, asc { ascending} other { descending}}',
  // A column filter in place goes Back to the sections; the footer's buttons.
  back: 'Back',
  clearAll: 'Clear all',
  apply: 'Apply',
} as const satisfies TmStringTree;
