// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ConnectionPositionPair } from '@angular/cdk/overlay';

/** A length token resolved on an element, in px (0 when it is not set). */
export function tmTokenPx(element: Element, name: string): number {
  return Number.parseFloat(getComputedStyle(element).getPropertyValue(name)) || 0;
}

/**
 * The physical inline offset that keeps a panel of `width` px, placed on a
 * block side of `anchor` with the CDK's alignment rules, inside
 * `[margin, viewport − margin]`. `start` and `end` resolve against the
 * direction as the CDK resolves them: in a left-to-right page `start`
 * aligns the left edges, in a right-to-left one the right edges.
 */
export function tmPopoverInlineShift(
  anchor: DOMRect,
  width: number,
  align: 'start' | 'center' | 'end',
  rtl: boolean,
  viewport: number,
  margin: number,
): number {
  const left =
    align === 'center'
      ? anchor.left + anchor.width / 2 - width / 2
      : (align === 'start') !== rtl
        ? anchor.left
        : anchor.right - width;
  return Math.round(Math.max(margin, Math.min(left, viewport - margin - width)) - left);
}

/**
 * The block size a panel attached to `anchor` by `pair` may take within a
 * viewport `viewport` px high, keeping `margin` from the edge it grows
 * towards: the room below the attach point, above it, or, for a panel
 * centred on it (an inline side aligned to the centre), twice the nearer.
 */
export function tmPopoverBlockRoom(
  anchor: DOMRect,
  pair: Pick<ConnectionPositionPair, 'originY' | 'overlayY'>,
  viewport: number,
  margin: number,
): number {
  const y =
    pair.originY === 'top'
      ? anchor.top
      : pair.originY === 'bottom'
        ? anchor.bottom
        : anchor.top + anchor.height / 2;
  const room =
    pair.overlayY === 'top'
      ? viewport - y - margin
      : pair.overlayY === 'bottom'
        ? y - margin
        : 2 * (Math.min(y, viewport - y) - margin);
  return Math.max(0, Math.floor(room));
}
