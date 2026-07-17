// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import {
  activeCell,
  cell,
  centerOf,
  colHeader,
  dragBetween,
  gotoGrid,
  gridScroller,
  rowHeader,
  scrollTopOf,
  selectedCells,
} from '../support/grid';

/**
 * Real-pointer battery for the readonly grid (spec 0004 §8.3): press/drag
 * range selection with pointer capture, edge auto-scroll, header gestures,
 * the select-all corner, and live column resize.
 */

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-readonly');
});

test.describe('cell presses', () => {
  test('a click activates and selects the cell', async ({ page }) => {
    await cell(page, 2, 1).click();

    await expect(cell(page, 2, 1)).toBeFocused();
    await expect(cell(page, 2, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(selectedCells(page)).toHaveCount(1);
  });

  test('Shift+click extends the range from the anchor', async ({ page }) => {
    await cell(page, 2, 1).click();
    await cell(page, 6, 3).click({ modifiers: ['Shift'] });

    await expect(selectedCells(page)).toHaveCount(15); // 5 rows × 3 cols
    await expect(cell(page, 2, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 6, 3)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 1, 1)).not.toHaveAttribute('aria-selected');
  });

  test('Control+click adds a discontiguous range', async ({ page }) => {
    await cell(page, 2, 1).click();
    await cell(page, 8, 5).click({ modifiers: ['Control'] });

    await expect(cell(page, 2, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 8, 5)).toHaveAttribute('aria-selected', 'true');
    await expect(selectedCells(page)).toHaveCount(2); // two 1×1 rects, nothing between
    await expect(cell(page, 5, 3)).not.toHaveAttribute('aria-selected');
    await expect(cell(page, 8, 5)).toBeFocused(); // the added range's cell became active
  });

  test('clicking interactive content inside a cell still selects the cell', async ({ page }) => {
    await cell(page, 3, 0).locator('a').click();

    // The press behaves as a cell press: selection + activation are intact.
    // Focus follows the native link press (the link is inside the active
    // cell), never a forced jump that would break the navigation.
    await expect(cell(page, 3, 0)).toHaveAttribute('aria-selected', 'true');
    await expect(activeCell(page)).toHaveAttribute('data-row', '3');
    const focusInsideCell = await cell(page, 3, 0).evaluate(
      (el) => el === document.activeElement || el.contains(document.activeElement),
    );
    expect(focusInsideCell).toBe(true);
  });

  test('a mouse click on a cell-embedded record link activates it', async ({ page }) => {
    // A press on interactive display content keeps its native affordances:
    // the grid activates the cell without preventDefault or pointer
    // capture, so the click reaches the link and navigation happens.
    await cell(page, 3, 0).locator('a').click();
    await expect(page).toHaveURL(/#record-3$/);
  });
});

test.describe('drag selection', () => {
  test('a drag from cell to cell selects the rectangle', async ({ page }) => {
    await dragBetween(page, cell(page, 1, 1), cell(page, 4, 3));

    await expect(selectedCells(page)).toHaveCount(12); // 4 rows × 3 cols
    await expect(cell(page, 1, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 4, 3)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 5, 2)).not.toHaveAttribute('aria-selected');
  });

  test('holding a drag near the bottom edge auto-scrolls the grid', async ({ page }) => {
    const scrollerBox = (await gridScroller(page).boundingBox())!;
    const start = await centerOf(cell(page, 2, 1));

    await page.mouse.move(start.x, start.y);
    await page.mouse.down();
    // Park the pointer inside the edge auto-scroll zone and hold it there.
    const edgeX = start.x;
    const edgeY = scrollerBox.y + scrollerBox.height - 8;
    await page.mouse.move(edgeX, edgeY, { steps: 5 });
    await page.mouse.move(edgeX, edgeY + 1);

    await expect
      .poll(() => scrollTopOf(page), { timeout: 15_000, message: 'edge auto-scroll never ran' })
      .toBeGreaterThan(200);
    await page.mouse.up();

    // The drag extended the selection to the rows it scrolled past.
    const count = await selectedCells(page).count();
    expect(count).toBeGreaterThan(1);
  });
});

test.describe('header gestures', () => {
  test('a row-header click selects the full row', async ({ page }) => {
    await rowHeader(page, 3).click();

    await expect(rowHeader(page, 3)).toHaveClass(/tm-grid__rowhdr--hit/);
    await expect(cell(page, 3, 0)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 3, 11)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 2, 0)).not.toHaveAttribute('aria-selected');
  });

  test('a drag across row headers selects the row span', async ({ page }) => {
    await dragBetween(page, rowHeader(page, 2), rowHeader(page, 6));

    for (const row of [2, 4, 6]) {
      await expect(rowHeader(page, row)).toHaveClass(/tm-grid__rowhdr--hit/);
      await expect(cell(page, row, 5)).toHaveAttribute('aria-selected', 'true');
    }
    await expect(rowHeader(page, 1)).not.toHaveClass(/tm-grid__rowhdr--hit/);
  });

  test('a column-header click selects the full column', async ({ page }) => {
    await colHeader(page, 2).click();

    await expect(colHeader(page, 2)).toHaveClass(/tm-grid__colhdr--hit/);
    await expect(cell(page, 0, 2)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 9, 2)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 0, 1)).not.toHaveAttribute('aria-selected');
    await expect(activeCell(page)).toHaveAttribute('data-row', '0'); // activation follows
  });

  test('a corner click selects all', async ({ page }) => {
    await page.locator('[data-tm-corner]').click();

    await expect(cell(page, 0, 0)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 10, 11)).toHaveAttribute('aria-selected', 'true');
    await expect(rowHeader(page, 5)).toHaveClass(/tm-grid__rowhdr--hit/);
    await expect(colHeader(page, 7)).toHaveClass(/tm-grid__colhdr--hit/);
  });
});

test.describe('column resize (§8.3 header edge drag)', () => {
  test('dragging the resize handle changes the column width live', async ({ page }) => {
    const header = colHeader(page, 2); // Qty, fixed width 90
    const before = (await header.boundingBox())!;

    const handle = header.locator('.tm-grid__resize');
    const grip = await centerOf(handle);
    await page.mouse.move(grip.x, grip.y);
    await page.mouse.down();
    await page.mouse.move(grip.x + 40, grip.y, { steps: 6 });

    // Live update: the width grows while the pointer is still down.
    await expect.poll(async () => (await header.boundingBox())!.width).toBeGreaterThan(
      before.width + 30,
    );
    await page.mouse.up();

    const after = (await header.boundingBox())!;
    expect(Math.abs(after.width - (before.width + 40))).toBeLessThanOrEqual(2);

    // Every row follows the shared template: the cell column resized too.
    const cellBox = (await cell(page, 0, 2).boundingBox())!;
    expect(Math.abs(cellBox.width - after.width)).toBeLessThanOrEqual(2);
  });
});
