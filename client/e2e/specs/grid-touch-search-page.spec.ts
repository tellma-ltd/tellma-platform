// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { cell, centerOf, colHeader, gotoGrid } from '../support/grid';

/**
 * The search page on touch — runs ONLY on the `touch`
 * project (a Pixel 7 descriptor): a tap opens its row, a tap on a sortable
 * header cycles the sort, and a cell link keeps the platform's own tap and
 * long-press, with no grid menu over it.
 *
 * The gestures target the first column, at the inline start in either
 * direction (the right edge in RTL).
 */

/** A point on the screen, in CSS pixels. */
interface Point {
  readonly x: number;
  readonly y: number;
}

/** The library context menu's panel. */
function menuPanel(page: Page) {
  return page.locator('.tm-menu__panel');
}

/**
 * Presses a finger down and holds it until `until` resolves, then lifts it
 * as a cancelled tap, the way a gesture recognizer ends a consumed
 * long-press (no synthesized click).
 */
async function touchHold(page: Page, point: Point, until: () => Promise<void>): Promise<void> {
  const client = await page.context().newCDPSession(page);
  try {
    await client.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: [{ x: point.x, y: point.y, id: 1 }],
    });
    await until();
    await client.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
  } finally {
    await client.detach();
  }
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`search page on touch (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await gotoGrid(page, 'grid-search-page', { dir });
    });

    test('the page fits the phone: nothing scrolls sideways', async ({ page }) => {
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        ),
      ).toBeLessThanOrEqual(0);
    });

    test('a tap opens the row', async ({ page }) => {
      await cell(page, 2, 0).tap();
      await expect(page.getByTestId('activated')).toHaveText('2 via pointer');
      await expect(page.getByTestId('active-row')).toHaveText('2');
    });

    test('a tap on a sortable header cycles the sort', async ({ page }) => {
      await colHeader(page, 0).tap();
      await expect(page.getByTestId('order')).toHaveText('code asc');
      await expect(colHeader(page, 0)).toHaveAttribute('aria-sort', 'ascending');
      await page.waitForTimeout(450); // past the double-tap window: two single taps
      await colHeader(page, 0).tap();
      await expect(page.getByTestId('order')).toHaveText('code desc');
      await expect(page.getByTestId('activation-count')).toHaveText('0');
    });

    test('a cell link keeps its tap and its long-press', async ({ page }) => {
      const link = cell(page, 3, 1).locator('a.item-link');
      await link.tap();
      await expect(page.getByTestId('link-clicked')).toHaveText('3');
      await expect(page.getByTestId('activation-count')).toHaveText('0');

      // A hold on the link outlasts the grid's 500 ms long-press, and no
      // grid menu opens: the platform's own link menu is left alone.
      await touchHold(page, await centerOf(link), async () => {
        await page.waitForTimeout(900);
        await expect(menuPanel(page)).toHaveCount(0);
      });
      await expect(menuPanel(page)).toHaveCount(0);

      // The same hold elsewhere in a cell opens the grid's menu.
      await touchHold(page, await centerOf(cell(page, 5, 0)), async () => {
        await expect(menuPanel(page)).toBeVisible();
      });
      await expect(page.getByTestId('activation-count')).toHaveText('0');
    });
  });
}
