// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** The pager's two modes: pages of a list, or single items. */
export type ɵTmPagerMode = 'range' | 'item';

/**
 * The status message and its parameters: `pager.range` reads
 * "{from}–{to} / {total}", `pager.item` reads "{index} / {total}", and
 * their `AtLeast` twins append "+" for a lower-bound total.
 */
export interface ɵTmPagerStatus {
  /** The status key under `pager.`, without the `Sentence` suffix the hidden sentence adds. */
  readonly key: 'range' | 'rangeAtLeast' | 'item' | 'itemAtLeast';
  /** The message's parameters, as numbers the message formats in the locale. */
  readonly params: Readonly<Record<string, number>>;
}

/** What the status shows: the mode, the position and the total. */
export interface ɵTmPagerState {
  /** Range or item mode. */
  readonly mode: ɵTmPagerMode;
  /** Range mode: the 1-based page. */
  readonly page: number;
  /** Range mode: the items per page. */
  readonly pageSize: number;
  /** Item mode: the 1-based position of the item. */
  readonly index: number;
  /** The number of items, or `null` when unknown. */
  readonly total: number | null;
  /** Whether `total` is a capped count, so more items may follow. */
  readonly totalIsLowerBound: boolean;
}

/**
 * The status the pager shows, or `null` when the total is unknown (the
 * status is then hidden).
 *
 * In range mode the page shows items `from` to `to`: `to` stops at the
 * total, except that a lower-bound total does not cap it, since more items
 * may follow. An empty list reads "0–0 / 0".
 */
export function ɵtmPagerStatus(state: ɵTmPagerState): ɵTmPagerStatus | null {
  const { total, totalIsLowerBound: atLeast } = state;
  if (total === null) {
    return null;
  }
  if (state.mode === 'item') {
    return { key: atLeast ? 'itemAtLeast' : 'item', params: { index: state.index, total } };
  }
  const size = Math.max(1, state.pageSize);
  const page = Math.max(1, state.page);
  const end = page * size;
  const from = total === 0 && !atLeast ? 0 : (page - 1) * size + 1;
  const to = atLeast ? end : Math.min(end, total);
  return { key: atLeast ? 'rangeAtLeast' : 'range', params: { from, to, total } };
}

/**
 * The widest status a total can produce: "{total}–{total} / {total}" in
 * range mode and "{total} / {total}" in item mode, with the lower bound's
 * "+" when it applies. Every position the pager can reach formats no wider
 * than this, digit for digit, so reserving its width keeps the buttons
 * still while the user pages.
 */
export function ɵtmPagerWidestStatus(
  mode: ɵTmPagerMode,
  total: number,
  totalIsLowerBound: boolean,
): ɵTmPagerStatus {
  if (mode === 'item') {
    return { key: totalIsLowerBound ? 'itemAtLeast' : 'item', params: { index: total, total } };
  }
  return {
    key: totalIsLowerBound ? 'rangeAtLeast' : 'range',
    params: { from: total, to: total, total },
  };
}

/**
 * The status's reserved inline size, in `ch`: one per character of the
 * widest status text. The status draws digits at tabular width, which is
 * the width of "0" (one `ch`), and its separators are narrower, so the
 * reservation holds every status of the same total.
 */
export function ɵtmPagerReservedCh(widestText: string): number {
  // Count code points, not UTF-16 units, so a digit outside the BMP is one.
  return [...widestText].length;
}
