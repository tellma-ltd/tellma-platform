// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { near, paintedColour, pixelsOf, type Rgb, type TmPixels } from '../support/pixels';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * The forced-colors battery: under `forced-colors: active` the browser
 * replaces every colour with the system palette, so a state drawn only by
 * a tint disappears. Each state the library keeps visible is proved from
 * the pixels the browser painted, not from computed styles: the selected
 * tile, the checked segment, the switch turned on, the counter tones, the
 * drop target under a drag and the tinted avatar.
 */

test.beforeEach(async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
});

/** How many pixels of `pixels` are within the default tolerance of `colour`. */
function pixelsNear(pixels: TmPixels, colour: Rgb): number {
  return pixels.width * pixels.height - pixels.pixelsUnlike(colour);
}

/** How many pixels of the outer `depth`-pixel ring of the image are near `colour`, and how many the ring has. */
function ringNear(pixels: TmPixels, colour: Rgb, depth = 2): { near: number; total: number } {
  let count = 0;
  let total = 0;
  for (let y = 0; y < pixels.height; y += 1) {
    for (let x = 0; x < pixels.width; x += 1) {
      const inRing =
        x < depth || y < depth || x >= pixels.width - depth || y >= pixels.height - depth;
      if (inRing) {
        total += 1;
        if (near(pixels.at(x, y), colour)) {
          count += 1;
        }
      }
    }
  }
  return { near: count, total };
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

/** The forced palette's Canvas and Highlight colours, as this page paints them. */
async function palette(page: Page): Promise<{ canvas: Rgb; highlight: Rgb; highlightText: Rgb }> {
  return {
    canvas: await paintedColour(page, 'Canvas'),
    highlight: await paintedColour(page, 'Highlight'),
    highlightText: await paintedColour(page, 'HighlightText'),
  };
}

test('a selected tile draws a highlight outline, an unselected one none', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const tile = (id: number): Locator =>
    page.getByTestId('tiles').locator(`.tm-tiles__item[data-index="${id - 1}"]`);
  await expect(tile(2)).toBeVisible();
  const { highlight } = await palette(page);
  await appStable(page);
  const before = ringNear(await pixelsOf(page, tile(2)), highlight);
  expect(before.near).toBe(0);

  // Section: select the tile, then move the active tile away, so only the
  // selection marks it.
  await tile(2).locator('[role="checkbox"]').click();
  await tile(3).focus();
  await expect(tile(2)).toHaveAttribute('aria-selected', 'true');
  await expect(tile(3)).toBeFocused();
  await appStable(page);
  const after = ringNear(await pixelsOf(page, tile(2)), highlight);
  expect(after.near).toBeGreaterThan(after.total / 2);
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
  const unchecked = await pixelsOf(page, segments.nth(0));
  // The label and the segment's rounded corners take the rest.
  expect(pixelsNear(checked, highlight)).toBeGreaterThan((checked.width * checked.height) / 3);
  expect(pixelsNear(checked, highlightText)).toBeGreaterThan(0);
  expect(pixelsNear(unchecked, highlight)).toBe(0);
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
  const off = await pixelsOf(page, track('switch-notify'));
  expect(pixelsNear(on, highlight)).toBeGreaterThan((on.width * on.height) / 3);
  // The knob stands out on the fill.
  expect(pixelsNear(on, highlightText)).toBeGreaterThan(0);
  expect(pixelsNear(off, highlight)).toBe(0);
});

test('every counter tone keeps a visible edge, and the attention tone alone a highlight fill', async ({
  page,
}) => {
  await page.goto(storyUrl('badge'));
  await expect(page.getByTestId('counter-attention')).toBeVisible();
  const { canvas, highlight } = await palette(page);
  await appStable(page);
  for (const id of ['counter-capped', 'counter-accent', 'counter-attention']) {
    const pixels = await pixelsOf(page, page.getByTestId(id));
    expect(edgesShow(pixels, canvas), `${id} shows its edge`).toBe(true);
    const filled = pixelsNear(pixels, highlight);
    if (id === 'counter-attention') {
      expect(filled, `${id} is filled`).toBeGreaterThan((pixels.width * pixels.height) / 3);
    } else {
      expect(filled, `${id} has no highlight fill`).toBe(0);
    }
  }
});

test('a drop target under an accepted drag draws a highlight outline', async ({ page }) => {
  await page.goto(storyUrl('drag-drop'));
  const well = page.getByTestId('drop-well');
  await expect(well).toBeVisible();
  const { highlight } = await palette(page);
  await appStable(page);
  expect(ringNear(await pixelsOf(page, well), highlight).near).toBe(0);
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
  // A dashed outline: its dashes cover about half the ring.
  const over = ringNear(await pixelsOf(page, well), highlight);
  expect(over.near).toBeGreaterThan(over.total / 5);
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
