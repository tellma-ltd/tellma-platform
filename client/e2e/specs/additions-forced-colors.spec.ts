// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import {
  near,
  paintedColour,
  pixelsOf,
  resolvedColour,
  type Rgb,
  type Rgba,
  type TmPaintedCount,
  type TmPixels,
} from '../support/pixels';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * The forced-colors battery: under `forced-colors: active` the browser
 * replaces every colour with the system palette, so a state drawn only by
 * a tint disappears. Each state the library keeps visible is proved from
 * the pixels the browser painted, not from computed styles: the selected
 * tile, the checked segment, the switch turned on, the counter tones, the
 * drop target under a drag and the tinted avatar.
 *
 * A system colour is not always opaque: Linux Chromium's Highlight is
 * translucent, so a highlight fill blends with whatever it covers. Each
 * highlight is therefore read against a reference shot of the same box
 * without it (the box hidden, or the state off), as that colour composited
 * over each reference pixel, never as one fixed colour.
 */

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
});

/** Whether (`x`, `y`) lies in the outer `depth`-pixel ring of `pixels`. */
function inRing(pixels: TmPixels, depth = 2): (x: number, y: number) => boolean {
  return (x, y) =>
    x < depth || y < depth || x >= pixels.width - depth || y >= pixels.height - depth;
}

/**
 * Whether each of the four sides shows an edge: walking in from the middle
 * of each side, some pixel within `reach` differs from the canvas colour.
 */
function edgesShow(pixels: TmPixels, canvas: Rgb, reach = 3): boolean {
  const midX = Math.floor(pixels.width / 2);
  const midY = Math.floor(pixels.height / 2);
  const sides = [
    (step: number) => pixels.at(step, midY),
    (step: number) => pixels.at(pixels.width - 1 - step, midY),
    (step: number) => pixels.at(midX, step),
    (step: number) => pixels.at(midX, pixels.height - 1 - step),
  ];
  return sides.every((side) =>
    Array.from({ length: reach }, (_, step) => side(step)).some((pixel) => !near(pixel, canvas)),
  );
}

/** The forced palette's colours, as this page resolves them (alpha included where it is not opaque). */
async function palette(page: Page): Promise<{ canvas: Rgb; highlight: Rgba; highlightText: Rgba }> {
  return {
    canvas: await paintedColour(page, 'Canvas'),
    highlight: await resolvedColour(page, 'Highlight'),
    highlightText: await resolvedColour(page, 'HighlightText'),
  };
}

/**
 * A shot of what lies beneath an element: its box, screenshotted with the
 * element (and so its children) hidden and its layout kept.
 */
async function backdropOf(page: Page, locator: Locator): Promise<TmPixels> {
  await locator.evaluate((element) => {
    (element as HTMLElement).style.visibility = 'hidden';
  });
  const backdrop = await pixelsOf(page, locator);
  await locator.evaluate((element) => {
    (element as HTMLElement).style.visibility = '';
  });
  return backdrop;
}

/** A fill covers more than a third of its box; its label, border and corners take the rest. */
function filled(count: TmPaintedCount): boolean {
  return count.painted > count.total / 3;
}

/**
 * Whether what lies beneath still shows through a text box: more than 40%
 * of its pixels match `backdrop`, the same box with the text hidden. Text
 * keeps to its strokes and their anti-aliasing (a short label covers about
 * half its box), so a box that changes almost everywhere has something
 * painted behind the text as well.
 */
function showsThrough(pixels: TmPixels, backdrop: TmPixels): boolean {
  const total = pixels.width * pixels.height;
  return total - pixels.differingPixels(backdrop) > total * 0.4;
}

test('a selected tile draws a highlight outline, an unselected one none', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const tile = (id: number): Locator =>
    page.getByTestId('tiles').locator(`.tm-tiles__item[data-index="${id - 1}"]`);
  await expect(tile(2)).toBeVisible();
  const { highlight } = await palette(page);
  await appStable(page);
  const backdrop = await backdropOf(page, tile(2));
  const before = await pixelsOf(page, tile(2));
  expect(before.paintedOver(backdrop, [highlight], { where: inRing(before) }).painted).toBe(0);

  // Section: select the tile, then move the active tile away, so only the
  // selection marks it. The ring is read over the unselected tile, whose
  // border and surface it covers.
  await tile(2).locator('[role="checkbox"]').click();
  await tile(3).focus();
  await expect(tile(2)).toHaveAttribute('aria-selected', 'true');
  await expect(tile(3)).toBeFocused();
  await appStable(page);
  const after = await pixelsOf(page, tile(2));
  const ring = after.paintedOver(before, [highlight], { where: inRing(after) });
  expect(ring.painted).toBeGreaterThan(ring.total / 2);
});

test('the checked segment is filled with the highlight, its label legible on it', async ({
  page,
}) => {
  await page.goto(storyUrl('segmented'));
  const segments = page.getByTestId('seg-period').locator('.tm-segmented__segment');
  await expect(segments.nth(1)).toHaveClass(/tm-segmented__segment--checked/);
  const { highlight, highlightText } = await palette(page);
  await appStable(page);
  const checked = await pixelsOf(page, segments.nth(1));
  const checkedBackdrop = await backdropOf(page, segments.nth(1));
  const unchecked = await pixelsOf(page, segments.nth(0));
  const uncheckedBackdrop = await backdropOf(page, segments.nth(0));
  expect(filled(checked.paintedOver(checkedBackdrop, [highlight]))).toBe(true);
  expect(unchecked.paintedOver(uncheckedBackdrop, [highlight]).painted).toBe(0);

  // Section: the label, read in its own box against the same box with the
  // label hidden, which shows the fill. Its strokes are in the highlight's
  // text colour, and between them the fill shows through: a backplate the
  // browser paints behind adjusted text would hide it, and with it a label
  // whose colour matches the backplate.
  const label = segments.nth(1).locator('.tm-segmented__label');
  const ink = await pixelsOf(page, label);
  const inkBackdrop = await backdropOf(page, label);
  // Anti-aliased strokes come near the text colour but rarely reach it.
  expect(ink.paintedOver(inkBackdrop, [highlightText], { tolerance: 24 }).painted).toBeGreaterThan(
    0,
  );
  expect(showsThrough(ink, inkBackdrop)).toBe(true);
});

test('a switch turned on fills its track with the highlight, one turned off does not', async ({
  page,
}) => {
  await page.goto(storyUrl('switch'));
  const track = (id: string): Locator => page.getByTestId(id).locator('.tm-switch__track');
  await expect(track('switch-on')).toBeVisible();
  const { highlight, highlightText } = await palette(page);
  await appStable(page);
  const on = await pixelsOf(page, track('switch-on'));
  const onBackdrop = await backdropOf(page, track('switch-on'));
  const off = await pixelsOf(page, track('switch-notify'));
  const offBackdrop = await backdropOf(page, track('switch-notify'));
  expect(filled(on.paintedOver(onBackdrop, [highlight]))).toBe(true);
  expect(off.paintedOver(offBackdrop, [highlight]).painted).toBe(0);

  // Section: the knob, read in its own box against the same box with the
  // knob hidden, which shows the track's fill: the highlight's text colour
  // fills it.
  const knob = page.getByTestId('switch-on').locator('.tm-switch__knob');
  const knobPixels = await pixelsOf(page, knob);
  const knobBackdrop = await backdropOf(page, knob);
  expect(filled(knobPixels.paintedOver(knobBackdrop, [highlightText]))).toBe(true);
});

test('every counter tone keeps a visible edge, and the attention tone alone a highlight fill', async ({
  page,
}) => {
  await page.goto(storyUrl('badge'));
  await expect(page.getByTestId('counter-attention')).toBeVisible();
  const { canvas, highlight } = await palette(page);
  await appStable(page);
  for (const id of ['counter-capped', 'counter-accent', 'counter-attention']) {
    const counter = page.getByTestId(id);
    const pixels = await pixelsOf(page, counter);
    const backdrop = await backdropOf(page, counter);
    expect(edgesShow(pixels, canvas), `${id} shows its edge`).toBe(true);
    const fill = pixels.paintedOver(backdrop, [highlight]);
    if (id === 'counter-attention') {
      expect(filled(fill), `${id} is filled`).toBe(true);
    } else {
      expect(fill.painted, `${id} has no highlight fill`).toBe(0);
    }
  }
});

test('a drop target under an accepted drag draws a highlight outline', async ({ page }) => {
  await page.goto(storyUrl('drag-drop'));
  const well = page.getByTestId('drop-well');
  await expect(well).toBeVisible();
  const { highlight } = await palette(page);
  await appStable(page);
  const backdrop = await backdropOf(page, well);
  const before = await pixelsOf(page, well);
  expect(before.paintedOver(backdrop, [highlight], { where: inRing(before) }).painted).toBe(0);
  // A synthetic drag holds the hover state still, which a real one cannot.
  await page.evaluate(() => {
    const row = document.querySelector('[data-key="Amount"] > .tm-tree__row')!;
    const target = document.querySelector('[data-testid="drop-well"]')!;
    const data = new DataTransfer();
    row.dispatchEvent(new DragEvent('dragstart', { bubbles: true, dataTransfer: data }));
    target.dispatchEvent(
      new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: data }),
    );
  });
  await expect(well).toHaveClass(/tm-drop-target--over/);
  await appStable(page);
  // A dashed outline, read over the well without it: its dashes cover
  // about half the ring.
  const over = await pixelsOf(page, well);
  const ring = over.paintedOver(before, [highlight], { where: inRing(over) });
  expect(ring.painted).toBeGreaterThan(ring.total / 5);
});

test('a tinted avatar keeps its circle in every tone when the tint is forced away', async ({
  page,
}) => {
  await page.goto(storyUrl('avatar'));
  await expect(page.getByTestId('tone-8')).toBeVisible();
  const { canvas } = await palette(page);
  await appStable(page);
  for (let tone = 1; tone <= 8; tone += 1) {
    const pixels = await pixelsOf(page, page.getByTestId(`tone-${tone}`));
    expect(edgesShow(pixels, canvas), `tone ${tone} shows its circle`).toBe(true);
  }
});
