// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// What the bell's and the tray's panels share inside the panels' chunk: the
// data the end drawer opens them with, and the place bucket of an opened
// row, which the `tm.attention.item-opened` event reports (spec 0028 §13).

/** What the panels' host opens a panel in the end drawer with. Internal. */
export interface TmAttentionPanelData {
  /** Whether the panel sits in the drawer, whose title stands in for the panel's heading. */
  readonly drawer: boolean;
}

/**
 * The place bucket of an opened row.
 *
 * @param index - The row's index in its list, counting from 0.
 * @returns `1`, `2-3` or `4-10`.
 */
export function tmItemPosition(index: number): string {
  return index === 0 ? '1' : index < 3 ? '2-3' : '4-10';
}
