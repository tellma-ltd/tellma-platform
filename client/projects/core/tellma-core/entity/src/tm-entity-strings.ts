// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The English strings only this entry point reads. They register with
// `TmLocalization` when the service that reads them is first created
// (`TmFormats` for `tm.value`, `TmEntityPickers` for `tm.picker`,
// `TmEntityActions` for its own `tm.entity` keys), so they ship here rather
// than in the primary's English table; a locale pack's table holds their
// translations under the same keys. The lazily loaded dialogs keep theirs in
// their own chunks (`tm.confirmations`, `tm.actionDialog`).

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
 * The entity pickers' English strings, under `tm.picker`. Internal: the
 * entry point registers them.
 */
export const ɵTM_PICKER_STRINGS_EN = {
  newLabel: '{label}…',
  unavailable: "This page isn't available yet.",
  loadFailed: "This page couldn't be loaded.",
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
