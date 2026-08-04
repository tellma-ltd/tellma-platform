// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import {
  activateCell,
  activeCell,
  cell,
  cellText,
  findBar,
  findCounter,
  findInput,
  gotoGrid,
  gridScroller,
  liveRegion,
  scrollTopOf,
} from '../support/grid';

/**
 * Find in grid (spec 0004 §8.7, DoD 15) against the list-screen story
 * (1,000 rows × 8 columns, `searchable`): Mod+F open/focus and its
 * outside-the-grid inertness, the debounced chunked scan feeding the
 * 'i of N' counter, window highlights, Enter/Shift+Enter/button navigation
 * activating (and revealing) matches while focus stays in the input, the
 * Esc close-and-restore contract, case-insensitive matching over display
 * text (localized numbers included), and the tree deep-search that
 * auto-expands ancestors of a hidden match.
 *
 * Query maths on the seeded data (`makeRow`): every name cell is
 * 'Item {i} {Region}', so 'Item 99' matches rows 99 and 990–999 (11
 * matches in scan order: 99 first) and 'Item 999 ' (trailing space)
 * matches row 999 alone.
 */

test.describe('opening and closing', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-list-screen');
  });

  test('Mod+F while the grid has focus opens the bar and focuses its input', async ({ page }) => {
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');

    await expect(findBar(page)).toBeVisible();
    await expect(findInput(page)).toBeFocused();
  });

  test('Mod+F is not shadowed while focus is outside the grid', async ({ page }) => {
    // Headless engines have no native find UI, so the only observable is
    // the grid's own bar — which must NOT open for a page-level Mod+F.
    await page.getByTestId('row-count').focus();
    await page.keyboard.press('Control+f');

    await expect(findBar(page)).toHaveCount(0);
  });

  test('a non-searchable grid ignores Mod+F', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');

    await expect(findBar(page)).toHaveCount(0);
  });

  test('Esc clears the query, closes the bar, and focuses the current match cell', async ({
    page,
  }) => {
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    await findInput(page).fill('Item 999 '); // exactly row 999's name cell
    await expect(findCounter(page)).toHaveText('1 of 1');
    await page.keyboard.press('Enter'); // navigate to the match (activates it)
    await expect(activeCell(page)).toHaveAttribute('data-row', '999');

    await page.keyboard.press('Escape');
    await expect(findBar(page)).toHaveCount(0);
    await expect(cell(page, 999, 1)).toBeFocused(); // focus returned AT the match

    // Reopening starts clean: the query did not survive the close.
    await page.keyboard.press('Control+f');
    await expect(findInput(page)).toHaveValue('');
    await expect(findCounter(page)).toHaveText('');
  });
});

test.describe('scanning and highlighting', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-list-screen');
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
  });

  test("typing shows the 'i of N' counter and highlights window matches", async ({ page }) => {
    await findInput(page).fill('Item 99');

    // Debounce (250ms) + sliced scan land within the expect poll.
    await expect(findCounter(page)).toHaveText('1 of 11');
    // The nearest match (row 99) scrolled into view and is highlighted.
    await expect(cell(page, 99, 1)).toHaveClass(/tm-grid__cell--find/);
    // The counter is announced through the live region.
    await expect(liveRegion(page)).toContainText('1 of 11');
  });

  test('matching is case-insensitive', async ({ page }) => {
    await findInput(page).fill('item 7 '); // lowercase vs. the rendered 'Item 7 …'

    await expect(findCounter(page)).toHaveText('1 of 1');
    await expect(cell(page, 7, 1)).toHaveClass(/tm-grid__cell--find/);
  });

  test('the formatted number text of a price cell is findable', async ({ page }) => {
    // What you can see and copy is what find searches: query the price
    // column's DISPLAY text (Intl-formatted decimals), not the raw value.
    const price = await cellText(page, 2, 3);
    expect(price).toMatch(/\d/);
    await findInput(page).fill(price);

    await expect(findCounter(page)).toHaveText(/^\d+ of \d+$/);
    await expect(cell(page, 2, 3)).toHaveClass(/tm-grid__cell--find/);
  });
});

test.describe('match navigation', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-list-screen');
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
  });

  test('Enter cycles forward activating each match; Shift+Enter cycles back and wraps', async ({
    page,
  }) => {
    await findInput(page).fill('Item 99');
    await expect(findCounter(page)).toHaveText('1 of 11');

    await page.keyboard.press('Enter'); // → match 2, the far row 990
    await expect(findCounter(page)).toHaveText('2 of 11');
    await expect(activeCell(page)).toHaveAttribute('data-row', '990');
    await expect(cell(page, 990, 1)).toHaveClass(/tm-grid__cell--find-active/);
    await expect(cell(page, 990, 1)).toBeVisible(); // revealed under virtualization
    await expect(findInput(page)).toBeFocused(); // focus never left the input

    await page.keyboard.press('Enter');
    await expect(findCounter(page)).toHaveText('3 of 11');
    await expect(activeCell(page)).toHaveAttribute('data-row', '991');

    await page.keyboard.press('Shift+Enter');
    await expect(findCounter(page)).toHaveText('2 of 11');
    await page.keyboard.press('Shift+Enter');
    await expect(findCounter(page)).toHaveText('1 of 11');
    await expect(activeCell(page)).toHaveAttribute('data-row', '99');

    await page.keyboard.press('Shift+Enter'); // wraps to the last match
    await expect(findCounter(page)).toHaveText('11 of 11');
    await expect(activeCell(page)).toHaveAttribute('data-row', '999');
  });

  test('a far match is scrolled into view when navigated to', async ({ page }) => {
    expect(await scrollTopOf(page)).toBe(0);
    await findInput(page).fill('Item 999 '); // row 999 — far outside the window
    await expect(findCounter(page)).toHaveText('1 of 1');

    await page.keyboard.press('Enter');
    await expect(activeCell(page)).toHaveAttribute('data-row', '999');
    await expect(cell(page, 999, 1)).toBeVisible();
    expect(await scrollTopOf(page)).toBeGreaterThan(0);
    await expect(findInput(page)).toBeFocused();
  });

  test('the previous/next buttons step the matches like the keys', async ({ page }) => {
    await findInput(page).fill('Item 99');
    await expect(findCounter(page)).toHaveText('1 of 11');

    await page.locator('[data-tm-find-next]').click();
    await expect(findCounter(page)).toHaveText('2 of 11');
    await expect(activeCell(page)).toHaveAttribute('data-row', '990');

    await page.locator('[data-tm-find-next]').click();
    await expect(findCounter(page)).toHaveText('3 of 11');

    await page.locator('[data-tm-find-prev]').click();
    await expect(findCounter(page)).toHaveText('2 of 11');
    await expect(activeCell(page)).toHaveAttribute('data-row', '990');
  });
});

test.describe('tree deep-search (§8.7)', () => {
  test('navigating to a match hidden in a collapsed subtree auto-expands its ancestors', async ({
    page,
  }) => {
    await gotoGrid(page, 'tree-grid');
    await page.getByTestId('depth-select').selectOption('0'); // roots only
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '10');

    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    // 'Petty cash' sits three levels deep under the collapsed Assets root —
    // the scan spans the whole model, hidden rows included.
    await findInput(page).fill('Petty cash');
    await expect(findCounter(page)).toHaveText('1 of 1');
    await expect(liveRegion(page)).toContainText('1 of 1');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '10'); // scan ≠ expand

    await page.keyboard.press('Enter'); // navigation expands Assets → Current assets → Cash…
    await expect(activeCell(page)).toHaveAttribute('data-row', '3');
    expect(await cellText(page, 3, 0)).toBe('Petty cash');
    await expect(cell(page, 3, 0)).toHaveClass(/tm-grid__cell--find-active/);
    const rowCount = Number(await gridScroller(page).getAttribute('aria-rowcount'));
    expect(rowCount).toBeGreaterThan(10); // the ancestor chain is open now
    await expect(findInput(page)).toBeFocused();
  });

  test('a grouped localized number in the tree is findable by its display text', async ({
    page,
  }) => {
    await gotoGrid(page, 'tree-grid'); // fully expanded by default
    // Petty cash (row 3): balance 8900 renders grouped in the en locale.
    expect(await cellText(page, 3, 2)).toBe('8,900');

    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    await findInput(page).fill('8,900');

    await expect(findCounter(page)).toHaveText(/^\d+ of \d+$/);
    await expect(cell(page, 3, 2)).toHaveClass(/tm-grid__cell--find/);
  });
});
