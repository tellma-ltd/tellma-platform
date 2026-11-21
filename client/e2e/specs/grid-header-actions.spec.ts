// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { cell, colHeader, gotoGrid, gridScroller } from '../support/grid';

/**
 * The header-action and row-end battery against the `grid-search-page`
 * story: filter popovers opened from header actions by pointer and by
 * keyboard, the keys that walk the header row, the row-end column holding
 * the column editor at the inline end while the grid scrolls sideways, and
 * the header kept above the empty template — in both directions.
 *
 * Story shape: 5 data columns (0 code, 1 name, 2 qty, 3 region, 4 manager);
 * region is sortable with a filter action, manager filter-only; the row-end
 * header's action opens the column editor. Readouts: `filters` ("none" or
 * "region=<r>, manager~<text>"), `columns` (the shown toggleable keys) and
 * `order`.
 */

/** A header's action button. */
function action(page: Page, testId: 'region-filter' | 'manager-filter' | 'column-editor'): Locator {
  return page.getByTestId(testId);
}

/** An open popover's panel, by its accessible name. */
function panel(page: Page, name: string): Locator {
  return page.getByRole('dialog', { name });
}

/** The row-end header cell. */
function endHeader(page: Page): Locator {
  return page.locator('[data-tm-endhdr]');
}

/** The row-end cell of a view-space row. */
function endCell(page: Page, row: number): Locator {
  return page.locator(`[data-tm-endcell][data-row="${row}"]`);
}

/** One screen pixel, as PNG bytes: equal bytes mean an equal colour. */
async function pixel(page: Page, x: number, y: number): Promise<Buffer> {
  return page.screenshot({ clip: { x: Math.floor(x), y: Math.floor(y), width: 1, height: 1 } });
}

for (const dir of ['ltr', 'rtl'] as const) {
  // The arrows toward the inline end and the inline start, by direction.
  const forward = dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft';
  const backward = dir === 'ltr' ? 'ArrowLeft' : 'ArrowRight';

  test.describe(`header actions and the row-end column (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await gotoGrid(page, 'grid-search-page', { dir });
    });

    test('a header action opens its filter in place, without sorting, and marks itself active', async ({
      page,
    }) => {
      await action(page, 'region-filter').click();
      await expect(panel(page, 'Filter Region')).toBeVisible();
      await expect(page.getByTestId('order')).toHaveText('none');
      // Focus moved into the popover; the grid leaves it there.
      await expect(page.getByTestId('region-filter-select')).toBeFocused();

      await page.getByTestId('region-filter-select').selectOption('North');
      await expect(page.getByTestId('filters')).toHaveText('region=North');
      await expect(action(page, 'region-filter')).toHaveClass(/tm-grid-header-action--active/);
      await expect(cell(page, 0, 3)).toHaveText('North');

      // Escape closes it onto its button, which is header focus: the arrows go on.
      await page.keyboard.press('Escape');
      await expect(panel(page, 'Filter Region')).toHaveCount(0);
      await expect(action(page, 'region-filter')).toBeFocused();
      await page.keyboard.press(forward);
      await expect(colHeader(page, 4)).toBeFocused();
    });

    test('the keys walk the header row, open actions and sort', async ({ page }) => {
      await cell(page, 2, 4).click();
      await page.keyboard.press('ArrowUp');
      await page.keyboard.press('ArrowUp');
      await expect(cell(page, 0, 4)).toBeFocused();
      // ArrowUp on the first row enters the active column's header.
      await page.keyboard.press('ArrowUp');
      await expect(colHeader(page, 4)).toBeFocused();
      await expect(colHeader(page, 4)).toHaveAccessibleDescription('Press Enter to open.');

      // Enter on the filter-only header opens its filter.
      await page.keyboard.press('Enter');
      await expect(page.getByTestId('manager-filter-input')).toBeFocused();
      await page.keyboard.type('ada');
      await expect(page.getByTestId('filters')).toHaveText('manager~ada');
      await page.keyboard.press('Escape');
      await expect(action(page, 'manager-filter')).toBeFocused();

      // The start-ward arrow, mapped through the direction, reaches Region.
      await page.keyboard.press(backward);
      await expect(colHeader(page, 3)).toBeFocused();
      await expect(colHeader(page, 3)).toHaveAccessibleDescription(/^Enter to sort\. .+ to filter\.$/);
      await page.keyboard.press('Alt+ArrowDown');
      await expect(panel(page, 'Filter Region')).toBeVisible();
      await page.keyboard.press('Escape');
      // Back on the filter's own button, Enter is the button's: it reopens.
      await expect(action(page, 'region-filter')).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(panel(page, 'Filter Region')).toBeVisible();
      await page.keyboard.press('Escape');
      // On the header cell itself, Enter sorts.
      await page.keyboard.press(forward);
      await page.keyboard.press(backward);
      await expect(colHeader(page, 3)).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(page.getByTestId('order')).toHaveText('region asc');
      await expect(colHeader(page, 3)).toBeFocused();

      // End reaches the column editor; ArrowDown returns to the first row.
      await page.keyboard.press('End');
      await expect(endHeader(page)).toBeFocused();
      await page.keyboard.press('Space');
      await expect(panel(page, 'Choose columns')).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(action(page, 'column-editor')).toBeFocused();
      await page.keyboard.press('Home');
      await expect(page.locator('[data-tm-checkall]')).toBeFocused();
      await page.keyboard.press('ArrowDown');
      await expect(cell(page, 0, 0)).toBeFocused();

      // The grid stays one tab stop: Tab from a header leaves the grid. The
      // roving tab stop moves on the next render, so wait for it to leave the
      // cell before pressing Tab.
      await page.keyboard.press('ArrowUp');
      await expect(colHeader(page, 0)).toBeFocused();
      await expect(colHeader(page, 0)).toHaveAttribute('tabindex', '0');
      await expect(cell(page, 0, 0)).toHaveAttribute('tabindex', '-1');
      await page.keyboard.press('Tab');
      expect(
        await gridScroller(page).evaluate((scroller) => scroller.contains(document.activeElement)),
      ).toBe(false);
      // And Shift+Tab comes back to the header that held the stop.
      await page.keyboard.press('Shift+Tab');
      await expect(colHeader(page, 0)).toBeFocused();
    });

    test('the column editor shows and hides columns', async ({ page }) => {
      await action(page, 'column-editor').click();
      await expect(panel(page, 'Choose columns')).toBeVisible();
      await page.getByTestId('column-manager').click();
      await expect(page.getByTestId('columns')).toHaveText('qty, region');
      await expect(action(page, 'column-editor')).toHaveClass(/tm-grid-header-action--active/);
      await expect(gridScroller(page)).toHaveAttribute('aria-colcount', '6');
      await expect(page.locator('[data-tm-colhdr][data-key="manager"]')).toHaveCount(0);
      await page.getByTestId('column-manager').click();
      await expect(page.getByTestId('columns')).toHaveText('qty, region, manager');
      await expect(gridScroller(page)).toHaveAttribute('aria-colcount', '7');
    });

    test("toggling an action's active state moves nothing in its header", async ({ page }) => {
      const header = colHeader(page, 3);
      const label = header.locator('.tm-grid__colhdr-label');
      const button = action(page, 'region-filter');
      const before = [(await label.boundingBox())!, (await button.boundingBox())!];
      expect(Math.round(before[1].width)).toBe(24);
      expect(Math.round(before[1].height)).toBe(24);

      await button.click();
      await page.getByTestId('region-filter-select').selectOption('South');
      await page.keyboard.press('Escape');
      await expect(button).toHaveClass(/tm-grid-header-action--active/);
      const after = [(await label.boundingBox())!, (await button.boundingBox())!];
      expect(after).toEqual(before);
    });

    test('the header stays above the empty template, its actions in reach', async ({ page }) => {
      await action(page, 'manager-filter').click();
      await page.getByTestId('manager-filter-input').fill('no such manager');
      await page.keyboard.press('Escape');
      await expect(page.getByTestId('empty-state')).toBeVisible();
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '1');

      // The header row, the row-end header included, sits above the empty layer.
      const empty = (await page.locator('[data-tm-empty]').boundingBox())!;
      for (const header of [colHeader(page, 4), endHeader(page)]) {
        const box = (await header.boundingBox())!;
        expect(box.y + box.height).toBeLessThanOrEqual(empty.y + 1);
      }
      await action(page, 'column-editor').click();
      await expect(panel(page, 'Choose columns')).toBeVisible();
      await page.keyboard.press('Escape');

      // The filter that emptied the list reopens from its header.
      await action(page, 'manager-filter').click();
      await page.getByTestId('manager-filter-input').fill('');
      await page.keyboard.press('Escape');
      await expect(page.getByTestId('empty-state')).toHaveCount(0);
      await expect(page.getByTestId('filters')).toHaveText('none');
    });

    test('the row-end column stays at the inline end while the grid scrolls sideways', async ({
      page,
    }) => {
      await page.setViewportSize({ width: 640, height: 720 });
      const scroller = gridScroller(page);
      const overflow = await scroller.evaluate((el) => el.scrollWidth - el.clientWidth);
      expect(overflow).toBeGreaterThan(50);

      /**
       * Where the column's inline-end edge may sit, in page pixels: from the
       * client box's inline end to the border's inner edge. The span is the
       * scrollbar gutter: a visible scrollbar bounds the sticky column at the
       * client box, while a headless browser hides the scrollbar but keeps
       * its gutter, and the column then sticks to the border.
       */
      const endRange = (): Promise<readonly [number, number]> =>
        scroller.evaluate((el, rtl) => {
          const box = el as HTMLElement;
          const rect = box.getBoundingClientRect();
          const border = Number.parseFloat(getComputedStyle(box).borderLeftWidth) || 0;
          const gutter = box.offsetWidth - box.clientWidth - 2 * border;
          // Under right-to-left the gutter sits at the left edge.
          return rtl
            ? ([rect.left + border, rect.left + border + gutter] as const)
            : ([rect.left + border + box.clientWidth, rect.right - border] as const);
        }, dir === 'rtl');
      const edge = async (locator: Locator): Promise<number> => {
        const box = (await locator.boundingBox())!;
        return dir === 'ltr' ? box.x + box.width : box.x;
      };
      /** Whether the column's header and a body cell both sit at the inline end. */
      const atInlineEnd = async (): Promise<void> => {
        const [low, high] = await endRange();
        for (const locator of [endHeader(page), endCell(page, 3)]) {
          const at = await edge(locator);
          expect(at).toBeGreaterThanOrEqual(low - 1);
          expect(at).toBeLessThanOrEqual(high + 1);
        }
      };

      // At the start, content hides under the column: its hairline shows.
      await atInlineEnd();
      const hairline = dir === 'ltr' ? 'border-left-color' : 'border-right-color';
      const transparent = 'rgba(0, 0, 0, 0)';
      await expect(endCell(page, 3)).not.toHaveCSS(hairline, transparent);

      // Scrolled to the far end, the column is still at the inline end, and
      // nothing hides under it.
      await scroller.evaluate((el, rtl) => {
        el.scrollLeft = rtl ? -el.scrollWidth : el.scrollWidth;
      }, dir === 'rtl');
      await expect(endCell(page, 3)).toHaveCSS(hairline, transparent);
      await atInlineEnd();
      // The last data column ends where the row-end column starts.
      const manager = (await colHeader(page, 4).boundingBox())!;
      const endBox = (await endHeader(page).boundingBox())!;
      if (dir === 'ltr') {
        expect(Math.abs(manager.x + manager.width - endBox.x)).toBeLessThanOrEqual(1);
      } else {
        expect(Math.abs(endBox.x + endBox.width - manager.x)).toBeLessThanOrEqual(1);
      }
    });

    test('the row-end cells continue each row band', async ({ page }) => {
      await page.mouse.move(0, 0);
      // An odd row carries the stripe; an active row its tint.
      await cell(page, 2, 0).click();
      await page.mouse.move(0, 0);
      for (const row of [1, 2]) {
        const data = (await cell(page, row, 2).boundingBox())!;
        const end = (await endCell(page, row).boundingBox())!;
        const y = data.y + data.height - 4;
        // A point in the data cell's padding, clear of its text and rules.
        const dataX = dir === 'ltr' ? data.x + 3 : data.x + data.width - 4;
        const endX = end.x + end.width / 2;
        expect((await pixel(page, endX, y)).equals(await pixel(page, dataX, y)), `row ${row}`).toBe(
          true,
        );
      }
    });

    for (const theme of ['light', 'dark'] as const) {
      test(`the story is axe-clean with a filter popover open (${theme})`, async ({ page }) => {
        await gotoGrid(page, 'grid-search-page', { dir, theme });
        await action(page, 'region-filter').click();
        await page.getByTestId('region-filter-select').selectOption('East');
        await expect(panel(page, 'Filter Region')).toBeVisible();
        await expectNoAxeViolations(page);

        await page.keyboard.press('Escape');
        await action(page, 'column-editor').click();
        await expect(panel(page, 'Choose columns')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  });
}
