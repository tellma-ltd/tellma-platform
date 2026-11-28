// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { activateCell, cell, colHeader, gotoGrid, gridScroller } from '../support/grid';

/**
 * The scrollbar gutter. Headless Chromium hides scrollbars,
 * so this file relaunches it with classic scrollbars shown: arriving rows
 * must not re-flow a flexible column, and the reserved strip must read as
 * part of the grid — the header's colour beside the header row, the body's
 * own background below it — and the active cell must clear it. (Hidden,
 * the scrollbars leave a gutter that Chromium still reserves while capping
 * the scroll range as if it were not there, so only a run with scrollbars
 * shown can tell whether the last column clears it.)
 */

test.use({ launchOptions: { ignoreDefaultArgs: ['--hide-scrollbars'] } });

test.skip(({ browserName }) => browserName !== 'chromium', 'the scrollbar switch is Chromium-only');

/** The reserved gutter's width and the inline position of its middle, in viewport pixels. */
async function gutter(page: Page): Promise<{ width: number; x: number }> {
  return gridScroller(page).evaluate((scroller: HTMLElement) => {
    const rect = scroller.getBoundingClientRect();
    const style = getComputedStyle(scroller);
    const left = parseFloat(style.borderLeftWidth);
    const right = parseFloat(style.borderRightWidth);
    const width = scroller.offsetWidth - scroller.clientWidth - left - right;
    // The vertical scrollbar sits at the inline end: the right in LTR, the left in RTL.
    const x =
      style.direction === 'rtl' ? rect.left + left + width / 2 : rect.right - right - width / 2;
    return { width, x };
  });
}

/** One screen pixel, as PNG bytes: equal bytes mean an equal colour. */
async function pixel(page: Page, x: number, y: number): Promise<Buffer> {
  return page.screenshot({ clip: { x: Math.floor(x), y: Math.floor(y), width: 1, height: 1 } });
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`scrollbar gutter (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await gotoGrid(page, 'grid-search-page', { dir });
    });

    test('rows arriving never re-flow a flexible column', async ({ page }) => {
      await page.getByTestId('toggle-rows').click();
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '1');
      // The gutter is reserved with nothing to scroll.
      expect((await gutter(page)).width).toBeGreaterThan(0);
      const before = (await colHeader(page, 1).boundingBox())!;

      await page.getByTestId('toggle-rows').click();
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '51');
      expect(
        await gridScroller(page).evaluate(
          (scroller) => scroller.scrollHeight > scroller.clientHeight,
        ),
      ).toBe(true);
      const after = (await colHeader(page, 1).boundingBox())!;
      expect([after.x, after.width]).toEqual([before.x, before.width]);
    });

    test("a short result's gutter paints the header and body bands", async ({ page }) => {
      await page.getByTestId('search').fill('Item 99');
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '2');
      const { width, x } = await gutter(page);
      expect(width).toBeGreaterThan(0);
      const scroller = (await gridScroller(page).boundingBox())!;
      const header = (await page.locator('[data-tm-checkhdr]').boundingBox())!;

      // The body band: the strip below the rows matches the empty body beside it.
      const bodyY = scroller.y + scroller.height - 8;
      const bodyX = scroller.x + scroller.width / 2;
      expect((await pixel(page, x, bodyY)).equals(await pixel(page, bodyX, bodyY))).toBe(true);

      // The header band: the strip beside the header row matches a header cell.
      const headerY = header.y + 3;
      const headerX = dir === 'ltr' ? header.x + 3 : header.x + header.width - 4;
      expect((await pixel(page, x, headerY)).equals(await pixel(page, headerX, headerY))).toBe(
        true,
      );
    });
  });
}

/** The editable story's last data column (the due date). */
const LAST_COL = 8;

for (const dir of ['ltr', 'rtl'] as const) {
  test(`arrowing onto the last column clears the scrollbar gutter (${dir})`, async ({ page }) => {
    await gotoGrid(page, 'grid-editable', { dir });
    await activateCell(page, 0, 0);
    for (let col = 0; col < LAST_COL; col++) {
      await page.keyboard.press(dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft');
    }
    await expect(cell(page, 0, LAST_COL)).toBeFocused();

    // The scroller's border box counts the vertical scrollbar's gutter as
    // visible space; the cell has to clear the CLIENT box.
    const viewport = await gridScroller(page).evaluate((element: HTMLElement) => {
      const rect = element.getBoundingClientRect();
      const gutter = element.offsetWidth - element.clientWidth;
      const rtl = getComputedStyle(element).direction === 'rtl';
      return { left: rect.left + (rtl ? gutter : 0), right: rect.right - (rtl ? 0 : gutter) };
    });
    expect(viewport.right - viewport.left).toBeLessThan(
      (await gridScroller(page).boundingBox())!.width - 4,
    );
    const box = (await cell(page, 0, LAST_COL).boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(viewport.left - 1);
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.right + 1);
  });
}
