// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmPopoverBlockRoom, tmPopoverInlineShift } from './fit';

/** An anchor box at `left`/`top`, `width` × `height`. */
function box(left: number, top: number, width = 80, height = 30): DOMRect {
  return new DOMRect(left, top, width, height);
}

describe('tmPopoverInlineShift', () => {
  it('leaves a panel that fits where its alignment puts it', () => {
    expect(tmPopoverInlineShift(box(100, 0), 320, 'start', false, 1024, 8)).toBe(0);
    expect(tmPopoverInlineShift(box(600, 0), 320, 'end', false, 1024, 8)).toBe(0);
  });

  it('slides a start-aligned panel back from the end edge, keeping the margin', () => {
    // Left at 900 with 320 wide would end at 1220; the limit is 1016.
    expect(tmPopoverInlineShift(box(900, 0), 320, 'start', false, 1024, 8)).toBe(-204);
  });

  it('keeps the start margin when the panel would cross the start edge', () => {
    // End-aligned to an anchor ending at 100: left would be 100 - 320.
    expect(tmPopoverInlineShift(box(20, 0), 320, 'end', false, 1024, 8)).toBe(228);
  });

  it('centres a full-width panel on a phone with the margin on both sides', () => {
    // 390 wide, 8 margin: the panel is 374 and must start at 8.
    const shift = tmPopoverInlineShift(box(250, 0), 374, 'start', false, 390, 8);
    expect(250 + shift).toBe(8);
  });

  it('resolves start against a right-to-left direction as the CDK does', () => {
    // RTL start aligns the right edges: left = 180 - 320 = -140, clamped to 8.
    expect(tmPopoverInlineShift(box(100, 0), 320, 'start', true, 1024, 8)).toBe(148);
    // RTL end aligns the left edges, like LTR start.
    expect(tmPopoverInlineShift(box(100, 0), 320, 'end', true, 1024, 8)).toBe(0);
  });

  it('centres on the anchor for a centre alignment', () => {
    // Anchor centre 140, panel 200: left 40, which fits.
    expect(tmPopoverInlineShift(box(100, 0), 200, 'center', false, 1024, 8)).toBe(0);
    // Anchor centre 40: left -60, clamped to 8.
    expect(tmPopoverInlineShift(box(0, 0), 200, 'center', false, 1024, 8)).toBe(68);
  });
});

describe('tmPopoverBlockRoom', () => {
  const below = { originY: 'bottom', overlayY: 'top' } as const;
  const above = { originY: 'top', overlayY: 'bottom' } as const;

  it('is the room below the anchor less the margin for a panel below', () => {
    // Anchor bottom at 130 in a 800-high viewport.
    expect(tmPopoverBlockRoom(box(0, 100), below, 800, 8)).toBe(662);
  });

  it('is the room above the anchor less the margin for a panel above', () => {
    expect(tmPopoverBlockRoom(box(0, 500), above, 800, 8)).toBe(492);
  });

  it('is twice the nearer room for a panel centred on its anchor', () => {
    const centred = { originY: 'center', overlayY: 'center' } as const;
    // Centre at 215: 215 above, 585 below; twice (215 - 8).
    expect(tmPopoverBlockRoom(box(0, 200), centred, 800, 8)).toBe(414);
  });

  it('never goes below zero', () => {
    expect(tmPopoverBlockRoom(box(0, 795), below, 800, 8)).toBe(0);
  });
});
