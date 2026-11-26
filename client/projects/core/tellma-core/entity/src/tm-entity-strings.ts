// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The English strings only this entry point reads. They register with
// `TmLocalization` when the code that reads them first runs (`TmFormats`
// for `tm.value`, `TmEntityActions` for its own `tm.entity` keys, the
// filter describer and control for `tm.filter`), so they ship here rather
// than in the primary's English table; a locale pack's table holds their
// translations under the same keys. The lazily loaded dialogs keep theirs
// in their own chunks (`tm.confirmations`, `tm.actionDialog`), and the
// editing entry point the pickers' (`tm.picker`).

/**
 * The typed values' English strings, under `tm.value`. Internal: the
 * entry point registers them.
 */
export const ɵTM_VALUE_STRINGS_EN = {
  none: '—',
  yes: 'Yes',
  no: 'No',
  file: 'File',
  chooseFile: 'Choose a file',
  fileTooLarge: 'This file is too large. The limit is {max}.',
  fileType: "This type of file isn't accepted.",
  uploadFailed: 'Upload failed',
} as const satisfies TmStringTree;

/**
 * The column filters' English strings, under `tm.filter`: the operators'
 * names, the control's labels, and one whole-sentence description per
 * operator, so a translation orders its words as the language does.
 * Internal: the entry point registers them.
 */
export const ɵTM_FILTER_STRINGS_EN = {
  operator: 'Operator',
  apply: 'Apply',
  clear: 'Clear',
  from: 'From',
  to: 'To',
  true: 'True',
  false: 'False',
  op: {
    contains: 'Contains',
    'starts-with': 'Starts with',
    'ends-with': 'Ends with',
    eq: 'Equals',
    ne: 'Does not equal',
    lt: 'Less than',
    le: 'At most',
    gt: 'Greater than',
    ge: 'At least',
    between: 'Between',
    before: 'Before',
    after: 'After',
    in: 'Is any of',
    is: 'Is',
  },
  describe: {
    contains: '{label} contains {value}',
    'starts-with': '{label} starts with {value}',
    'ends-with': '{label} ends with {value}',
    eq: '{label} is {value}',
    ne: '{label} is not {value}',
    lt: '{label} is less than {value}',
    le: '{label} is at most {value}',
    gt: '{label} is greater than {value}',
    ge: '{label} is at least {value}',
    between: '{label} is between {from} and {to}',
    before: '{label} is before {value}',
    after: '{label} is after {value}',
    in: '{label}: {values}',
    is: '{label}: {value, select, true {Yes} other {No}}',
  },
} as const satisfies TmStringTree;

/**
 * The entity actions' English strings, merged under `tm.entity`: the
 * default confirmation of a destructive action. Internal: the entry point
 * registers them.
 */
export const ɵTM_ENTITY_STRINGS_EN = {
  confirmAction: 'Run “{action}” on {n, plural, one {this item} other {# items}}?',
  confirmActionMessage: "This can't be undone.",
} as const satisfies TmStringTree;
