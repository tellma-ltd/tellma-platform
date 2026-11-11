// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmKeys } from '@tellma/core-ui/kbd';

import { TM_GRID_KEY_BINDINGS } from './internal/grid-keymap';

/** One key combination the grid handles, with the label of its help row. */
export interface TmGridKeyEntry {
  /**
   * The key combination, in `@tellma/core-ui/kbd`'s grammar: render it with
   * `tm-kbd` or `tmFormatKeys`, which label it for the platform.
   */
  readonly keys: TmKeys;
  /**
   * The full translation key of what the combination does, such as
   * `tmUi.grid.keys.fillDown` ("Fill down"). Entries that share a label key
   * form one help row.
   */
  readonly labelKey: string;
}

/**
 * Every key combination `tm-grid` and `tm-tree-grid` handle while a cell
 * has focus and no editor is open: navigation and selection, editing,
 * clipboard, history and row operations, and the tree's expand and
 * collapse. The list is the grid's own binding table without its actions,
 * so it names exactly what the grid handles.
 *
 * A shortcut registry uses it to refuse an application shortcut that a
 * focused grid would take (any entry with a modifier), and a keyboard help
 * dialog renders it as a list of rows, one per label key.
 *
 * @example
 * ```ts
 * // One help row per label, each listing its combinations
 * // ('Fill down': ['Mod+D']; 'Redo': ['Mod+Y', 'Mod+Shift+Z']).
 * const rows = Map.groupBy(TM_GRID_KEYS, (entry) => entry.labelKey);
 * ```
 */
export const TM_GRID_KEYS: readonly TmGridKeyEntry[] = Object.freeze(
  TM_GRID_KEY_BINDINGS.map(({ keys, labelKey }) => Object.freeze({ keys, labelKey })),
);
