// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPivotMember } from '../tm-pivot-types';
import {
  ɵtmPivotAxis,
  ɵtmPivotColumns,
  ɵtmPivotExpandableIds,
  ɵtmPivotFlattenRows,
  ɵtmPivotHeaderAt,
  ɵtmPivotStronger,
  type ɵTmPivotStyle,
} from './pivot-layout';

/** A member under a parent (or the total, id 0). */
function m(id: number, parent: number | null, label = `M${id}`, kind: TmPivotMember['kind'] = 'member'): TmPivotMember {
  return { id, parent, kind, label };
}

/**
 * The regions cube: the total (0); Riyadh (1) with Olaya (11, with its
 * phantom 111 and Tower 112) and Malaz (12); Jeddah (2), a leaf.
 */
const REGIONS: readonly TmPivotMember[] = [
  { id: 0, parent: null, kind: 'total', label: 'Total' },
  m(1, 0, 'Riyadh'),
  m(11, 1, 'Olaya'),
  m(111, 11, 'Olaya', 'phantom'),
  m(112, 11, 'Tower'),
  m(12, 1, 'Malaz'),
  m(2, 0, 'Jeddah'),
];

describe('ɵtmPivotStronger', () => {
  it('ranks total, subtotal-1, subtotal-2, phantom, then leaf, either way round', () => {
    const order: ɵTmPivotStyle[] = ['total', 'subtotal-1', 'subtotal-2', 'phantom', 'leaf'];
    for (let strong = 0; strong < order.length; strong++) {
      for (let weak = strong; weak < order.length; weak++) {
        expect(ɵtmPivotStronger(order[strong], order[weak])).toBe(order[strong]);
        expect(ɵtmPivotStronger(order[weak], order[strong])).toBe(order[strong]);
      }
    }
  });
});

describe('ɵtmPivotAxis', () => {
  it('indexes depths, children in array order, the first level and the total', () => {
    const axis = ɵtmPivotAxis(REGIONS);
    expect(axis.total).toBe(0);
    expect(axis.firstLevel).toEqual([1, 6]);
    expect(axis.children[1]).toEqual([2, 5]);
    expect(axis.children[2]).toEqual([3, 4]);
    expect([...axis.depth]).toEqual([0, 1, 2, 3, 3, 2, 1]);
    expect(axis.maxDepth).toBe(3);
    expect(axis.problems).toEqual([]);
  });

  it('treats null-parent members as the first level when there is no total', () => {
    const axis = ɵtmPivotAxis([m(1, null), m(2, 1), m(3, null)]);
    expect(axis.total).toBe(-1);
    expect(axis.firstLevel).toEqual([0, 2]);
    expect(axis.maxDepth).toBe(2);
  });

  it('reports duplicates, unknown and self parents, cycles and a second total, and still indexes', () => {
    const axis = ɵtmPivotAxis([
      { id: 0, parent: null, kind: 'total', label: 'Total' },
      m(1, 0),
      m(1, 0),
      m(2, 99),
      m(3, 3),
      m(4, 5),
      m(5, 4),
      { id: 6, parent: null, kind: 'total', label: 'Again' },
    ]);
    expect(axis.problems).toEqual([
      'duplicate member id 1',
      'a second total member (id 6)',
      'member 2 names an unknown parent 99',
      'member 3 is its own parent',
      'member 4 is inside a parent cycle',
    ]);
    // Every survivor is reachable: the bad parents fall back to the first
    // level, and the cycle is cut where it closes (5 under the first level,
    // 4 under 5).
    expect(axis.firstLevel).toEqual([1, 3, 4, 6, 7]);
    expect(axis.depth[5]).toBe(2);
    expect(axis.depth[6]).toBe(1);
    expect(axis.depth.every((depth) => depth >= 0)).toBe(true);
  });

  it('walks a 20,000-deep chain without recursion', () => {
    const chain = Array.from({ length: 20_000 }, (_, i) => m(i + 1, i === 0 ? null : i));
    const axis = ɵtmPivotAxis(chain);
    expect(axis.maxDepth).toBe(20_000);
    const rows = ɵtmPivotFlattenRows(axis, ɵtmPivotExpandableIds(axis));
    expect(rows).toHaveLength(20_000);
  });
});

describe('ɵtmPivotFlattenRows', () => {
  const axis = ɵtmPivotAxis(REGIONS);

  it('shows the first level and the total last when nothing is expanded', () => {
    const rows = ɵtmPivotFlattenRows(axis, new Set());
    expect(rows.map((row) => REGIONS[row.index].id)).toEqual([1, 2, 0]);
    expect(rows.map((row) => [row.level, row.posInSet, row.setSize])).toEqual([
      [1, 1, 3],
      [1, 2, 3],
      [1, 3, 3],
    ]);
    expect(rows[0]).toMatchObject({ expandable: true, expanded: false, style: 'subtotal-1' });
    expect(rows[2]).toMatchObject({ expandable: false, style: 'total' });
  });

  it('puts each parent before its children, depth first, with each look', () => {
    const rows = ɵtmPivotFlattenRows(axis, new Set([1, 11]));
    expect(rows.map((row) => REGIONS[row.index].id)).toEqual([1, 11, 111, 112, 12, 2, 0]);
    expect(rows.map((row) => row.style)).toEqual([
      'subtotal-1',
      'subtotal-2',
      'phantom',
      'leaf',
      'leaf',
      'leaf',
      'total',
    ]);
    expect(rows.map((row) => [row.level, row.posInSet, row.setSize])).toEqual([
      [1, 1, 3],
      [2, 1, 2],
      [3, 1, 2],
      [3, 2, 2],
      [2, 2, 2],
      [1, 2, 3],
      [1, 3, 3],
    ]);
  });

  it('ignores an expanded child under a collapsed parent and an unknown id', () => {
    const rows = ɵtmPivotFlattenRows(axis, new Set([11, 999]));
    expect(rows.map((row) => REGIONS[row.index].id)).toEqual([1, 2, 0]);
  });

  it('draws a deeper parent as a leaf', () => {
    const deep = ɵtmPivotAxis([m(1, null), m(2, 1), m(3, 2), m(4, 3)]);
    const rows = ɵtmPivotFlattenRows(deep, ɵtmPivotExpandableIds(deep));
    expect(rows.map((row) => row.style)).toEqual(['subtotal-1', 'subtotal-2', 'leaf', 'leaf']);
    expect(rows[2].expandable).toBe(true);
  });
});

describe('ɵtmPivotColumns', () => {
  const axis = ɵtmPivotAxis(REGIONS);

  /** Each slot as `id` or `id+` for a subtotal. */
  function slotIds(columns: ReturnType<typeof ɵtmPivotColumns>): string[] {
    return columns.slots.map((slot) => `${REGIONS[slot.index].id}${slot.kind === 'subtotal' ? '+' : ''}`);
  }

  it('lays a collapsed axis out as one group per first-level member, the total last', () => {
    const columns = ɵtmPivotColumns(axis, new Set(), 1, 0);
    expect(slotIds(columns)).toEqual(['1', '2', '0']);
    expect(columns.levelRows).toBe(3);
    expect(columns.headerRows).toBe(3);
    expect(columns.cells.map((cell) => [cell.key, cell.row, cell.rowSpan, cell.start, cell.end])).toEqual([
      ['m1', 0, 3, 0, 1],
      ['m2', 0, 3, 1, 2],
      ['t', 0, 3, 2, 3],
    ]);
  });

  it('follows an expanded member’s children with its subtotal column, under a group header', () => {
    const columns = ɵtmPivotColumns(axis, new Set([1, 11]), 1, 0);
    expect(slotIds(columns)).toEqual(['111', '112', '11+', '12', '1+', '2', '0']);
    const byKey = new Map(columns.cells.map((cell) => [cell.key, cell]));
    expect(byKey.get('g1')).toMatchObject({ kind: 'group', row: 0, rowSpan: 1, start: 0, end: 4 });
    expect(byKey.get('s1')).toMatchObject({ kind: 'subtotal', row: 0, rowSpan: 3, start: 4, end: 5 });
    expect(byKey.get('g11')).toMatchObject({ row: 1, rowSpan: 1, start: 0, end: 2 });
    expect(byKey.get('s11')).toMatchObject({ row: 1, rowSpan: 2, start: 2, end: 3 });
    expect(byKey.get('m12')).toMatchObject({ row: 1, rowSpan: 2, start: 3, end: 4 });
    expect(byKey.get('m111')).toMatchObject({ row: 2, rowSpan: 1, style: 'phantom' });
    expect(columns.slots.map((slot) => slot.style)).toEqual([
      'phantom',
      'leaf',
      'subtotal-2',
      'leaf',
      'subtotal-1',
      'leaf',
      'total',
    ]);
    expect(columns.slotOf[1]).toBe(4);
    expect(columns.slotOf[3]).toBe(0);
  });

  it('adds a row of measure names for two or more measures, each resizing its own measure', () => {
    const columns = ɵtmPivotColumns(axis, new Set(), 2, 0);
    expect(columns.dataCount).toBe(6);
    expect(columns.headerRows).toBe(4);
    const measures = columns.rowCells[3];
    expect(measures.map((cell) => [cell.key, cell.measure, cell.resizes, cell.start])).toEqual([
      ['m1.0', 0, 0, 0],
      ['m1.1', 1, 1, 1],
      ['m2.0', 0, 0, 2],
      ['m2.1', 1, 1, 3],
      ['m0.0', 0, 0, 4],
      ['m0.1', 1, 1, 5],
    ]);
    // With measure names below, the member headers carry no handle.
    expect(columns.cells.filter((cell) => cell.kind !== 'measure').every((cell) => cell.resizes === -1)).toBe(true);
  });

  it('gives a single measure’s handle to every header reaching the last row', () => {
    const columns = ɵtmPivotColumns(axis, new Set([1]), 1, 0);
    // The cells come by row, then by start.
    const handles = columns.cells.filter((cell) => cell.resizes === 0).map((cell) => cell.key);
    expect(handles).toEqual(['s1', 'm2', 't', 'm11', 'm12']);
  });

  it('keeps a header row per level label even when the members are shallower', () => {
    const flat = ɵtmPivotAxis([m(1, null), m(2, null)]);
    const columns = ɵtmPivotColumns(flat, new Set(), 1, 2);
    expect(columns.levelRows).toBe(2);
    expect(columns.cells.every((cell) => cell.rowSpan === 2)).toBe(true);
  });

  it('indexes each header row’s occupants, spans from above included, for lookup', () => {
    const columns = ɵtmPivotColumns(axis, new Set([1, 11]), 1, 0);
    expect(columns.rowCells[2].map((cell) => cell.key)).toEqual(['m111', 'm112', 's11', 'm12', 's1', 'm2', 't']);
    expect(ɵtmPivotHeaderAt(columns, 0, 3)?.key).toBe('g1');
    expect(ɵtmPivotHeaderAt(columns, 1, 3)?.key).toBe('m12');
    expect(ɵtmPivotHeaderAt(columns, 2, 3)?.key).toBe('m12');
    expect(ɵtmPivotHeaderAt(columns, 2, 6)?.key).toBe('t');
    expect(ɵtmPivotHeaderAt(columns, 2, 7)).toBeNull();
    expect(ɵtmPivotHeaderAt(columns, 9, 0)).toBeNull();
  });
});
