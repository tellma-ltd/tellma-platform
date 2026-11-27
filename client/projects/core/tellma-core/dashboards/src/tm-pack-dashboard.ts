// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmDashboardWidgetModel } from '@tellma/core';

// The grid's two layouts, computed once per widget set (spec 0035 §8.2):
// the wide one keeps each tile's stored columns and compacts the rows
// upward, so a hidden tile leaves no hole; the narrow one stacks the tiles
// in reading order. Two container rules then apply one or the other, so a
// resize runs no script. Reading order is the DOM order (§8.3).

/** The grid's columns. */
const COLUMNS = 12;

/** One tile's place in both layouts, 1-based, as the grid's CSS reads it. */
export interface TmPlacement {
  /** The tile's key. */
  readonly key: string;
  /** The wide layout's first column, counted from the inline-start edge. */
  readonly col: number;
  /** The wide layout's width, in columns. */
  readonly span: number;
  /** The wide layout's first row, after compaction. */
  readonly row: number;
  /** The height, in rows, in both layouts. */
  readonly rows: number;
  /** The narrow layout's first row. */
  readonly rowNarrow: number;
}

/** A tile while it is packed: 0-based, its row compacted. */
interface TmPackedTile {
  /** The tile's key. */
  readonly key: string;
  /** The first column. */
  readonly x: number;
  /** The first row. */
  y: number;
  /** The width, in columns. */
  readonly w: number;
  /** The height, in rows. */
  readonly h: number;
}

/**
 * A whole number within bounds; anything else is the lower bound.
 *
 * @param value - The stored value.
 * @param min - The lower bound.
 * @param max - The upper bound.
 * @returns The value, clamped.
 */
function clamp(value: number, min: number, max: number): number {
  return Number.isFinite(value) ? Math.min(Math.max(Math.trunc(value), min), max) : min;
}

/**
 * Lays a dashboard's tiles out in both of the grid's layouts.
 *
 * 1. Compaction (wide): in `(y, x)` order, each tile moves up one row at a
 *    time while the row above it is free in its own columns.
 * 2. Reading order: the tiles group into bands, read top to bottom. A band
 *    starts at the topmost unread tile and takes every tile that starts
 *    above its bottom edge; each tile that joins can push that edge down.
 *    Within a band, tiles read by `x`, then by `y`. In the narrow layout,
 *    each tile starts below the previous one.
 *
 * A position outside the grid is clamped into it: a stored layout is
 * validated on save, and the clamp only keeps a hand-written one on the grid.
 *
 * @param widgets - The tiles, in any order.
 * @returns Their placements, in reading order.
 *
 * @example
 * ```ts
 * tmPackDashboard([{ key: 'home.recents', type: 'core.recents', x: 7, y: 0, w: 5, h: 9 }]);
 * // [{ key: 'home.recents', col: 8, span: 5, row: 1, rows: 9, rowNarrow: 1 }]
 * ```
 */
export function tmPackDashboard(widgets: readonly TmDashboardWidgetModel[]): readonly TmPlacement[] {
  // --- The tiles on the grid, in (y, x) order; a tie keeps the given order ---
  const tiles: TmPackedTile[] = widgets
    .map((widget) => {
      const x = clamp(widget.x, 0, COLUMNS - 1);
      return { key: widget.key, x, y: clamp(widget.y, 0, Number.MAX_SAFE_INTEGER), w: clamp(widget.w, 1, COLUMNS - x), h: clamp(widget.h, 1, Number.MAX_SAFE_INTEGER) };
    })
    .sort((a, b) => a.y - b.y || a.x - b.x);

  // --- Compaction: each tile rises while the row above is free in its columns ---
  // A cell holds the tiles already placed; a tile only ever rises into rows
  // the tiles before it left free, so it never lands on one.
  const taken = new Set<string>();
  const free = (row: number, tile: TmPackedTile): boolean => {
    for (let column = tile.x; column < tile.x + tile.w; column++) {
      if (taken.has(`${row}:${column}`)) {
        return false;
      }
    }
    return true;
  };
  for (const tile of tiles) {
    while (tile.y > 0 && free(tile.y - 1, tile)) {
      tile.y--;
    }
    for (let row = tile.y; row < tile.y + tile.h; row++) {
      for (let column = tile.x; column < tile.x + tile.w; column++) {
        taken.add(`${row}:${column}`);
      }
    }
  }

  // --- Reading order: bands top to bottom, each read by x, then y ---
  const unread = [...tiles].sort((a, b) => a.y - b.y || a.x - b.x);
  const ordered: TmPackedTile[] = [];
  while (unread.length > 0) {
    let bottom = unread[0]!.y + unread[0]!.h;
    const band: TmPackedTile[] = [];
    // A joining tile can push the edge down, so the scan repeats until no tile joins.
    let joined = true;
    while (joined) {
      joined = false;
      for (const tile of [...unread]) {
        if (tile.y < bottom) {
          band.push(tile);
          unread.splice(unread.indexOf(tile), 1);
          bottom = Math.max(bottom, tile.y + tile.h);
          joined = true;
        }
      }
    }
    ordered.push(...band.sort((a, b) => a.x - b.x || a.y - b.y));
  }

  // --- The narrow layout: each tile below the one before it ---
  let next = 1;
  return ordered.map((tile) => {
    const placement: TmPlacement = { key: tile.key, col: tile.x + 1, span: tile.w, row: tile.y + 1, rows: tile.h, rowNarrow: next };
    next += tile.h;
    return placement;
  });
}
