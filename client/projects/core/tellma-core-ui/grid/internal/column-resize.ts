// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { untracked, type Signal, type WritableSignal } from '@angular/core';

/**
 * Fallback minimum column width in px when a column declares no `minWidth`
 * (kept in lock-step with the `--grid-min-col-width` token's default).
 */
export const ɵTM_GRID_MIN_COL_WIDTH = 48;

/** What the resize controller needs to know about the column being resized. */
export interface ɵTmGridResizeColumn {
  /** The column's stable identity (the width-override map key). */
  readonly id: string;
  /** The column's declared minimum width in px, if any. */
  readonly minWidth: number | undefined;
}

/** Construction inputs of {@link ɵTmGridColumnResize}. */
export interface ɵTmGridColumnResizeOptions {
  /** The live width overrides (column id → px) the grid template reads. */
  readonly widthOverrides: WritableSignal<ReadonlyMap<string, number>>;
  /** The reading direction — drag deltas are direction-mapped. */
  readonly direction: Signal<'ltr' | 'rtl'>;
  /** Persists the current widths (called once per completed drag). */
  persist(): void;
}

/**
 * The column-resize pointer controller: a drag on a header's resize handle
 * live-updates the column's width override (converting a proportional
 * column to a fixed px width, the Excel behavior), clamped to the column's
 * minimum, with the delta sign mapped through the reading direction. The
 * handle captures the pointer, so the drag survives leaving the header.
 */
export class ɵTmGridColumnResize {
  constructor(private readonly options: ɵTmGridColumnResizeOptions) {}

  /** Starts a resize drag from a `pointerdown` on the column's resize handle. */
  start(column: ɵTmGridResizeColumn, event: PointerEvent): void {
    if (!event.isPrimary || event.button !== 0) {
      return;
    }
    const handle = event.currentTarget;
    if (!(handle instanceof HTMLElement)) {
      return;
    }
    const header = handle.closest('[data-tm-colhdr]');
    if (header === null) {
      return;
    }
    // Own the gesture: no text selection, no cell-press/column-select path.
    event.preventDefault();
    event.stopPropagation();
    const startX = event.clientX;
    const startWidth =
      untracked(this.options.widthOverrides).get(column.id) ??
      header.getBoundingClientRect().width;
    const min = column.minWidth ?? ɵTM_GRID_MIN_COL_WIDTH;
    const sign = untracked(this.options.direction) === 'rtl' ? -1 : 1;
    // Pin the drag to the pointer that started it: a second touch landing on
    // the handle mid-drag must not perturb the width or end the gesture.
    const pointerId = event.pointerId;
    try {
      handle.setPointerCapture(pointerId);
    } catch {
      // Synthetic events may carry no active pointer; the drag still works
      // as long as the pointer stays over the handle.
    }
    const onMove = (move: PointerEvent): void => {
      if (move.pointerId !== pointerId) {
        return;
      }
      const width = Math.max(min, Math.round(startWidth + (move.clientX - startX) * sign));
      this.options.widthOverrides.update((overrides) => {
        if (overrides.get(column.id) === width) {
          return overrides;
        }
        const next = new Map(overrides);
        next.set(column.id, width);
        return next;
      });
    };
    const onEnd = (end: PointerEvent): void => {
      if (end.pointerId !== pointerId) {
        return;
      }
      handle.removeEventListener('pointermove', onMove);
      handle.removeEventListener('pointerup', onEnd);
      handle.removeEventListener('pointercancel', onEnd);
      this.options.persist();
    };
    handle.addEventListener('pointermove', onMove);
    handle.addEventListener('pointerup', onEnd);
    handle.addEventListener('pointercancel', onEnd);
  }
}
