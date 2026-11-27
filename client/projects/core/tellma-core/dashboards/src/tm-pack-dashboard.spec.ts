// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmDashboardWidgetModel } from '@tellma/core';

import { TM_HOME_WIDGETS } from './tm-home-widgets';
import { tmPackDashboard } from './tm-pack-dashboard';

// The layout's vectors: the home default in both layouts, a hidden tile's
// hole closing, a band that a joining tile pushes down, reading order
// within a band, and stored positions clamped onto the grid.

/**
 * A widget of a test type at a stored box.
 *
 * @param key - The widget's key.
 * @param x - Its first column, 0-based.
 * @param y - Its first row, 0-based.
 * @param w - Its width, in columns.
 * @param h - Its height, in rows.
 * @returns The widget.
 */
function widget(key: string, x: number, y: number, w: number, h: number): TmDashboardWidgetModel {
  return { key, type: 'acme.cash-position', x, y, w, h };
}

describe('tmPackDashboard', () => {
  it('lays the home default out as two bands, its lists side by side', () => {
    expect(tmPackDashboard(TM_HOME_WIDGETS)).toEqual([
      { key: 'home.to-me', col: 1, span: 7, row: 1, rows: 9, rowNarrow: 1 },
      { key: 'home.recents', col: 8, span: 5, row: 1, rows: 9, rowNarrow: 10 },
      { key: 'home.by-me', col: 1, span: 7, row: 10, rows: 9, rowNarrow: 19 },
    ]);
  });

  it('closes the hole a hidden tile leaves, so the tile under it rises', () => {
    // Omar's hatch without the tile B he cannot run.
    const placements = tmPackDashboard([widget('A', 0, 0, 6, 9), widget('C', 6, 9, 6, 9)]);

    expect(placements).toEqual([
      { key: 'A', col: 1, span: 6, row: 1, rows: 9, rowNarrow: 1 },
      { key: 'C', col: 7, span: 6, row: 1, rows: 9, rowNarrow: 10 },
    ]);
  });

  it('rises a tile only while the row above is free in its own columns', () => {
    const placements = tmPackDashboard([
      widget('wide', 0, 0, 8, 4),
      widget('under', 4, 10, 4, 2),
      widget('beside', 8, 6, 4, 3),
    ]);

    expect(placements.map(({ key, row }) => [key, row])).toEqual([
      ['wide', 1],
      ['beside', 1],
      ['under', 5],
    ]);
  });

  it('reads a band by column, then row, and lets a joining tile push its bottom edge down', () => {
    // `left` alone would end the first band at row 4; `tall` joins and
    // pushes the edge to row 10, so `stack` and `late` join the band too,
    // and `low`, which rises to the eleventh row, starts the next one.
    const placements = tmPackDashboard([
      widget('left', 0, 0, 4, 4),
      widget('tall', 4, 0, 4, 10),
      widget('filler', 8, 0, 4, 8),
      widget('stack', 0, 4, 4, 4),
      widget('late', 8, 8, 4, 2),
      widget('low', 0, 12, 12, 2),
    ]);

    expect(placements.map(({ key }) => key)).toEqual([
      'left',
      'stack',
      'tall',
      'filler',
      'late',
      'low',
    ]);
    expect(placements.map(({ row }) => row)).toEqual([1, 5, 1, 1, 9, 11]);
    expect(placements.map(({ rowNarrow }) => rowNarrow)).toEqual([1, 5, 9, 19, 27, 29]);
  });

  it('reads tiles stored out of order in their reading order', () => {
    const placements = tmPackDashboard([
      widget('second', 6, 0, 6, 3),
      widget('third', 0, 3, 12, 2),
      widget('first', 0, 0, 6, 3),
    ]);

    expect(placements.map(({ key }) => key)).toEqual(['first', 'second', 'third']);
  });

  it('clamps a position outside the grid onto it', () => {
    const placements = tmPackDashboard([
      widget('over', 10, 0, 6, 0),
      widget('before', -3, Number.NaN, 2.7, 3),
    ]);

    expect(placements).toEqual([
      { key: 'before', col: 1, span: 2, row: 1, rows: 3, rowNarrow: 1 },
      { key: 'over', col: 11, span: 2, row: 1, rows: 1, rowNarrow: 4 },
    ]);
  });

  it('lays out nothing for no widgets', () => {
    expect(tmPackDashboard([])).toEqual([]);
  });
});
