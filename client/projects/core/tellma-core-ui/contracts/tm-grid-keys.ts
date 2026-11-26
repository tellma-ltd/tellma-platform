// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The grid's key combinations as plain data, for a keyboard help dialog or a
// shortcut registry that must not load the grid itself. The grid's binding
// table (grid/internal/grid-keymap.ts) pairs each of these with its intent;
// a unit test there holds the two equal, in the same order, so they cannot
// drift.

/** One key combination the grid handles, with the label of its help row. */
export interface TmGridKeyEntry {
  /**
   * The key combination, in `@tellma/core-ui/kbd`'s grammar: render it with
   * `tm-kbd` or `tmFormatKeys`, which label it for the platform.
   */
  readonly keys: string;
  /**
   * The full translation key of what the combination does, such as
   * `tmUi.grid.keys.fillDown` ("Fill down"). Entries that share a label key
   * form one help row.
   */
  readonly labelKey: string;
}

/** The four physical arrows, in the binding table's order. */
const ARROWS = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'];

/** The entries of one help label, frozen, in the order given. */
function entries(name: string, ...keys: readonly string[]): readonly TmGridKeyEntry[] {
  return keys.map((combination) => Object.freeze({ keys: combination, labelKey: `tmUi.grid.keys.${name}` }));
}

/** The four arrows behind one modifier prefix, under one help label. */
function arrows(prefix: string, name: string): readonly TmGridKeyEntry[] {
  return entries(name, ...ARROWS.map((arrow) => `${prefix}${arrow}`));
}

/**
 * Every key combination `tm-grid` and `tm-tree-grid` handle while a cell
 * has focus and no editor is open: navigation and selection, editing,
 * clipboard, history and row operations, and the tree's expand and
 * collapse. It is the grid's own binding table without its actions, so it
 * names exactly what the grid handles, and it carries none of the grid's
 * code.
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
export const TM_GRID_KEYS: readonly TmGridKeyEntry[] = Object.freeze([
  // Section: moving the active cell.
  ...arrows('', 'move'),
  ...arrows('Mod+', 'jump'),
  ...entries('page', 'PageUp', 'PageDown'),
  ...entries('rowEdge', 'Home', 'End'),
  ...entries('gridEdge', 'Mod+Home', 'Mod+End'),
  // Section: extending the selection.
  ...arrows('Shift+', 'extend'),
  ...arrows('Mod+Shift+', 'extend'),
  ...entries('extend', 'Shift+PageUp', 'Shift+PageDown', 'Shift+Home', 'Shift+End', 'Mod+Shift+Home', 'Mod+Shift+End'),
  // Section: whole rows, columns and the grid.
  ...entries('selectRows', 'Shift+Space'),
  ...entries('selectColumns', 'Ctrl+Space'),
  ...entries('checkAll', 'Ctrl+Shift+Space'),
  ...entries('checkRow', 'Space'),
  ...entries('selectAll', 'Mod+A'),
  // Section: editing and the cell's own actions.
  ...entries('tab', 'Tab', 'Shift+Tab'),
  ...entries('enter', 'Enter'),
  ...entries('openRowNewTab', 'Mod+Enter'),
  ...entries('moveUp', 'Shift+Enter'),
  ...entries('edit', 'F2'),
  ...entries('openDropdown', 'Alt+ArrowDown'),
  ...entries('clear', 'Delete', 'Backspace'),
  ...entries('escape', 'Escape'),
  ...entries('menu', 'ContextMenu', 'Shift+F10'),
  ...entries('find', 'Mod+F'),
  // Section: clipboard, history and rows.
  ...entries('copy', 'Mod+C'),
  ...entries('cut', 'Mod+X'),
  ...entries('paste', 'Mod+V'),
  ...entries('fillDown', 'Mod+D'),
  ...entries('undo', 'Mod+Z'),
  ...entries('redo', 'Mod+Y', 'Mod+Shift+Z'),
  ...entries('deleteRows', 'Mod+Alt+Minus'),
  ...entries('insertRows', 'Mod+Alt+Plus', 'Mod+Alt+='),
  // Section: the tree's additions, then the header row's.
  ...entries('expand', 'Alt+ArrowRight'),
  ...entries('collapse', 'Alt+ArrowLeft'),
  ...entries('headerAction', 'Alt+ArrowDown'),
]);
