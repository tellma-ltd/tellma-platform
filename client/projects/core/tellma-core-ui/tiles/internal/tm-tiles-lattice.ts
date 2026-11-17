// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The tiles lattice as pure arithmetic: how many columns a width holds,
// which lines a scroll position renders, and where each navigation key
// lands. No DOM and no signals, so every rule is unit-testable on numbers.

/** The lattice's shape for one width and one row count. */
export interface ɵTmTilesShape {
  /** Tiles per line (1 in the list and tree layouts). */
  readonly columns: number;
  /** The rows (tiles) the lattice holds. */
  readonly count: number;
}

/** What one width resolves the columns from. */
export interface ɵTmTilesColumnOptions {
  /** Tree mode always stacks one tile per line. */
  readonly tree: boolean;
  /** The narrowest a tile may be, in px. */
  readonly minWidth: number;
  /** The gap between neighboring tiles, in px. */
  readonly gap: number;
  /** Below this width (px) the tiles stack as a list. */
  readonly breakpoint: number;
}

/**
 * The tiles a line holds at a content width: `max(1, floor((W + gap) /
 * (minWidth + gap)))` from the breakpoint up, so every tile stays at least
 * `minWidth` wide with `gap` between neighbors; one below the breakpoint and
 * always in tree mode.
 */
export function ɵtmTilesColumns(width: number, options: ɵTmTilesColumnOptions): number {
  if (options.tree || !(width >= options.breakpoint)) {
    return 1;
  }
  return Math.max(1, Math.floor((width + options.gap) / (options.minWidth + options.gap)));
}

/** The lines a lattice spans: every full line plus a short last one. */
export function ɵtmTilesLineCount(shape: ɵTmTilesShape): number {
  return Math.ceil(shape.count / shape.columns);
}

/**
 * The lines to render, ascending: the windowed range `[start, end)` plus
 * the active tile's line when it falls outside, so the element the active
 * descendant names always exists.
 */
export function ɵtmTilesRenderedLines(
  start: number,
  end: number,
  activeLine: number | null,
): readonly number[] {
  const lines: number[] = [];
  if (activeLine !== null && activeLine < start) {
    lines.push(activeLine);
  }
  for (let line = start; line < end; line++) {
    lines.push(line);
  }
  if (activeLine !== null && activeLine >= end) {
    lines.push(activeLine);
  }
  return lines;
}

/** Lattice lines rendered beyond the visible ones on each side. */
export const ɵTM_TILES_OVERSCAN = 3;

/** The rendered lines: a range of lattice lines, `[start, end)`. */
export interface ɵTmTilesSpan {
  /** The first line (inclusive). */
  readonly start: number;
  /** One past the last line (exclusive). */
  readonly end: number;
}

/**
 * The lines to render at a scroll offset, for lines `pitch` px apart (> 0),
 * `count` of them, under a viewport `viewport` px high: as many as it can show,
 * one more for the part-lines at its edges, and {@link ɵTM_TILES_OVERSCAN}
 * on each side, clipped to the lattice. With `from`, the offset the scroll
 * started at, the span rendered there instead moves only as far as the
 * visible lines need, so the scroll's own frame shows the viewport from the
 * lines it already has and the overscan follows.
 */
export function ɵtmTilesSpan(
  offset: number,
  from: number | null,
  pitch: number,
  count: number,
  viewport: number,
): ɵTmTilesSpan {
  const size = Math.ceil(viewport / pitch) + 1 + 2 * ɵTM_TILES_OVERSCAN;
  const first = Math.floor(offset / pitch);
  // The span starts the overscan above the first line visible at `from`
  // (at the offset itself without one), moved only as far as it must to
  // hold every line visible now: anywhere in [first - 2 × overscan, first].
  const start = clamp(
    Math.floor((from ?? offset) / pitch) - ɵTM_TILES_OVERSCAN,
    first - 2 * ɵTM_TILES_OVERSCAN,
    first,
  );
  // The span is never shorter than twice the overscan plus one, so it ends
  // past line 0 even at the lowest start.
  const end = Math.min(count, start + size);
  return { start: clamp(start, 0, end), end };
}

/**
 * Gives each rendered line the slot that tracks its element. A line keeps
 * its slot while it stays rendered, and a line that enters takes the slot of
 * one that left, the last of them first: the lines that enter at one end
 * take the elements of those that left the other end in mirror order, which
 * lets a list reconcile a scroll either way by moving only the elements it
 * re-binds. A line with no slot left to take gets a new one.
 */
export function ɵtmTilesAssignSlots(
  lines: readonly number[],
  previous: ReadonlyMap<number, number>,
): Map<number, number> {
  // The slots of the lines that left, in the order they were rendered.
  const free = [...previous].flatMap(([line, slot]) => (lines.includes(line) ? [] : [slot]));
  let next = Math.max(-1, ...previous.values()) + 1;
  return new Map(lines.map((line) => [line, previous.get(line) ?? free.pop() ?? next++]));
}

/** A keyboard or driver move over the lattice. */
export type ɵTmTilesMove =
  /** Next or previous tile in reading order (the arrows across a line). */
  | { readonly kind: 'step'; readonly by: number }
  /** Whole lines up or down, keeping the column (vertical arrows, pages). */
  | { readonly kind: 'lines'; readonly by: number }
  /** The first or last tile of the active line. */
  | { readonly kind: 'lineStart' }
  | { readonly kind: 'lineEnd' }
  /** The first or last tile of all. */
  | { readonly kind: 'first' }
  | { readonly kind: 'last' };

/** Where a move lands, and the column a later vertical move aims for. */
export interface ɵTmTilesMoveResult {
  /** The tile index the move lands on, or `null` on an empty lattice. */
  readonly index: number | null;
  /**
   * The column vertical moves keep: a move down onto a short last line
   * clamps to its last tile, and the next move up returns to the column the
   * moves started from.
   */
  readonly column: number | null;
}

/**
 * Applies a move from the active tile (`null`: none yet). Every move clamps
 * and never wraps. With no active tile any move starts at the first tile,
 * except `last`. `column` is the remembered vertical-move column, or
 * `null` to take the active tile's own.
 */
export function ɵtmTilesMove(
  shape: ɵTmTilesShape,
  from: number | null,
  move: ɵTmTilesMove,
  column: number | null,
): ɵTmTilesMoveResult {
  const { count, columns } = shape;
  if (count === 0) {
    return { index: null, column: null };
  }
  const last = count - 1;
  if (from === null || from > last) {
    // No active tile (or one beyond a shrunken lattice): start at an end.
    const index = move.kind === 'last' ? last : 0;
    return { index, column: null };
  }
  const line = Math.floor(from / columns);
  switch (move.kind) {
    case 'step':
      return { index: clamp(from + move.by, 0, last), column: null };
    case 'lines': {
      // Keep the column through short-line clamps: the remembered one
      // when the previous move was vertical too, clamped into a lattice
      // that lost columns since.
      const keep = Math.min(column ?? from - line * columns, columns - 1);
      const target = clamp(line + move.by, 0, ɵtmTilesLineCount(shape) - 1);
      return { index: Math.min(target * columns + keep, last), column: keep };
    }
    case 'lineStart':
      return { index: line * columns, column: null };
    case 'lineEnd':
      return { index: Math.min(line * columns + columns - 1, last), column: null };
    case 'first':
      return { index: 0, column: null };
    case 'last':
      return { index: last, column: null };
  }
}

/** Clamps a value into `[min, max]`. */
function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}
