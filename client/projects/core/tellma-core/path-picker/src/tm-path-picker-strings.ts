// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The path picker's English strings, under `tm.pathPicker`. They register
// with `TmLocalization` when the first picker is created, so they ship with
// the picker rather than in the primary's English table; a locale pack's
// table holds their translations under the same keys. The fields' own
// labels are the server's.

/**
 * The path picker's English strings, under `tm.pathPicker`: the search
 * field, the search's announcement, and the date parts' names. Internal:
 * the picker registers them.
 */
export const ɵTM_PATH_PICKER_STRINGS_EN = {
  fields: 'Fields',
  search: 'Search fields',
  count: '{count, plural, one {# field} other {# fields}}',
  none: 'No fields match',
  gregorian: '{part} (Gregorian)',
  part: {
    year: 'Year',
    quarter: 'Quarter',
    month: 'Month',
    week: 'Week',
    day: 'Day',
    quarterOfYear: 'Quarter of year',
    monthOfYear: 'Month of year',
    dayOfWeek: 'Day of week',
  },
} as const satisfies TmStringTree;
