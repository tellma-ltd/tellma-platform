// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmPivotWidths } from '../tm-pivot-types';

// The width rules of spec 0034 §6.2, in px. They are layout arithmetic over
// the measured host, not paint, so they live here rather than in tokens.

/** Below this host width nothing sticks and the row header takes its minimum. */
export const ɵTM_PIVOT_NARROW_HOST = 300;
/** The share of the host width the sticky block's default widths may take. */
const STICKY_SHARE = 0.4;
/** The room a resized sticky column must leave for the data columns. */
const STICKY_RESERVE = 112;
/** The default row header's floor: 40% of the host, clamped to 120–280. */
const ROW_HEADER_DEFAULT_MIN = 120;
/** The default row header's ceiling. */
const ROW_HEADER_DEFAULT_MAX = 280;
/** An attribute column's default width. */
const ATTRIBUTE_DEFAULT = 96;
/** A measure's columns' least default width while the columns fit the host. */
const MEASURE_FITTING_MIN = 96;
/** A measure's columns' least default width once the pivot scrolls sideways. */
const MEASURE_SCROLLING_MIN = 112;

/** The range a user resize keeps each kind of column in. */
export const ɵTM_PIVOT_RESIZE_RANGE = {
  rowHeader: { min: 120, max: 600 },
  attribute: { min: 64, max: 400 },
  measure: { min: 64, max: 600 },
} as const;

/** A resizable column: the row header, one attribute column, or every column of one measure. */
export type ɵTmPivotResizeTarget =
  | { readonly kind: 'rowHeader' }
  | { readonly kind: 'attribute'; readonly index: number }
  | { readonly kind: 'measure'; readonly index: number };

/** Clamps a value into a range, rounding to whole pixels. */
function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, Math.round(value)));
}

/** A user width, kept inside its column's resize range; `undefined` when unset or invalid. */
function userWidth(value: number | undefined, range: { min: number; max: number }): number | undefined {
  return value === undefined || !Number.isFinite(value) ? undefined : clamp(value, range.min, range.max);
}

/** The row header's default width for a host width: 40% of it in 120–280, or 120 on a narrow host. */
export function ɵtmPivotDefaultRowHeader(hostWidth: number): number {
  return hostWidth < ɵTM_PIVOT_NARROW_HOST
    ? ROW_HEADER_DEFAULT_MIN
    : clamp(STICKY_SHARE * hostWidth, ROW_HEADER_DEFAULT_MIN, ROW_HEADER_DEFAULT_MAX);
}

/**
 * How many leading columns stick: the row header, then the longest run of
 * attribute columns whose DEFAULT widths still fit in 40% of the host with
 * it, so a resize never toggles it; none below a 300 px host.
 */
export function ɵtmPivotStickyCount(hostWidth: number, attributeCount: number): number {
  if (hostWidth < ɵTM_PIVOT_NARROW_HOST) {
    return 0;
  }
  // From 300 px up, the row header's default is at most 40% of the host
  // give or take its rounding (round(0.4 × W) can exceed 0.4 × W by half a
  // pixel), so it always sticks; the attributes follow while they fit.
  const limit = STICKY_SHARE * hostWidth;
  let width = ɵtmPivotDefaultRowHeader(hostWidth);
  let count = 1;
  while (count <= attributeCount && width + ATTRIBUTE_DEFAULT <= limit) {
    width += ATTRIBUTE_DEFAULT;
    count++;
  }
  return count;
}

/** What the width plan is computed from. */
export interface ɵTmPivotWidthInput {
  /** The pivot's inline size (W). */
  readonly hostWidth: number;
  /** The scroller's client inline size: what the columns may fill without scrolling. */
  readonly available: number;
  /** The attribute column count. */
  readonly attributeCount: number;
  /** The column group count; each holds one column per measure. */
  readonly slotCount: number;
  /** Each measure's fit: its widest value's measured width, padding and rule included. */
  readonly fits: readonly number[];
  /** The user's widths. */
  readonly user: TmPivotWidths;
  /** While a resize drags, each measure's width as it was at the press, so nothing else moves. */
  readonly frozen: readonly number[] | null;
}

/** The resolved widths, in px. */
export interface ɵTmPivotWidthPlan {
  /** The row header. */
  readonly rowHeader: number;
  /** Each attribute column. */
  readonly attributes: readonly number[];
  /** Each measure's data column width, slack included. */
  readonly measures: readonly number[];
  /** The slack each measure's columns took (0 for a user width). */
  readonly slack: readonly number[];
  /** One column group: the sum of the measures' widths. */
  readonly slotWidth: number;
  /** The leading block: the row header and the attribute columns. */
  readonly corner: number;
  /** The whole table. */
  readonly total: number;
  /** How many leading columns stick. */
  readonly sticky: number;
  /** The sticky block's rendered width. */
  readonly stickyWidth: number;
  /** The host width the plan was made for. */
  readonly hostWidth: number;
}

/**
 * Resolves every column's width. The row header and attribute columns take
 * the user's width or their default. A measure's columns take the user's
 * width, else `max(96, fit)`; while the whole table fits, the measures
 * without a user width share the leftover slack equally per column. When
 * it does not fit, those columns are `max(112, fit)` and the pivot scrolls
 * sideways.
 */
export function ɵtmPivotWidthPlan(input: ɵTmPivotWidthInput): ɵTmPivotWidthPlan {
  const ranges = ɵTM_PIVOT_RESIZE_RANGE;
  const user = input.user;
  // Section: the leading block.
  const rowHeader =
    userWidth(user.rowHeader, ranges.rowHeader) ?? ɵtmPivotDefaultRowHeader(input.hostWidth);
  const attributes: number[] = [];
  for (let index = 0; index < input.attributeCount; index++) {
    attributes.push(userWidth(user.attributes?.[index], ranges.attribute) ?? ATTRIBUTE_DEFAULT);
  }
  const corner = rowHeader + attributes.reduce((sum, width) => sum + width, 0);
  // Section: the measures' natural widths and whether the table fits.
  const measureCount = Math.max(1, input.fits.length);
  const fixed: (number | undefined)[] = [];
  for (let index = 0; index < measureCount; index++) {
    fixed.push(userWidth(user.measures?.[index], ranges.measure));
  }
  const fit = (index: number): number => Math.ceil(Math.max(0, input.fits[index] ?? 0));
  const natural = fixed.map((width, index) => width ?? Math.max(MEASURE_FITTING_MIN, fit(index)));
  const naturalTotal = corner + input.slotCount * natural.reduce((sum, width) => sum + width, 0);
  const slack = new Array<number>(measureCount).fill(0);
  let measures: number[];
  if (input.frozen !== null) {
    // A drag keeps every other measure as it was at the press.
    const frozen = input.frozen;
    measures = fixed.map((width, index) => width ?? frozen[index] ?? natural[index]);
  } else if (naturalTotal <= input.available) {
    // The columns fit: the measures without a user width share the rest.
    const sharing = fixed.filter((width) => width === undefined).length * input.slotCount;
    const each = sharing === 0 ? 0 : Math.floor((input.available - naturalTotal) / sharing);
    measures = natural.map((width, index) => {
      slack[index] = fixed[index] === undefined ? each : 0;
      return width + slack[index];
    });
  } else {
    measures = fixed.map((width, index) => width ?? Math.max(MEASURE_SCROLLING_MIN, fit(index)));
  }
  const slotWidth = measures.reduce((sum, width) => sum + width, 0);
  // Section: the sticky block, decided on default widths only.
  const sticky = ɵtmPivotStickyCount(input.hostWidth, input.attributeCount);
  let stickyWidth = 0;
  for (let index = 0; index < sticky; index++) {
    stickyWidth += index === 0 ? rowHeader : attributes[index - 1];
  }
  return {
    rowHeader,
    attributes,
    measures,
    slack,
    slotWidth,
    corner,
    total: corner + input.slotCount * slotWidth,
    sticky,
    stickyWidth,
    hostWidth: input.hostWidth,
  };
}

/** A column's current width in a plan. */
export function ɵtmPivotWidthOf(plan: ɵTmPivotWidthPlan, target: ɵTmPivotResizeTarget): number {
  switch (target.kind) {
    case 'rowHeader':
      return plan.rowHeader;
    case 'attribute':
      return plan.attributes[target.index] ?? 0;
    case 'measure':
      return plan.measures[target.index] ?? 0;
  }
}

/**
 * The range a resize of a column may move in: its kind's range, and for a
 * sticky column no further than where the sticky block would leave less
 * than 112 px of the host for the data.
 */
export function ɵtmPivotResizeLimits(
  plan: ɵTmPivotWidthPlan,
  target: ɵTmPivotResizeTarget,
): { readonly min: number; readonly max: number } {
  const range = ɵTM_PIVOT_RESIZE_RANGE[target.kind];
  const position = target.kind === 'rowHeader' ? 0 : target.kind === 'attribute' ? target.index + 1 : -1;
  if (position === -1 || position >= plan.sticky) {
    return range;
  }
  const others = plan.stickyWidth - ɵtmPivotWidthOf(plan, target);
  const cap = plan.hostWidth - STICKY_RESERVE - others;
  return { min: range.min, max: Math.max(range.min, Math.min(range.max, Math.floor(cap))) };
}

/** A copy of the user widths with one column set. */
export function ɵtmPivotWithWidth(
  widths: TmPivotWidths,
  target: ɵTmPivotResizeTarget,
  width: number,
): TmPivotWidths {
  switch (target.kind) {
    case 'rowHeader':
      return { ...widths, rowHeader: width };
    case 'attribute': {
      const attributes = [...(widths.attributes ?? [])];
      attributes[target.index] = width;
      return { ...widths, attributes: Array.from(attributes, (entry) => entry) };
    }
    case 'measure': {
      const measures = [...(widths.measures ?? [])];
      measures[target.index] = width;
      return { ...widths, measures: Array.from(measures, (entry) => entry) };
    }
  }
}
