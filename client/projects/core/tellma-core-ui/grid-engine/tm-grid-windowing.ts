// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Inputs of one windowing computation over a single axis of fixed-size
 * items. Deliberately shape-neutral — it knows nothing about rows, columns,
 * or pinning — so any fixed-item-size virtualized axis can reuse it.
 */
export interface TmAxisWindowArgs {
  /** The scroll offset along the axis, in px. */
  readonly scrollOffset: number;
  /** The viewport size along the axis, in px. */
  readonly viewportSize: number;
  /** The fixed per-item size, in px (> 0). */
  readonly itemSize: number;
  /** The total item count. */
  readonly itemCount: number;
  /** Extra items rendered on each side of the visible slice. */
  readonly overscan: number;
}

/** The computed window over one axis. */
export interface TmAxisWindow {
  /** First rendered item index (inclusive, clamped). */
  readonly start: number;
  /** One past the last rendered item index (exclusive, clamped). */
  readonly end: number;
  /** The translation of the rendered block from the axis origin, in px. */
  readonly leadOffset: number;
  /** The full axis extent (`itemCount × itemSize`), in px — the spacer size. */
  readonly totalSize: number;
}

/**
 * Computes the rendered window over one virtualized axis of fixed-size
 * items: which item indices to render and where to translate the rendered
 * block so it lines up under the scroll offset. Degenerate inputs clamp
 * (no items → an empty window; an offset past the extent → the last page).
 */
export function tmComputeAxisWindow(args: TmAxisWindowArgs): TmAxisWindow {
  const itemSize = Math.max(1, args.itemSize);
  const itemCount = Math.max(0, Math.floor(args.itemCount));
  const overscan = Math.max(0, Math.floor(args.overscan));
  const totalSize = itemCount * itemSize;
  if (itemCount === 0) {
    return { start: 0, end: 0, leadOffset: 0, totalSize: 0 };
  }
  const maxOffset = Math.max(0, totalSize - Math.max(0, args.viewportSize));
  const scrollOffset = Math.min(Math.max(0, args.scrollOffset), maxOffset);
  // Clamp to the last item: a zero viewport with a full-extent offset must
  // still render one item, never an empty window.
  const firstVisible = Math.min(itemCount - 1, Math.floor(scrollOffset / itemSize));
  const lastVisible = Math.ceil((scrollOffset + Math.max(0, args.viewportSize)) / itemSize);
  const start = Math.max(0, firstVisible - overscan);
  const end = Math.min(itemCount, Math.max(start + 1, lastVisible + overscan));
  return { start, end, leadOffset: start * itemSize, totalSize };
}
