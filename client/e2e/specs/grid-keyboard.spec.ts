// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import {
  activeCell,
  cell,
  cellText,
  clientHeightOf,
  gotoGrid,
  gridScroller,
  rowHeader,
  rowHeightOf,
  selectedCells,
} from '../support/grid';

/**
 * The readonly keyboard matrix (spec 0004 §8.2) against the 100k-row story:
 * arrow/jump/page/extreme motion in MODEL space under virtualization,
 * selection extension, Enter semantics, and the tab-stop/escape exits.
 * Tests run on Windows/Linux CI runners, so the platform modifier is
 * literally Control throughout.
 */

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-readonly');
});

test.describe('entering the grid', () => {
  test('the empty grid container is the tab stop; an arrow enters at cell 0,0', async ({
    page,
  }) => {
    await page.getByTestId('row-count').focus();
    await page.keyboard.press('Tab');
    await expect(gridScroller(page)).toBeFocused();

    await page.keyboard.press('ArrowDown'); // first activation lands at the origin
    await expect(cell(page, 0, 0)).toBeFocused();
    await expect(gridScroller(page)).toHaveAttribute('tabindex', '-1');
  });
});

test.describe('arrow motion', () => {
  test('arrows move the active cell and collapse the selection to it', async ({ page }) => {
    await cell(page, 2, 2).click();
    await cell(page, 4, 4).click({ modifiers: ['Shift'] }); // 3×3 range
    await expect(selectedCells(page)).toHaveCount(9);

    await page.keyboard.press('ArrowDown');
    await expect(cell(page, 3, 2)).toBeFocused();
    await expect(selectedCells(page)).toHaveCount(1);
    await expect(cell(page, 3, 2)).toHaveAttribute('aria-selected', 'true');

    await page.keyboard.press('ArrowRight');
    await expect(cell(page, 3, 3)).toBeFocused();
    await page.keyboard.press('ArrowUp');
    await expect(cell(page, 2, 3)).toBeFocused();
    await page.keyboard.press('ArrowLeft');
    await expect(cell(page, 2, 2)).toBeFocused();
  });

  test('Control+Arrow jumps to the data edge (model-space, all cells filled)', async ({
    page,
  }) => {
    await cell(page, 5, 1).click();

    await page.keyboard.press('Control+ArrowRight');
    await expect(activeCell(page)).toHaveAttribute('data-col', '11');
    await page.keyboard.press('Control+ArrowLeft');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');

    await page.keyboard.press('Control+ArrowDown'); // every cell holds data → the far edge
    await expect(activeCell(page)).toHaveAttribute('data-row', '99999');
    await page.keyboard.press('Control+ArrowUp');
    await expect(activeCell(page)).toHaveAttribute('data-row', '0');
  });
});

test.describe('paging and extremes', () => {
  test('PageDown/PageUp move by one viewport page', async ({ page }) => {
    const rowHeight = await rowHeightOf(page);
    const clientHeight = await clientHeightOf(page);
    const pageSize = Math.max(1, Math.floor((clientHeight - rowHeight) / rowHeight));

    await cell(page, 0, 1).click();
    await page.keyboard.press('PageDown');
    await expect(activeCell(page)).toHaveAttribute('data-row', String(pageSize));
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');

    await page.keyboard.press('PageUp');
    await expect(activeCell(page)).toHaveAttribute('data-row', '0');
  });

  test('Home/End go to the row start/end; Control+Home/End to the grid corners', async ({
    page,
  }) => {
    await cell(page, 3, 4).click();

    await page.keyboard.press('End');
    await expect(activeCell(page)).toHaveAttribute('data-row', '3');
    await expect(activeCell(page)).toHaveAttribute('data-col', '11');

    await page.keyboard.press('Home');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');

    // Control+End proves model-space navigation under virtualization: row
    // 99999 was never in the DOM until now, and must be rendered + focused.
    await page.keyboard.press('Control+End');
    await expect(activeCell(page)).toHaveAttribute('data-row', '99999');
    await expect(activeCell(page)).toHaveAttribute('data-col', '11');
    await expect(cell(page, 99999, 11)).toBeFocused();
    await expect(page.locator('.tm-grid__row [data-tm-rowhdr][data-row="99999"]')).toBeVisible();

    await page.keyboard.press('Control+Home');
    await expect(cell(page, 0, 0)).toBeFocused();
  });
});

test.describe('selection extension', () => {
  test('Shift+Arrow grows the active range from its anchor', async ({ page }) => {
    await cell(page, 3, 3).click();
    await expect(selectedCells(page)).toHaveCount(1);

    await page.keyboard.press('Shift+ArrowDown');
    await page.keyboard.press('Shift+ArrowDown');
    await expect(selectedCells(page)).toHaveCount(3); // 3 rows × 1 col

    await page.keyboard.press('Shift+ArrowRight');
    await expect(selectedCells(page)).toHaveCount(6); // 3 rows × 2 cols
    // The active cell stays put at the anchor while the focus edge extends.
    await expect(activeCell(page)).toHaveAttribute('data-row', '3');
    await expect(activeCell(page)).toHaveAttribute('data-col', '3');
  });

  test('Shift+PageDown extends by a viewport page', async ({ page }) => {
    const rowHeight = await rowHeightOf(page);
    const clientHeight = await clientHeightOf(page);
    const pageSize = Math.max(1, Math.floor((clientHeight - rowHeight) / rowHeight));

    await cell(page, 0, 2).click();
    await page.keyboard.press('Shift+PageDown');

    await expect(cell(page, 0, 2)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, pageSize, 2)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 0, 3)).not.toHaveAttribute('aria-selected');
  });

  test('Shift+Space selects the full row; Ctrl+Space the full column', async ({ page }) => {
    await cell(page, 4, 5).click();

    await page.keyboard.press('Shift+Space');
    await expect(rowHeader(page, 4)).toHaveClass(/tm-grid__rowhdr--hit/);
    await expect(cell(page, 4, 0)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 4, 11)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 3, 5)).not.toHaveAttribute('aria-selected');

    // Column select acts on the ACTIVE RANGE's columns — re-collapse first,
    // or the full-row range above would legitimately select every column.
    await cell(page, 4, 5).click();
    await page.keyboard.press('Control+Space'); // literal Ctrl on every platform (§8.2)
    await expect(page.locator('[data-tm-colhdr][data-col="5"]')).toHaveClass(
      /tm-grid__colhdr--hit/,
    );
    await expect(cell(page, 0, 5)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 8, 5)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 8, 4)).not.toHaveAttribute('aria-selected');
  });

  test('Control+A selects all — distant cells included, checked after scrolling', async ({
    page,
  }) => {
    const rowHeight = await rowHeightOf(page);
    await cell(page, 1, 1).click();
    await page.keyboard.press('Control+a');
    await expect(cell(page, 1, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 0, 11)).toHaveAttribute('aria-selected', 'true');

    // Selection is O(ranges): rows rendered later must paint selected too.
    await page.locator('[role="grid"]').evaluate((el, y) => {
      el.scrollTop = y;
    }, 60_000 * rowHeight);
    await expect(cell(page, 60_000, 6)).toHaveAttribute('aria-selected', 'true');
    await expect(rowHeader(page, 60_000)).toHaveClass(/tm-grid__rowhdr--hit/);
  });
});

test.describe('Enter semantics (§8.2 readonly)', () => {
  test('Enter on a link cell activates the record link', async ({ page }) => {
    await cell(page, 7, 0).click(); // the code column projects a record link
    await page.keyboard.press('Enter');

    // Same-document fragment navigation: the story page stays alive.
    await expect(page).toHaveURL(/#record-7$/);
    await expect(cell(page, 7, 0)).toBeVisible();
    await page.goBack();
  });

  test('Enter on a non-interactive cell moves down; Shift+Enter moves up', async ({ page }) => {
    await cell(page, 5, 1).click();

    await page.keyboard.press('Enter');
    await expect(cell(page, 6, 1)).toBeFocused();

    await page.keyboard.press('Shift+Enter');
    await expect(cell(page, 5, 1)).toBeFocused();
    await page.keyboard.press('Shift+Enter');
    await expect(cell(page, 4, 1)).toBeFocused();
  });
});

test.describe('tab stop and mid-grid exit', () => {

  test('Esc parks focus on the container; Tab then exits the grid', async ({ page }) => {
    await gotoGrid(page, 'grid-states');
    await cell(page, 5, 2).click();
    await expect(cell(page, 5, 2)).toBeFocused();

    await page.keyboard.press('Escape');
    await expect(gridScroller(page)).toBeFocused(); // the mid-grid exit ramp
    await expect(gridScroller(page)).toHaveAttribute('tabindex', '0');
    await expect(activeCell(page)).toHaveCount(0); // cells left the tab order

    await page.keyboard.press('Tab');
    const focusInsideGrid = await page.evaluate(() => {
      const grid = document.querySelector('[role="grid"]');
      return grid !== null && grid.contains(document.activeElement);
    });
    expect(focusInsideGrid).toBe(false);

    // Any arrow re-enters at the active cell (§8.2 Esc row).
    await gridScroller(page).focus();
    await page.keyboard.press('ArrowDown');
    await expect(cell(page, 6, 2)).toBeFocused();
  });

  test('a readonly grid is a single tab stop: Tab from a focused cell leaves it', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-states');
    await cell(page, 3, 1).click();
    await expect(cell(page, 3, 1)).toBeFocused();

    await page.keyboard.press('Tab');
    const focusInsideGrid = await page.evaluate(() => {
      const grid = document.querySelector('[role="grid"]');
      return grid !== null && grid.contains(document.activeElement);
    });
    expect(focusInsideGrid).toBe(false);
  });

  test('single tab stop holds with interactive display content (link column)', async ({
    page,
  }) => {
    // Projected display content (the code column's record links) is pulled
    // out of the tab order (tabindex -1) so Tab from a focused cell exits
    // the grid; the links stay reachable via Enter on the cell.
    await gotoGrid(page, 'grid-readonly');
    await cell(page, 3, 1).click();
    await page.keyboard.press('Tab');
    const focusInsideGrid = await page.evaluate(() => {
      const grid = document.querySelector('[role="grid"]');
      return grid !== null && grid.contains(document.activeElement);
    });
    expect(focusInsideGrid).toBe(false);
  });
});

test.describe('display text sanity (what motion asserts against)', () => {
  test('the deterministic dataset embeds the row index in text cells', async ({ page }) => {
    expect(await cellText(page, 0, 0)).toBe('PRD-0');
    expect(await cellText(page, 5, 0)).toBe('PRD-5');
    expect(await cellText(page, 5, 1)).toContain('Item 5');
  });
});
