// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmGridAssignRowSlots } from './row-slots';

/** The keys `#start` to `#end - 1`, ascending, as the grid renders a window. */
function keys(start: number, end: number): string[] {
  return Array.from({ length: end - start }, (_, i) => `#${start + i}`);
}

describe('ɵtmGridAssignRowSlots', () => {
  it('numbers the first rows from zero, in render order', () => {
    expect([...ɵtmGridAssignRowSlots(keys(97, 101), new Map(), null)]).toEqual([
      ['#97', 0],
      ['#98', 1],
      ['#99', 2],
      ['#100', 3],
    ]);
  });

  it('keeps the slot of every row that stays', () => {
    const first = ɵtmGridAssignRowSlots(keys(0, 8), new Map(), null);
    const next = ɵtmGridAssignRowSlots(keys(3, 11), first, null);
    for (const key of keys(3, 8)) {
      expect(next.get(key)).toBe(first.get(key));
    }
  });

  it('gives the rows entering below the slots of those that left above, last out first in', () => {
    const first = ɵtmGridAssignRowSlots(keys(0, 8), new Map(), null);
    const next = ɵtmGridAssignRowSlots(keys(3, 11), first, null);
    // Rows 0, 1 and 2 left (slots 0, 1, 2); 8, 9 and 10 take 2, 1, 0.
    expect([next.get('#8'), next.get('#9'), next.get('#10')]).toEqual([2, 1, 0]);
  });

  it('gives the rows entering above the slots of those that left below, last out first in', () => {
    const first = ɵtmGridAssignRowSlots(keys(10, 18), new Map(), null);
    const next = ɵtmGridAssignRowSlots(keys(7, 15), first, null);
    // Rows 15, 16 and 17 left (slots 5, 6, 7); 7, 8 and 9 take 7, 6, 5.
    expect([next.get('#7'), next.get('#8'), next.get('#9')]).toEqual([7, 6, 5]);
  });

  it('re-binds every slot on a jump past the whole window, and creates none', () => {
    const first = ɵtmGridAssignRowSlots(keys(0, 8), new Map(), null);
    const next = ɵtmGridAssignRowSlots(keys(5000, 5008), first, null);
    expect([...next.values()].sort((a, b) => a - b)).toEqual([0, 1, 2, 3, 4, 5, 6, 7]);
  });

  it('gives a row with no free slot a new one past every slot in use', () => {
    const first = ɵtmGridAssignRowSlots(keys(0, 4), new Map(), null);
    const next = ɵtmGridAssignRowSlots(keys(0, 6), first, null);
    expect([next.get('#4'), next.get('#5')]).toEqual([4, 5]);
  });

  it('never hands the retired row’s slot to an entering row', () => {
    // The window 0-3 plus the active row 900 riding outside it; the active
    // cell moves to 901, which enters as 900 leaves.
    const first = ɵtmGridAssignRowSlots([...keys(0, 4), '#900'], new Map(), null);
    const next = ɵtmGridAssignRowSlots([...keys(0, 4), '#901'], first, '#900');
    expect(next.get('#901')).not.toBe(first.get('#900'));
    expect(next.get('#901')).toBe(5);
    // Unretired, the entering row re-binds the slot that left.
    expect(ɵtmGridAssignRowSlots([...keys(0, 4), '#901'], first, null).get('#901')).toBe(4);
  });

  it('keeps the retired row’s slot while that row stays rendered', () => {
    const first = ɵtmGridAssignRowSlots(keys(0, 4), new Map(), null);
    const next = ɵtmGridAssignRowSlots(keys(0, 4), first, '#2');
    expect(next.get('#2')).toBe(first.get('#2'));
  });

  it('keeps every slot unique through a sweep of scrolls both ways and jumps', () => {
    let previous: Map<string, number> = new Map();
    const starts = [0, 3, 9, 20, 18, 400, 396, 0, 7, 99_990, 2];
    for (const start of starts) {
      const window = keys(start, start + 12);
      const next = ɵtmGridAssignRowSlots(window, previous, null);
      expect(new Set(next.values()).size).toBe(window.length);
      expect(Math.max(...next.values())).toBeLessThan(12);
      previous = next;
    }
  });
});
