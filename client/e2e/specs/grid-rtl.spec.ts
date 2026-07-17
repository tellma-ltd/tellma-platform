// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { activeCell, cell, colHeader, gotoGrid } from '../support/grid';

/**
 * RTL battery (spec 0004 §15): logical column order mirrors, physical
 * arrows map to inline directions in the engine, deliberate physical
 * alignment stays physical, and a live dir flip keeps the grid coherent.
 */

test.describe('mirrored layout', () => {
  test('the first column renders at the inline start — the RIGHT side in RTL', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });

    const first = (await colHeader(page, 0).boundingBox())!;
    const second = (await colHeader(page, 1).boundingBox())!;
    expect(first.x).toBeGreaterThan(second.x);

    // Cells mirror with their headers.
    const cellFirst = (await cell(page, 0, 0).boundingBox())!;
    const cellSecond = (await cell(page, 0, 1).boundingBox())!;
    expect(cellFirst.x).toBeGreaterThan(cellSecond.x);
  });

  test('number columns keep their deliberate physical right alignment', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });

    const align = await cell(page, 0, 2).evaluate((el) => getComputedStyle(el).textAlign);
    expect(align).toBe('right');
  });
});

test.describe('direction-mapped arrows (§15)', () => {
  test('ArrowLeft moves toward inline-end (a HIGHER data-col) in RTL', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });
    await cell(page, 2, 1).click();

    await page.keyboard.press('ArrowLeft'); // physically left = logically forward
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
    await page.keyboard.press('ArrowLeft');
    await expect(activeCell(page)).toHaveAttribute('data-col', '3');

    await page.keyboard.press('ArrowRight'); // physically right = logically back
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
  });

  test('Home is the logical row start: data-col 0, sitting at the right edge', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });
    await cell(page, 2, 3).click();

    await page.keyboard.press('Home');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');

    const home = (await cell(page, 2, 0).boundingBox())!;
    const next = (await cell(page, 2, 1).boundingBox())!;
    expect(home.x).toBeGreaterThan(next.x); // logical start = physical right
  });
});

test.describe('live dir flip (shell Dir wrapper)', () => {
  test('a runtime flip re-maps arrows and mirrors the template', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly'); // fresh LTR load
    await cell(page, 1, 1).click();
    await page.keyboard.press('ArrowLeft'); // LTR: left = back
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');

    await page.getByTestId('lang-ar').click(); // flip at runtime — no reload
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    // Template mirrored live…
    await expect
      .poll(async () => {
        const first = (await colHeader(page, 0).boundingBox())!;
        const second = (await colHeader(page, 1).boundingBox())!;
        return first.x > second.x;
      })
      .toBe(true);

    // …and the engine's arrow mapping followed the live Directionality.
    await cell(page, 1, 1).click();
    await page.keyboard.press('ArrowLeft'); // now inline-end
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
  });
});
