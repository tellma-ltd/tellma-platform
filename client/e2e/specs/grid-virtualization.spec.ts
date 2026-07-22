// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { mulberry32 } from '../../projects/internal/showcase/src/app/grid/seeded-random';
import {
  activeCell,
  cell,
  cellText,
  centerOf,
  clientHeightOf,
  colHeader,
  gotoGrid,
  gridScroller,
  renderedRows,
  rowHeader,
  rowHeightOf,
  scrollTopOf,
  selectedCells,
  setScrollTop,
} from '../support/grid';

/**
 * Virtualization structure (spec 0004 §4: window + overscan + the
 * always-rendered active row; the CI-gated structural half of DoD 1) and
 * the §12 state-lifetime walk over the grid-states story.
 */

/** The story's seeded Fisher–Yates, replicated to predict the shuffle. */
function shuffledIds(count: number): number[] {
  const random = mulberry32(42);
  const ids = Array.from({ length: count }, (_, i) => i);
  for (let i = ids.length - 1; i > 0; i--) {
    const j = Math.floor(random() * (i + 1));
    [ids[i], ids[j]] = [ids[j], ids[i]];
  }
  return ids;
}

test.describe('windowed rendering (100k rows)', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
  });

  test('the DOM holds only the window plus overscan; the spacer carries the extent', async ({
    page,
  }) => {
    const rowHeight = await rowHeightOf(page);
    const clientHeight = await clientHeightOf(page);
    // Visible slice + 4 overscan on each side + windowing rounding slack.
    const bound = Math.ceil(clientHeight / rowHeight) + 10;

    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '100001');
    const rendered = await renderedRows(page).count();
    expect(rendered).toBeLessThanOrEqual(bound);
    expect(rendered).toBeLessThan(100);

    const spacerHeight = await page
      .locator('.tm-grid__spacer')
      .evaluate((el) => Number.parseFloat(getComputedStyle(el).height));
    expect(Math.abs(spacerHeight - 100_000 * rowHeight)).toBeLessThanOrEqual(rowHeight);
  });

  test('scrolling to the middle renders that window, aria-rowindex following', async ({
    page,
  }) => {
    const rowHeight = await rowHeightOf(page);
    const clientHeight = await clientHeightOf(page);
    const bound = Math.ceil(clientHeight / rowHeight) + 10;

    await setScrollTop(page, 50_000 * rowHeight);

    await expect(cell(page, 50_000, 0)).toBeAttached();
    await expect(rowHeader(page, 0)).toHaveCount(0); // the origin rows unmounted
    await expect(
      page.locator('.tm-grid__row:has([data-tm-rowhdr][data-row="50000"])'),
    ).toHaveAttribute('aria-rowindex', '50002'); // 1-based, counting the header row
    expect(await renderedRows(page).count()).toBeLessThanOrEqual(bound);
  });

  test('the active row is always rendered: focus survives scrolling it far away', async ({
    page,
  }) => {
    const rowHeight = await rowHeightOf(page);
    await cell(page, 2, 3).click();
    await expect(cell(page, 2, 3)).toBeFocused();

    await setScrollTop(page, 50_000 * rowHeight);
    await expect(cell(page, 50_000, 0)).toBeAttached();

    // The active row rides along as the out-of-window outlier…
    const outlier = page.locator('.tm-grid__row--outlier');
    await expect(outlier).toHaveCount(1);
    await expect(outlier.locator('[data-tm-rowhdr]')).toHaveAttribute('data-row', '2');
    // …and real DOM focus never left its cell.
    await expect(cell(page, 2, 3)).toBeFocused();

    await setScrollTop(page, 0);
    await expect(page.locator('.tm-grid__row--outlier')).toHaveCount(0); // back in the window
    await expect(cell(page, 2, 3)).toBeFocused();
  });

  test('rendered row count is identical for 1k and 100k rows (structural DoD 1)', async ({
    page,
  }) => {
    await page.getByTestId('row-count').selectOption('1000');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '1001');
    const with1k = await renderedRows(page).count();

    await page.getByTestId('row-count').selectOption('100000');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '100001');
    const with100k = await renderedRows(page).count();

    expect(with100k).toBe(with1k);
  });
});

test.describe('state lifetimes (§12 walk on grid-states)', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-states');
  });

  test('scroll and active cell survive unmount/remount for the same content', async ({
    page,
  }) => {
    await setScrollTop(page, 256);
    await cell(page, 20, 1).click();
    await expect(cell(page, 20, 1)).toBeFocused();
    const scrollBefore = await scrollTopOf(page);

    await page.getByTestId('toggle-mount').click();
    await expect(page.locator('[role="grid"]')).toHaveCount(0);
    await page.getByTestId('toggle-mount').click();
    await expect(page.locator('[role="grid"]')).toBeVisible();

    await expect.poll(() => scrollTopOf(page)).toBe(scrollBefore);
    await expect(activeCell(page)).toHaveAttribute('data-row', '20');
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');
    await expect(cell(page, 20, 1)).toHaveAttribute('aria-selected', 'true');
  });

  test('a contentKey switch starts fresh at the origin; switching back restores', async ({
    page,
  }) => {
    await cell(page, 5, 1).click();
    await setScrollTop(page, 128);
    await expect.poll(() => scrollTopOf(page)).toBe(128);

    await page.getByTestId('switch-content').click(); // A → B
    await expect(page.getByTestId('content-key-label')).toHaveText('B');
    await expect.poll(() => scrollTopOf(page)).toBe(0);
    await expect(activeCell(page)).toHaveCount(0); // selection cleared with the content
    await expect(selectedCells(page)).toHaveCount(0);
    await expect(gridScroller(page)).toHaveAttribute('tabindex', '0');

    await page.getByTestId('switch-content').click(); // B → A
    await expect(page.getByTestId('content-key-label')).toHaveText('A');
    await expect.poll(() => scrollTopOf(page)).toBe(128);
    await expect(activeCell(page)).toHaveAttribute('data-row', '5');
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');
  });

  test('removing the active row drops activation to the nearest row, same column', async ({
    page,
  }) => {
    await setScrollTop(page, 256); // bring rows ~8..22 into view
    await cell(page, 22, 0).click();
    expect(await cellText(page, 22, 0)).toBe('Row 22');

    await page.getByTestId('remove-rows').click(); // removes rows 20..29 by id

    await expect(activeCell(page)).toHaveAttribute('data-row', '22');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
    // View row 22 now holds the nearest surviving row (id 32).
    expect(await cellText(page, 22, 0)).toBe('Row 32');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '41');
  });

  test('an in-place refresh keeps the selection through the identity remap', async ({ page }) => {
    await cell(page, 5, 0).click();
    await cell(page, 8, 1).click({ modifiers: ['Shift'] }); // 4×2 range
    await expect(selectedCells(page)).toHaveCount(8);
    expect(await cellText(page, 5, 1)).toBe('105');

    await page.getByTestId('refresh-rows').click(); // same ids, new objects, new values

    await expect(cell(page, 5, 1)).toHaveText('1,105'); // the refresh visibly landed
    await expect(selectedCells(page)).toHaveCount(8); // selection survived by rowId
    await expect(cell(page, 5, 0)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 8, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(activeCell(page)).toHaveAttribute('data-row', '5');
  });

  test('a shuffle moves the selection with the rows, not the positions', async ({ page }) => {
    await rowHeader(page, 3).click(); // select the full row of id 3
    await expect(rowHeader(page, 3)).toHaveClass(/tm-grid__rowhdr--hit/);

    await page.getByTestId('shuffle-rows').click();

    const target = shuffledIds(50).indexOf(3); // where id 3 landed
    expect(target).toBeGreaterThanOrEqual(0);
    // The active row is always rendered, wherever the shuffle put it.
    await expect(activeCell(page)).toHaveAttribute('data-row', String(target));
    expect(await cellText(page, target, 0)).toBe('Row 3');
    await expect(rowHeader(page, target)).toHaveClass(/tm-grid__rowhdr--hit/);
    await expect(cell(page, target, 2)).toHaveAttribute('aria-selected', 'true');
  });

  test('column widths survive remount AND content switches (keyed by gridId)', async ({
    page,
  }) => {
    const header = colHeader(page, 0); // Name, fixed width 140
    const before = (await header.boundingBox())!;

    const grip = await centerOf(header.locator('.tm-grid__resize'));
    await page.mouse.move(grip.x, grip.y);
    await page.mouse.down();
    await page.mouse.move(grip.x + 40, grip.y, { steps: 6 });
    await page.mouse.up();
    const resized = (await header.boundingBox())!;
    expect(Math.abs(resized.width - (before.width + 40))).toBeLessThanOrEqual(2);

    await page.getByTestId('toggle-mount').click();
    await expect(page.locator('[role="grid"]')).toHaveCount(0);
    await page.getByTestId('toggle-mount').click();
    await expect(page.locator('[role="grid"]')).toBeVisible();
    expect(Math.abs((await header.boundingBox())!.width - resized.width)).toBeLessThanOrEqual(2);

    await page.getByTestId('switch-content').click();
    await expect(page.getByTestId('content-key-label')).toHaveText('B');
    expect(Math.abs((await header.boundingBox())!.width - resized.width)).toBeLessThanOrEqual(2);
  });
});
