// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The limits a splitter clamps its primary pane's size between, all in
 * pixels except `maxFraction`.
 * @internal
 */
export interface ɵTmSplitterLimits {
  /** The primary pane's minimum. */
  readonly min: number;
  /** The other pane's minimum. */
  readonly otherMin: number;
  /** The primary pane's largest share of the container (0–1). */
  readonly maxFraction: number;
  /** The separator bar's size along the split. */
  readonly separator: number;
}

/**
 * The range a splitter's primary pane may take in a container `container`
 * pixels long along the split: from `min` up to the smaller of
 * `maxFraction` of the container and what the other pane's minimum and the
 * separator leave. When the container is too small for both minimums, the
 * primary pane keeps its own and the range collapses to `min`.
 * @internal
 */
export function ɵtmSplitterBounds(
  container: number,
  limits: ɵTmSplitterLimits,
): { readonly lower: number; readonly upper: number } {
  const upper = Math.min(
    limits.maxFraction * container,
    container - limits.otherMin - limits.separator,
  );
  return { lower: limits.min, upper: Math.max(limits.min, upper) };
}

/**
 * The size a splitter renders its primary pane at:
 * `max(min, min(size, maxFraction × W, W − otherMin − separator))`, with `W`
 * the container's size along the split. The stylesheet applies the same
 * expression in CSS, so the first frame and every container resize are
 * clamped without script; this function serves the keyboard, the drag and
 * the separator's ARIA values.
 * @internal
 */
export function ɵtmSplitterClamp(
  size: number,
  container: number,
  limits: ɵTmSplitterLimits,
): number {
  const { lower, upper } = ɵtmSplitterBounds(container, limits);
  return Math.max(lower, Math.min(size, upper));
}
