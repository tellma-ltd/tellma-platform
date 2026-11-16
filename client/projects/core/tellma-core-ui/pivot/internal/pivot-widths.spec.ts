// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ɵTM_PIVOT_RESIZE_RANGE,
  ɵtmPivotDefaultRowHeader,
  ɵtmPivotResizeLimits,
  ɵtmPivotStickyCount,
  ɵtmPivotWidthOf,
  ɵtmPivotWidthPlan,
  ɵtmPivotWithWidth,
  type ɵTmPivotWidthInput,
} from './pivot-widths';

/** A plan input over a 1,000px host with two attributes and four groups of two measures. */
function input(overrides: Partial<ɵTmPivotWidthInput> = {}): ɵTmPivotWidthInput {
  return {
    hostWidth: 1000,
    available: 1000,
    attributeCount: 2,
    slotCount: 4,
    fits: [60, 130],
    user: {},
    frozen: null,
    ...overrides,
  };
}

describe('ɵtmPivotDefaultRowHeader', () => {
  it('takes 40% of the host within 120–280, and 120 below a 300px host', () => {
    expect(ɵtmPivotDefaultRowHeader(1280)).toBe(280);
    expect(ɵtmPivotDefaultRowHeader(700)).toBe(280);
    expect(ɵtmPivotDefaultRowHeader(500)).toBe(200);
    expect(ɵtmPivotDefaultRowHeader(301)).toBe(120);
    expect(ɵtmPivotDefaultRowHeader(299)).toBe(120);
    expect(ɵtmPivotDefaultRowHeader(0)).toBe(120);
  });
});

describe('ɵtmPivotStickyCount', () => {
  it('matches the sticky table for two attributes', () => {
    // 1,180px and up: 280 + 96 + 96 fits 40%; 940–1,179: the first
    // attribute; 300–939: the row header alone; below 300: nothing.
    expect(ɵtmPivotStickyCount(1280, 2)).toBe(3);
    expect(ɵtmPivotStickyCount(1180, 2)).toBe(3);
    expect(ɵtmPivotStickyCount(1179, 2)).toBe(2);
    expect(ɵtmPivotStickyCount(940, 2)).toBe(2);
    expect(ɵtmPivotStickyCount(939, 2)).toBe(1);
    expect(ɵtmPivotStickyCount(300, 2)).toBe(1);
    expect(ɵtmPivotStickyCount(299, 2)).toBe(0);
  });

  it('sticks the row header alone without attributes', () => {
    expect(ɵtmPivotStickyCount(1280, 0)).toBe(1);
    expect(ɵtmPivotStickyCount(280, 0)).toBe(0);
  });

  it('sticks the row header at every host from 300px, its rounding up included', () => {
    // round(0.4 × 344) = 138 is half a pixel over 40% of 344.
    for (let host = 300; host < 940; host++) {
      expect(ɵtmPivotStickyCount(host, 2)).toBe(1);
    }
  });
});

describe('ɵtmPivotWidthPlan', () => {
  it('defaults the leading block and fits each measure at 96px or more', () => {
    const plan = ɵtmPivotWidthPlan(input({ available: 0 }));
    expect(plan.rowHeader).toBe(280);
    expect(plan.attributes).toEqual([96, 96]);
    // Not fitting: each measure is max(112, fit).
    expect(plan.measures).toEqual([112, 130]);
    expect(plan.slotWidth).toBe(242);
    expect(plan.corner).toBe(472);
    expect(plan.total).toBe(472 + 4 * 242);
  });

  it('shares the slack equally per column among measures without a user width', () => {
    // Natural: 472 + 4 × (96 + 130) = 1,376; 1,600 leaves 224 over 8 columns.
    const plan = ɵtmPivotWidthPlan(input({ available: 1600 }));
    expect(plan.measures).toEqual([96 + 28, 130 + 28]);
    expect(plan.slack).toEqual([28, 28]);
    expect(plan.total).toBeLessThanOrEqual(1600);
  });

  it('never changes a user width, and gives the slack to the other measures', () => {
    const plan = ɵtmPivotWidthPlan(input({ available: 1600, user: { measures: [200] } }));
    // Natural: 472 + 4 × (200 + 130) = 1,792 > 1,600, so nothing is shared.
    expect(plan.measures).toEqual([200, 130]);
    const roomy = ɵtmPivotWidthPlan(input({ available: 2000, user: { measures: [200] } }));
    // 2,000 − 1,792 = 208 over the second measure's 4 columns.
    expect(roomy.measures).toEqual([200, 130 + 52]);
    expect(roomy.slack).toEqual([0, 52]);
  });

  it('keeps user widths inside their ranges', () => {
    const plan = ɵtmPivotWidthPlan(
      input({ user: { rowHeader: 9999, attributes: [10, undefined], measures: [undefined, Number.NaN] } }),
    );
    expect(plan.rowHeader).toBe(ɵTM_PIVOT_RESIZE_RANGE.rowHeader.max);
    expect(plan.attributes).toEqual([ɵTM_PIVOT_RESIZE_RANGE.attribute.min, 96]);
    expect(plan.measures[1]).toBe(130);
  });

  it('holds every other measure as it was at the press while a drag is under way', () => {
    const plan = ɵtmPivotWidthPlan(input({ available: 400, frozen: [124, 158], user: { measures: [undefined, 200] } }));
    expect(plan.measures).toEqual([124, 200]);
  });

  it('decides the sticky block on default widths, so a resize never toggles it', () => {
    const wide = ɵtmPivotWidthPlan(input({ hostWidth: 1180, user: { rowHeader: 600 } }));
    expect(wide.sticky).toBe(3);
    expect(wide.stickyWidth).toBe(600 + 96 + 96);
  });
});

describe('resizing', () => {
  it('caps a sticky column where the sticky block would leave less than 112px', () => {
    const plan = ɵtmPivotWidthPlan(input({ hostWidth: 1180 }));
    // W − 112 − the other sticky widths (96 + 96) = 876, above the 600 max.
    expect(ɵtmPivotResizeLimits(plan, { kind: 'rowHeader' })).toEqual({ min: 120, max: 600 });
    const narrow = ɵtmPivotWidthPlan(input({ hostWidth: 500, attributeCount: 0 }));
    // 500 − 112 = 388.
    expect(ɵtmPivotResizeLimits(narrow, { kind: 'rowHeader' })).toEqual({ min: 120, max: 388 });
    // A non-sticky attribute keeps its own range.
    expect(ɵtmPivotResizeLimits(plan, { kind: 'measure', index: 0 })).toEqual({ min: 64, max: 600 });
  });

  it('reads and writes one column’s width', () => {
    const plan = ɵtmPivotWidthPlan(input({ available: 0 }));
    expect(ɵtmPivotWidthOf(plan, { kind: 'attribute', index: 1 })).toBe(96);
    expect(ɵtmPivotWidthOf(plan, { kind: 'measure', index: 1 })).toBe(130);
    const widths = ɵtmPivotWithWidth({ measures: [undefined, 150] }, { kind: 'measure', index: 0 }, 80);
    expect(widths).toEqual({ measures: [80, 150] });
    expect(ɵtmPivotWithWidth(widths, { kind: 'attribute', index: 1 }, 70)).toEqual({
      measures: [80, 150],
      attributes: [undefined, 70],
    });
    expect(ɵtmPivotWithWidth({}, { kind: 'rowHeader' }, 300)).toEqual({ rowHeader: 300 });
  });
});
