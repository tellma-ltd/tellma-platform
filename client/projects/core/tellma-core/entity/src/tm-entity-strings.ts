// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The English strings only this entry point reads. They register with
// `TmLocalization` when `TmFormats` is first created, so they ship here
// rather than in the primary's English table; a locale pack's table holds
// their translations under the same keys.

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
