// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmComputeAxisWindow, type TmAxisWindowArgs } from './tm-grid-windowing';

/** 100 items × 10px under a 100px viewport, no overscan — override per test. */
function window(overrides: Partial<TmAxisWindowArgs> = {}) {
  return tmComputeAxisWindow({
    scrollOffset: 0,
    viewportSize: 100,
    itemSize: 10,
    itemCount: 100,
    overscan: 0,
    ...overrides,
  });
}

describe('tmComputeAxisWindow', () => {
  it('computes the window at the axis origin', () => {
    expect(window()).toEqual({ start: 0, end: 10, leadOffset: 0, totalSize: 1000 });
  });

  it('computes the window at an item-aligned mid-axis offset', () => {
    expect(window({ scrollOffset: 250 })).toEqual({
      start: 25,
      end: 35,
      leadOffset: 250,
      totalSize: 1000,
    });
  });

  it('renders the partially visible items at a non-aligned offset', () => {
    // 255..355 clips item 25 at the top and item 35 at the bottom — both render.
    expect(window({ scrollOffset: 255 })).toEqual({
      start: 25,
      end: 36,
      leadOffset: 250,
      totalSize: 1000,
    });
  });

  it('applies overscan on both sides and translates the block accordingly', () => {
    expect(window({ scrollOffset: 250, overscan: 3 })).toEqual({
      start: 22,
      end: 38,
      leadOffset: 220,
      totalSize: 1000,
    });
  });

  it('clamps overscan at the axis start', () => {
    expect(window({ overscan: 5 })).toEqual({ start: 0, end: 15, leadOffset: 0, totalSize: 1000 });
  });

  it('clamps overscan at the axis end', () => {
    expect(window({ scrollOffset: 900, overscan: 5 })).toEqual({
      start: 85,
      end: 100,
      leadOffset: 850,
      totalSize: 1000,
    });
  });

  it('returns an empty window with totalSize 0 for zero items', () => {
    expect(window({ itemCount: 0 })).toEqual({ start: 0, end: 0, leadOffset: 0, totalSize: 0 });
    // The empty window ignores the scroll offset entirely.
    expect(window({ itemCount: 0, scrollOffset: 5000 })).toEqual({
      start: 0,
      end: 0,
      leadOffset: 0,
      totalSize: 0,
    });
  });

  it('clamps a scroll offset past the extent to the last page', () => {
    // 50 items × 10px = 500px extent; the max offset under a 100px viewport is 400.
    expect(window({ scrollOffset: 99999, itemCount: 50 })).toEqual({
      start: 40,
      end: 50,
      leadOffset: 400,
      totalSize: 500,
    });
  });

  it('renders the clipped item when the viewport is smaller than one item', () => {
    expect(window({ viewportSize: 4, itemCount: 10 })).toEqual({
      start: 0,
      end: 1,
      leadOffset: 0,
      totalSize: 100,
    });
    // 15..19 lies entirely inside item 1 — exactly one item renders.
    expect(window({ scrollOffset: 15, viewportSize: 4, itemCount: 10 })).toEqual({
      start: 1,
      end: 2,
      leadOffset: 10,
      totalSize: 100,
    });
    // 18..22 straddles the boundary — items 1 and 2 both render.
    expect(window({ scrollOffset: 18, viewportSize: 4, itemCount: 10 })).toEqual({
      start: 1,
      end: 3,
      leadOffset: 10,
      totalSize: 100,
    });
  });

  it('renders at least one item under a zero-size viewport', () => {
    const result = window({ scrollOffset: 20, viewportSize: 0 });
    expect(result.start).toBe(2);
    expect(result.end).toBe(3);
  });

  it('clamps an overscan larger than the item count to the full range', () => {
    expect(window({ itemCount: 3, viewportSize: 10, overscan: 10 })).toEqual({
      start: 0,
      end: 3,
      leadOffset: 0,
      totalSize: 30,
    });
  });

  it('handles fractional scroll offsets with integer indices', () => {
    expect(window({ scrollOffset: 25.5 })).toEqual({
      start: 2,
      end: 13,
      leadOffset: 20,
      totalSize: 1000,
    });
  });

  it('treats item sizes below 1 as 1', () => {
    expect(window({ itemSize: 0, itemCount: 5, viewportSize: 3 })).toEqual({
      start: 0,
      end: 3,
      leadOffset: 0,
      totalSize: 5,
    });
    expect(window({ itemSize: 0.4, itemCount: 5, viewportSize: 3 }).totalSize).toBe(5);
    expect(window({ itemSize: -10, itemCount: 5, viewportSize: 3 }).totalSize).toBe(5);
  });

  it('clamps a negative scroll offset to 0', () => {
    expect(window({ scrollOffset: -50 })).toEqual({
      start: 0,
      end: 10,
      leadOffset: 0,
      totalSize: 1000,
    });
  });

  it('floors a fractional item count and clamps a negative overscan to 0', () => {
    expect(window({ itemCount: 5.9, viewportSize: 30 })).toEqual({
      start: 0,
      end: 3,
      leadOffset: 0,
      totalSize: 50,
    });
    expect(window({ scrollOffset: 250, overscan: -3 })).toEqual({
      start: 25,
      end: 35,
      leadOffset: 250,
      totalSize: 1000,
    });
  });
});
