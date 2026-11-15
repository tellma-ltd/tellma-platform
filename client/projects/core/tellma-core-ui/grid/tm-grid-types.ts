// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmRowId } from '@tellma/core-ui/contracts';

/**
 * A row the user opened in a grid with `rowActivation="row"`: a click
 * anywhere in the row, Enter on it, a middle click, or a touch tap. The
 * grid only reports the gesture; the consumer opens the record.
 */
export interface TmGridRowActivation<T> {
  /** The opened row. */
  readonly row: T;
  /** The opened row's identity. */
  readonly rowId: TmRowId;
  /** Whether a pointer (mouse, pen or touch) or the keyboard opened it. */
  readonly via: 'pointer' | 'keyboard';
  /**
   * Whether the user asked for a new tab: Mod (Ctrl, ⌘) was held, or the
   * middle button clicked. The consumer opens the record in a new tab.
   */
  readonly newTab: boolean;
}

/**
 * The one column a grid shows as sorted, in its two-way `orderBy` model.
 * The grid shows and edits the sort; the consumer orders the rows.
 */
export interface TmGridSort {
  /** The sorted column's `key`. */
  readonly key: string;
  /** The sort direction. */
  readonly direction: 'asc' | 'desc';
}

/**
 * How a column's editor opens when it is taller than a row: in a pane over
 * its cell that spills past the grid's edge while every row keeps its
 * height. The pane covers the cell and grows toward the block end (or
 * upward when the viewport has no room below), as wide as the cell or
 * `minWidth`, whichever is wider. Bind it to `tm-grid-column`'s
 * `editorPopup`. The editor is the column's `*tmGridEditor` template, a
 * control that implements `TmCellEditor` — a sub-form of several controls
 * registers as one, as `TmGridEditorDef` shows; on a cell the user cannot
 * edit, F2 opens the pane with the template's `readonly` context set.
 *
 * @example
 * ```html
 * <tm-grid-column key="filter" header="Filter" [editorPopup]="{ minWidth: 480 }">
 *   <app-filter-editor *tmGridEditor="let value; let readonly = readonly" [readonly]="readonly" />
 * </tm-grid-column>
 * ```
 */
export interface TmGridEditorPopup {
  /** The pane's least inline size in px; default 320. */
  readonly minWidth?: number;
  /** The pane's greatest block size in px; default 360. The content scrolls inside beyond it. */
  readonly maxHeight?: number;
}
