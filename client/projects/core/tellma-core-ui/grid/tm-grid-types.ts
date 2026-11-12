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
