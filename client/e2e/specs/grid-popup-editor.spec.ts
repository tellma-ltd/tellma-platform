// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import {
  activateCell,
  activeCell,
  cell,
  gotoGrid,
  gridScroller,
  modelJson,
  rowHeightOf,
  setScrollTop,
} from '../support/grid';

/**
 * The popup-editor battery against the `grid-popup-editor` story: an
 * editor taller than a row opens in a pane over its cell that spills past
 * the grid while every row keeps its height, flips upward at the viewport
 * edge, aligns to the cell's inline start in both directions, keeps focus
 * inside the gridcell, commits on a press outside, and hides while its
 * cell is scrolled out of the grid's view or off screen, letting the
 * pointer through.
 *
 * Story shape: 4 columns (0 user, 1 role, 2 window — a two-field popup
 * editor of least width 360, 3 filter — a multi-line popup editor of least
 * width 480, read-only on Administrator rows, every fourth from row 2), 30
 * rows in a 320 px grid, a `toggle-readonly` button and the `model-json`
 * dump.
 */

/** A membership row as the model dump shows it. */
interface Membership {
  readonly id: number;
  readonly filter: string | null;
}

/** The open popup pane's body. */
function pane(page: Page): Locator {
  return page.locator('[data-tm-popup]');
}

/** A rectangle's four edges, from a locator's bounding box. */
async function edges(
  locator: Locator,
): Promise<{ top: number; bottom: number; left: number; right: number }> {
  const box = await locator.boundingBox();
  if (box === null) {
    throw new Error('element has no bounding box');
  }
  return { top: box.y, bottom: box.y + box.height, left: box.x, right: box.x + box.width };
}

/** The boxes of the first rendered rows: what an open pane must never move. */
async function rowBoxes(page: Page): Promise<string> {
  return page
    .locator('.tm-grid__row')
    .evaluateAll((rows) =>
      rows.slice(0, 9).map((row) => JSON.stringify(row.getBoundingClientRect())),
    )
    .then((boxes) => boxes.join('|'));
}

/** Opens the pane of a cell with F2 and waits until the CDK has placed it on the cell. */
async function openPane(page: Page, row: number, col: number): Promise<void> {
  await activateCell(page, row, col);
  await page.keyboard.press('F2');
  await expect(pane(page)).toBeVisible();
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`popup editors (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await gotoGrid(page, 'grid-popup-editor', { dir });
    });

    test('@cross-engine the pane spills past the grid bottom over its cell, moving no row', async ({
      page,
    }) => {
      const before = await rowBoxes(page);
      await openPane(page, 7, 3);
      const target = cell(page, 7, 3);
      const cellBox = await edges(target);
      // Over the cell's own box, growing toward the block end.
      await expect.poll(async () => Math.round((await edges(pane(page))).top)).toBe(
        Math.round(cellBox.top),
      );
      const paneBox = await edges(pane(page));
      expect(paneBox.bottom).toBeGreaterThan((await edges(gridScroller(page))).bottom);
      expect(paneBox.right - paneBox.left).toBeGreaterThanOrEqual(480);
      // It takes no layout space: every row keeps its box.
      expect(await rowBoxes(page)).toBe(before);

      // Focus is inside the gridcell, in the editor's own field.
      await expect(page.getByTestId('filter-input')).toBeFocused();
      expect(
        await page.evaluate(() => document.activeElement?.closest('[role="gridcell"]')?.getAttribute('data-col')),
      ).toBe('3');
    });

    test('@cross-engine the pane flips upward when the viewport has no room below', async ({
      page,
    }) => {
      const cellBox = await edges(cell(page, 7, 3));
      const viewport = page.viewportSize()!;
      // Leave less room under the cell than the pane needs.
      await page.setViewportSize({ width: viewport.width, height: Math.ceil(cellBox.bottom) + 40 });
      await openPane(page, 7, 3);
      const flipped = await edges(cell(page, 7, 3));
      // Its bottom on the cell's: the pane anchors to the cell's editor box,
      // which stops at the cell's bottom gridline, a pixel above its edge.
      await expect
        .poll(async () => Math.abs((await edges(pane(page))).bottom - flipped.bottom))
        .toBeLessThanOrEqual(1.5);
      expect((await edges(pane(page))).top).toBeLessThan(flipped.top);
    });

    test(`@cross-engine the pane aligns to its cell's inline start (${dir})`, async ({ page }) => {
      await openPane(page, 1, 2);
      const cellBox = await edges(cell(page, 1, 2));
      const paneEdge = async (): Promise<number> => {
        const box = await edges(pane(page));
        return Math.round(dir === 'ltr' ? box.left : box.right);
      };
      await expect.poll(paneEdge).toBe(Math.round(dir === 'ltr' ? cellBox.left : cellBox.right));
      const paneBox = await edges(pane(page));
      // Wider than its cell: the column's least width.
      expect(Math.round(paneBox.right - paneBox.left)).toBe(360);
    });

    test('@cross-engine a press outside the grid commits the pane', async ({ page }) => {
      await openPane(page, 1, 3);
      await page.getByTestId('filter-input').fill('Name = me()');
      await page.getByRole('heading', { name: 'Grid (popup editors)' }).click();
      await expect(pane(page)).toHaveCount(0);
      const rows = await modelJson<Membership[]>(page);
      expect(rows[1].filter).toBe('Name = me()');
    });

    test('@cross-engine the pane hides while its cell is scrolled away, and the next key brings it back', async ({
      page,
    }) => {
      await openPane(page, 0, 3);
      await page.getByTestId('filter-input').fill('Kept');
      await setScrollTop(page, 480);
      await expect(pane(page)).toHaveClass(/tm-grid__popup--hidden/);
      await setScrollTop(page, 0);
      await expect(pane(page)).not.toHaveClass(/tm-grid__popup--hidden/);

      // Scrolled away again, a keystroke scrolls the cell back first; the
      // editor kept its text and its focus throughout.
      await setScrollTop(page, 480);
      await expect(pane(page)).toHaveClass(/tm-grid__popup--hidden/);
      await page.keyboard.press('End');
      await page.keyboard.type('!');
      await expect(pane(page)).not.toHaveClass(/tm-grid__popup--hidden/);
      await expect(cell(page, 0, 3)).toBeInViewport();
      await expect(page.getByTestId('filter-input')).toHaveValue('Kept!');
    });

    test('@cross-engine a hidden pane lets a press through to the cell under it, which commits the pane', async ({
      page,
    }) => {
      await openPane(page, 0, 3);
      await page.getByTestId('filter-input').fill('Hidden');
      // One row down: the cell goes behind the sticky header and its pane
      // hides, while the pane's box still spans the rows below it.
      await setScrollTop(page, Math.ceil(await rowHeightOf(page)) + 2);
      await expect(pane(page)).toHaveClass(/tm-grid__popup--hidden/);
      const target = await edges(cell(page, 3, 3));
      const x = (target.left + target.right) / 2;
      const y = (target.top + target.bottom) / 2;
      const paneBox = await edges(pane(page));
      expect(paneBox.bottom).toBeGreaterThan(y);
      expect(paneBox.top).toBeLessThan(y);

      await page.mouse.click(x, y);
      await expect(pane(page)).toHaveCount(0);
      await expect(activeCell(page)).toHaveAttribute('data-row', '3');
      const rows = await modelJson<Membership[]>(page);
      expect(rows[0].filter).toBe('Hidden');
    });

    test('@cross-engine the pane hides while the page scrolls its cell off screen, and returns with it', async ({
      page,
    }) => {
      const viewport = page.viewportSize()!;
      await page.setViewportSize({ width: viewport.width, height: 330 });
      await openPane(page, 0, 3);
      const opened = await edges(pane(page));
      const cellBox = await edges(cell(page, 0, 3));

      // The page, not the grid, scrolls the cell above the viewport.
      await page.evaluate((by) => window.scrollBy(0, by), Math.ceil(cellBox.bottom) + 20);
      expect((await edges(cell(page, 0, 3))).bottom).toBeLessThan(0);
      await expect(pane(page)).toHaveClass(/tm-grid__popup--hidden/);

      await page.evaluate(() => window.scrollTo(0, 0));
      await expect(pane(page)).not.toHaveClass(/tm-grid__popup--hidden/);
      await expect.poll(async () => Math.round((await edges(pane(page))).top)).toBe(
        Math.round(opened.top),
      );
    });

    test('@cross-engine a press on the pane outside its controls keeps the editor focused, its keys inside', async ({
      page,
    }) => {
      await openPane(page, 1, 3);
      await page.locator('.filter-editor__hint').click();
      await page.keyboard.type('xyz');
      await expect(page.getByTestId('filter-input')).toHaveValue('xyz');
      // ArrowDown stays in the editor: no commit, no move.
      await page.keyboard.press('ArrowDown');
      await expect(page.getByTestId('filter-input')).toBeFocused();
      await expect(pane(page)).toBeVisible();
      await expect(activeCell(page)).toHaveAttribute('data-row', '1');
    });

    test('@cross-engine view mode opens the pane read-only on F2 and Escape closes it', async ({
      page,
    }) => {
      await page.getByTestId('toggle-readonly').click();
      await openPane(page, 2, 2);
      await expect(pane(page)).toHaveAttribute('data-tm-readonly', '');
      await expect(page.getByTestId('window-starts')).toBeFocused();
      await expect(page.getByTestId('window-starts')).not.toBeEditable();
      await page.keyboard.press('Escape');
      await expect(pane(page)).toHaveCount(0);
      await expect(cell(page, 2, 2)).toBeFocused();
    });

    for (const theme of ['light', 'dark'] as const) {
      test(`a two-field pane is a labelled group, axe-clean while open (${theme})`, async ({
        page,
      }) => {
        await gotoGrid(page, 'grid-popup-editor', { dir, theme });
        await openPane(page, 1, 2);
        await expect(pane(page)).toHaveAttribute('role', 'group');
        await expect(pane(page)).toHaveAttribute('aria-label', 'Window');
        await expectNoAxeViolations(page);
      });
    }
  });
}
