// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, type Locator, type Page } from '@playwright/test';

import { near, paintedColour, pixelsOf, type Rgb, type TmPixels } from './pixels';

/**
 * Whether text or a glyph shows in its ink on a system fill under forced
 * colors. Chromium paints an opaque Canvas "readability backplate" behind
 * every line of text in a block that keeps `forced-color-adjust: auto`,
 * whatever its background. Text drawn in HighlightText over a Highlight
 * fill then lands on Canvas instead, and HighlightText is Canvas in the
 * common palettes: white on white, or black on black. Computed styles still
 * read HighlightText on Highlight, so only the pixels show the loss.
 *
 * The read compares two shots of the box: as painted, and with its ink made
 * transparent and the box opted out of forced colours, which keeps the fill
 * and drops any backplate. Ink drawn on the fill changes glyph-shaped pixels
 * and leaves the fill showing between them; a backplate changes its whole
 * line box, so nothing of the fill is left between the changed pixels,
 * whatever colour the plate and the ink are. The reference shot shows the
 * fill as painted, blended over what lies beneath it, so the reads hold
 * whether the fill is opaque or translucent.
 */

/** What {@link readInk} measured in one box. */
export interface TmInkReading {
  /**
   * The ink's mass: over the changed pixels, the sum of the share of the
   * ink colour each carries, so about the number of pixels its strokes
   * cover, anti-aliased edges counting in part.
   */
  readonly ink: number;
  /** The pixels the ink changed in any way. */
  readonly changed: number;
  /** The pixels inside the bounding box of the changed pixels. */
  readonly span: number;
  /** The pixels inside that bounding box that still show the fill unchanged. */
  readonly fillBetween: number;
}

/** The attribute that marks the box whose ink {@link readInk} hides for its reference shot. */
const HIDDEN_INK = 'data-tm-ink-hidden';

/**
 * Hides the ink of the marked box and everything inside it, text and
 * `currentColor` glyphs alike. The box opts out of forced colours, so the
 * transparent ink is kept as given (a forced box would repaint it in
 * CanvasText) and no backplate is painted; its fill is a system colour and
 * paints the same either way.
 */
const HIDDEN_INK_STYLE =
  `[${HIDDEN_INK}], [${HIDDEN_INK}] * {` +
  ' color: transparent !important;' +
  ' forced-color-adjust: none !important;' +
  ' text-decoration-color: transparent !important; }';

/** How far, per channel, a pixel may stray from the line between what is under the ink and the ink. */
const INK_LINE_TOLERANCE = 24;

/**
 * How much of `ink` the pixel `shown` carries over the colour `under` it,
 * from 0 to 1: 1 where a stroke covers the pixel, about 0.5 on the edge of
 * an anti-aliased stroke. A thin glyph stroke covers few whole pixels, so
 * its ink is read as this blend rather than as an exact colour. A pixel
 * that moved some other way, or off the line from `under` to `ink` by more
 * than {@link INK_LINE_TOLERANCE}, carries none, as does an ink that `under`
 * already matches.
 */
function inkShare(shown: Rgb, under: Rgb, ink: Rgb): number {
  const toward = [ink.r - under.r, ink.g - under.g, ink.b - under.b];
  const moved = [shown.r - under.r, shown.g - under.g, shown.b - under.b];
  const length = toward.reduce((sum, channel) => sum + channel * channel, 0);
  if (length < 3 * INK_LINE_TOLERANCE * INK_LINE_TOLERANCE) {
    return 0;
  }
  const share = moved.reduce((sum, channel, index) => sum + channel * toward[index], 0) / length;
  const offLine = moved.some(
    (channel, index) => Math.abs(channel - share * toward[index]) > INK_LINE_TOLERANCE,
  );
  return offLine ? 0 : Math.min(1, Math.max(0, share));
}

/** Options of {@link readInk}. */
export interface TmInkReadOptions {
  /** The CSS colour the ink should paint in; `HighlightText` when unset. */
  readonly inkColour?: string;
  /**
   * The CSS colour the ink is drawn on, when it is drawn on a backplate
   * rather than on the fill (`Canvas`). Unset, the ink is read against the
   * fill, the reference shot's pixel beneath it.
   */
  readonly plateColour?: string;
}

/**
 * Shoots `target` as painted and again with its ink hidden, and measures
 * what the ink changed. The page must be settled, and the pointer and focus
 * must not move between the shots.
 */
export async function readInk(
  page: Page,
  target: Locator,
  options: TmInkReadOptions = {},
): Promise<TmInkReading> {
  const { inkColour = 'HighlightText', plateColour } = options;
  const ink = await paintedColour(page, inkColour);
  const plate = plateColour === undefined ? null : await paintedColour(page, plateColour);
  await target.scrollIntoViewIfNeeded();
  const shown = await pixelsOf(page, target);

  // Section: the reference shot, ink hidden by a sheet scoped to the box.
  await target.evaluate(
    (element, { attribute, css }) => {
      const sheet = document.createElement('style');
      sheet.setAttribute(attribute, 'sheet');
      sheet.textContent = css;
      document.head.append(sheet);
      element.setAttribute(attribute, '');
    },
    { attribute: HIDDEN_INK, css: HIDDEN_INK_STYLE },
  );
  let hidden: TmPixels;
  try {
    hidden = await pixelsOf(page, target);
  } finally {
    await target.evaluate((element, attribute) => {
      element.removeAttribute(attribute);
      document.head.querySelector(`style[${attribute}]`)?.remove();
    }, HIDDEN_INK);
  }

  // Section: the changed pixels, the ink they carry, and their bounding box.
  let changed = 0;
  let mass = 0;
  let left = shown.width;
  let top = shown.height;
  let right = -1;
  let bottom = -1;
  const differs = (x: number, y: number): boolean => !near(shown.at(x, y), hidden.at(x, y));
  for (let y = 0; y < shown.height; y += 1) {
    for (let x = 0; x < shown.width; x += 1) {
      if (!differs(x, y)) {
        continue;
      }
      changed += 1;
      mass += inkShare(shown.at(x, y), plate ?? hidden.at(x, y), ink);
      left = Math.min(left, x);
      top = Math.min(top, y);
      right = Math.max(right, x);
      bottom = Math.max(bottom, y);
    }
  }

  // Section: how much of the fill still shows between the changed pixels.
  let span = 0;
  let fillBetween = 0;
  for (let y = top; y <= bottom; y += 1) {
    for (let x = left; x <= right; x += 1) {
      span += 1;
      if (!differs(x, y)) {
        fillBetween += 1;
      }
    }
  }
  return { ink: Math.round(mass), changed, span, fillBetween };
}

/** Options of {@link expectInkShows} and {@link expectInkOnFill}. */
export interface TmInkExpectation extends TmInkReadOptions {
  /** The least ink mass (see {@link TmInkReading.ink}) the box must show; 8 when unset. */
  readonly minInk?: number;
}

/**
 * Asserts that the ink of `target` shows in its colour, on the fill or on a
 * backplate as `options` says: an ink mass of at least `minInk`. `what`
 * names the box in the failure messages. Soft: a test reads every box and
 * reports each one that fails.
 */
export async function expectInkShows(
  page: Page,
  target: Locator,
  what: string,
  options: TmInkExpectation = {},
): Promise<TmInkReading> {
  const { minInk = 8 } = options;
  const reading = await readInk(page, target, options);
  expect
    .soft(reading.ink, `${what} shows ${options.inkColour ?? 'HighlightText'} ink`)
    .toBeGreaterThanOrEqual(minInk);
  return reading;
}

/**
 * Asserts that the ink of `target` shows in its colour directly on the
 * fill behind it: as {@link expectInkShows}, and with the fill showing
 * through more than a quarter of the box the ink spans, which no backplate
 * leaves.
 */
export async function expectInkOnFill(
  page: Page,
  target: Locator,
  what: string,
  options: Omit<TmInkExpectation, 'plateColour'> = {},
): Promise<void> {
  const reading = await expectInkShows(page, target, what, options);
  expect
    .soft(reading.fillBetween, `${what} shows its fill between the strokes, not a backplate`)
    .toBeGreaterThan(reading.span / 4);
}
