// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import {
  activateCell,
  activeCell,
  cell,
  checkAllBox,
  checkCell,
  colHeader,
  editor,
  findCounter,
  findInput,
  gotoGrid,
  gridScroller,
  liveRegion,
  rowCheckbox,
  rowHeader,
  scrollTopOf,
  syntheticCopy,
} from '../support/grid';

/**
 * Row checkbox selection (spec 0004 §8.8, DoD 19) against the list-screen
 * story: the chrome column between row header and data, click/Shift+click
 * (Gmail range semantics), the tri-state select-all header with its click
 * and Ctrl+Shift+Space paths, Space on the active row, the coordinate-space
 * exclusions (arrows, ranges/copy, find), checked-vs-range styling
 * independence, count announcements, and `selectedIds` identity across
 * scrolling and row-count switches.
 *
 * Story shape: 8 data columns (0 code … 7 note), 1,000 rows by default,
 * `selectable` + `searchable`, `data-testid="selected-count"` mirroring
 * `selectedIds().size`.
 */

/** The rendered row element at a view-space row index. */
function row(page: Page, viewIndex: number): Locator {
  return page.locator(`.tm-grid__row[aria-rowindex="${viewIndex + 2}"]`);
}

/** The toolbar's `selectedIds` readout ('N selected'). */
function selectedCount(page: Page): Locator {
  return page.getByTestId('selected-count');
}

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-list-screen');
});

test.describe('the chrome column', () => {
  test('renders between the row header and the data columns with shifted ARIA indices', async ({
    page,
  }) => {
    // 8 data columns + row header + checkbox column.
    await expect(gridScroller(page)).toHaveAttribute('aria-colcount', '10');
    await expect(page.locator('[data-tm-corner]')).toHaveAttribute('aria-colindex', '1');
    await expect(page.locator('[data-tm-checkhdr]')).toHaveAttribute('aria-colindex', '2');
    await expect(colHeader(page, 0)).toHaveAttribute('aria-colindex', '3');
    await expect(cell(page, 0, 0)).toHaveAttribute('aria-colindex', '3');
    await expect(checkCell(page, 0)).toHaveAttribute('role', 'gridcell');

    // Physically between the two: corner | checkbox header | first data header.
    const corner = (await page.locator('[data-tm-corner]').boundingBox())!;
    const checkHdr = (await page.locator('[data-tm-checkhdr]').boundingBox())!;
    const firstCol = (await colHeader(page, 0).boundingBox())!;
    expect(checkHdr.x).toBeGreaterThanOrEqual(corner.x + corner.width - 1);
    expect(firstCol.x).toBeGreaterThanOrEqual(checkHdr.x + checkHdr.width - 1);
  });
});

test.describe('row checkbox toggling', () => {
  test('a click checks the row (tint + row aria-selected + count) and a second click unchecks', async ({
    page,
  }) => {
    await rowCheckbox(page, 2).click();

    await expect(rowCheckbox(page, 2)).toHaveAttribute('aria-checked', 'true');
    await expect(rowCheckbox(page, 2)).toHaveClass(/tm-grid__check--on/);
    await expect(row(page, 2)).toHaveClass(/tm-grid__row--checked/);
    await expect(row(page, 2)).toHaveAttribute('aria-selected', 'true');
    await expect(selectedCount(page)).toHaveText('1 selected');

    await rowCheckbox(page, 2).click();
    await expect(rowCheckbox(page, 2)).toHaveAttribute('aria-checked', 'false');
    await expect(row(page, 2)).not.toHaveClass(/tm-grid__row--checked/);
    await expect(row(page, 2)).not.toHaveAttribute('aria-selected', /.*/);
    await expect(selectedCount(page)).toHaveText('0 selected');
  });

  test('Shift+click applies the anchor state to the whole range (Gmail model)', async ({
    page,
  }) => {
    // Checked anchor: the range checks.
    await rowCheckbox(page, 2).click();
    await rowCheckbox(page, 7).click({ modifiers: ['Shift'] });

    await expect(selectedCount(page)).toHaveText('6 selected');
    for (const index of [2, 3, 4, 5, 6, 7]) {
      await expect(rowCheckbox(page, index)).toHaveAttribute('aria-checked', 'true');
    }
    await expect(rowCheckbox(page, 1)).toHaveAttribute('aria-checked', 'false');
    await expect(rowCheckbox(page, 8)).toHaveAttribute('aria-checked', 'false');

    // UNchecked anchor: the same gesture unchecks the range instead.
    await rowCheckbox(page, 4).click(); // uncheck row 4 → the new anchor, state off
    await expect(selectedCount(page)).toHaveText('5 selected');
    await rowCheckbox(page, 7).click({ modifiers: ['Shift'] });

    await expect(selectedCount(page)).toHaveText('2 selected'); // rows 2 and 3 survive
    for (const index of [4, 5, 6, 7]) {
      await expect(rowCheckbox(page, index)).toHaveAttribute('aria-checked', 'false');
    }
    await expect(rowCheckbox(page, 2)).toHaveAttribute('aria-checked', 'true');
    await expect(rowCheckbox(page, 3)).toHaveAttribute('aria-checked', 'true');
  });
});

test.describe('the tri-state select-all header', () => {
  test('none → mixed → all → none through row clicks and header clicks', async ({ page }) => {
    await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'false');

    await rowCheckbox(page, 1).click(); // some
    await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'mixed');
    await expect(checkAllBox(page)).toHaveClass(/tm-grid__check--mixed/);

    await checkAllBox(page).click(); // mixed → all
    await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'true');
    await expect(checkAllBox(page)).toHaveClass(/tm-grid__check--on/);
    await expect(selectedCount(page)).toHaveText('1000 selected');
    await expect(rowCheckbox(page, 0)).toHaveAttribute('aria-checked', 'true');
    await expect(rowCheckbox(page, 5)).toHaveAttribute('aria-checked', 'true');

    await checkAllBox(page).click(); // all → none
    await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'false');
    await expect(selectedCount(page)).toHaveText('0 selected');
    await expect(rowCheckbox(page, 5)).toHaveAttribute('aria-checked', 'false');
  });

  test('Ctrl+Shift+Space toggles select-all from the keyboard', async ({ page }) => {
    await activateCell(page, 0, 0);

    await page.keyboard.press('Control+Shift+Space');
    await expect(selectedCount(page)).toHaveText('1000 selected');
    await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'true');

    await page.keyboard.press('Control+Shift+Space');
    await expect(selectedCount(page)).toHaveText('0 selected');
    await expect(checkAllBox(page)).toHaveAttribute('aria-checked', 'false');
  });
});

test.describe('keyboard on the active row', () => {
  test('Space toggles the active row checkbox without opening an editor or scrolling', async ({
    page,
  }) => {
    await activateCell(page, 3, 1);
    const scrollBefore = await scrollTopOf(page);
    const pageScrollBefore = await page.evaluate(() => window.scrollY);

    await page.keyboard.press('Space');
    await expect(rowCheckbox(page, 3)).toHaveAttribute('aria-checked', 'true');
    await expect(selectedCount(page)).toHaveText('1 selected');

    await page.keyboard.press('Space');
    await expect(rowCheckbox(page, 3)).toHaveAttribute('aria-checked', 'false');
    await expect(selectedCount(page)).toHaveText('0 selected');

    await expect(editor(page)).toHaveCount(0); // readonly: Space never edits
    expect(await scrollTopOf(page)).toBe(scrollBefore); // the grid did not scroll…
    expect(await page.evaluate(() => window.scrollY)).toBe(pageScrollBefore); // …nor the page
  });
});

test.describe('outside the cell coordinate space', () => {
  test('arrows never land on the checkbox column', async ({ page }) => {
    await activateCell(page, 2, 0);

    await page.keyboard.press('ArrowLeft'); // no column -1: the chrome is not navigable
    await expect(activeCell(page)).toHaveAttribute('data-row', '2');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
    await expect(cell(page, 2, 0)).toBeFocused();
  });

  test('ranges never include it: a full-row copy exports exactly the data columns', async ({
    page,
  }) => {
    await rowHeader(page, 2).click(); // the full-row range
    const { text } = await syntheticCopy(page);

    const fields = text.replace(/\r?\n$/, '').split('\t');
    expect(fields).toHaveLength(8); // 8 data columns, no checkbox artifact
    expect(fields[0]).toBe('PRD-2');
  });

  test('find never matches checkbox cells: a query hitting every row counts data cells only', async ({
    page,
  }) => {
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    await findInput(page).fill('Item'); // every row's name cell — and nothing else

    // 1,000 rows → exactly 1,000 matches; the checkbox column contributed none.
    await expect(findCounter(page)).toHaveText(/^1 of 1,?000$/);
    await expect(page.locator('[data-tm-checkcell].tm-grid__cell--find')).toHaveCount(0);
  });
});

test.describe('checked rows vs. range selection', () => {
  test('the row tint and the range fill are independent and coexist on the same row', async ({
    page,
  }) => {
    await rowCheckbox(page, 3).click();
    await expect(row(page, 3)).toHaveClass(/tm-grid__row--checked/);

    await activateCell(page, 2, 1);
    await page.keyboard.press('Shift+ArrowDown'); // range rows 2–3 × col 1 over the checked row

    await expect(row(page, 3)).toHaveClass(/tm-grid__row--checked/); // tint survives
    await expect(row(page, 3)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 3, 1)).toHaveClass(/tm-grid__cell--selected/); // range fill on top
    await expect(cell(page, 3, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 3, 2)).not.toHaveAttribute('aria-selected', /.*/); // cell-scoped
    await expect(selectedCount(page)).toHaveText('1 selected'); // ranges never write selectedIds
  });
});

test.describe('announcements', () => {
  test("changes announce 'N of M selected' through the live region", async ({ page }) => {
    await rowCheckbox(page, 2).click();
    await expect(liveRegion(page)).toContainText(/1 of 1,?000 selected/);

    // A Shift+click burst speaks once, with the settled count.
    await rowCheckbox(page, 7).click({ modifiers: ['Shift'] });
    await expect(liveRegion(page)).toContainText(/6 of 1,?000 selected/);
  });
});

test.describe('selection durability', () => {
  test('bulk selection survives scrolling to the far end and back', async ({ page }) => {
    await activateCell(page, 0, 0);
    await rowCheckbox(page, 2).click();
    await expect(selectedCount(page)).toHaveText('1 selected');

    await page.keyboard.press('Control+End'); // row 999 — row 2 leaves the DOM
    await expect(activeCell(page)).toHaveAttribute('data-row', '999');
    await expect(checkCell(page, 2)).toHaveCount(0);

    await page.keyboard.press('Control+Home');
    await expect(rowCheckbox(page, 2)).toHaveAttribute('aria-checked', 'true');
    await expect(row(page, 2)).toHaveClass(/tm-grid__row--checked/);
    await expect(selectedCount(page)).toHaveText('1 selected');
  });

  test('switching 1k → 100k keeps selectedIds by row identity', async ({ page }) => {
    await rowCheckbox(page, 5).click();
    await expect(selectedCount(page)).toHaveText('1 selected');

    await page.getByTestId('row-count').selectOption('100000');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '100001');

    // Row id 5 is the same row in the larger dataset — still checked.
    await expect(rowCheckbox(page, 5)).toHaveAttribute('aria-checked', 'true');
    await expect(row(page, 5)).toHaveClass(/tm-grid__row--checked/);
    await expect(selectedCount(page)).toHaveText('1 selected');
  });
});
