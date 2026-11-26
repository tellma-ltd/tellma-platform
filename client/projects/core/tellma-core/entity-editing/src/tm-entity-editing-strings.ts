// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The English strings only this entry point reads. They register with
// `TmLocalization` when the code that reads them first runs (the picker
// binding and the picker page, for `tm.picker`), so they ship here rather
// than in the primary's English table; a locale pack's table holds their
// translations under the same keys. The typed values' `tm.value` strings,
// which the value editor reads too, are `@tellma/core/entity`'s, and
// register with `TmFormats`.

/**
 * The entity pickers' English strings, under `tm.picker`. Internal: the
 * picker binding and the picker page register them.
 */
export const ɵTM_PICKER_STRINGS_EN = {
  newLabel: '{label}…',
  unavailable: "This page isn't available yet.",
  loadFailed: "This page couldn't be loaded.",
} as const satisfies TmStringTree;
