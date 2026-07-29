// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { activateCell, cell, cellText, gotoGrid, gridScroller } from '../support/grid';
import { syntheticPaste } from '../support/clipboard';

/**
 * Browser battery for the grid's built-in `date` columns (DoD 9): built-in
 * display + parse defaults, tm-date-picker as the cell editor
 * (cell-anchored popup via Alt+ArrowDown), the two-stage Esc, paste, and
 * invalid input. The editable story's Due column is the LAST data column
 * (appended after Total so the long-standing suites' column indices stay
 * put).
 */

const DUE_COL = 8;

test.describe('date column defaults (DoD 9)', () => {
  test('cells display via the built-in locale format', async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
    // Row 0's seeded dueDate is deterministic; assert the en-US shape.
    const text = await cellText(page, 0, DUE_COL);
    expect(text).toMatch(/^\d{1,2}\/\d{1,2}\/2026$/);
  });

  test('type-to-edit seeds tm-date-picker; Enter commits ISO into the model', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, DUE_COL);
    await page.keyboard.type('3/12/2026');
    const input = page.locator('.tm-date-picker__input');
    await expect(input).toBeFocused();
    await expect(input).toHaveValue('3/12/2026');
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('model-json')).toContainText('"dueDate":"2026-03-12"');
    await expect(cell(page, 0, DUE_COL)).toContainText('3/12/2026');
  });

  // Row 0's seeded dueDate is 2026-06-26, so the calendar opens on June
  // 2026 and day 12 of it is 2026-06-12. Picking IS the edit: it commits
  // and closes without an Enter, exactly like activating an enum option.
  // The popup renders in the top layer but lives in the cell's own DOM, so
  // both paths also prove the grid leaves events inside it alone — a
  // pointerdown there is not a click-away commit, and Enter there is not
  // the grid's commit-and-move.
  test('clicking a day in the popup commits it and closes the editor', async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, DUE_COL);
    await page.keyboard.press('F2');
    await page.keyboard.press('Alt+ArrowDown');
    const popup = page.locator('.tm-date-popup');
    await expect(popup).toBeVisible();

    await popup.locator('[data-tm-day="12"]').click();

    await expect(popup).toBeHidden();
    await expect(page.locator('.tm-date-picker__input')).toHaveCount(0);
    await expect(page.getByTestId('model-json')).toContainText('"dueDate":"2026-06-12"');
    await expect(cell(page, 0, DUE_COL)).toContainText('6/12/2026');
    await expect(cell(page, 0, DUE_COL)).toBeFocused(); // no move (Sheets)
  });

  test('Enter on a day in the popup commits that day, not a move', async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, DUE_COL);
    await page.keyboard.press('F2');
    await page.keyboard.press('Alt+ArrowDown');
    const popup = page.locator('.tm-date-popup');
    await expect(popup).toBeVisible();

    // Focus reaches the calendar a render after it paints; a key pressed
    // inside that beat lands in the input behind it. Wait for the day cell
    // the popup opens on — and for the one the arrow moves to — before
    // pressing on.
    await expect(popup.locator('[data-tm-day="26"]')).toBeFocused();
    await page.keyboard.press('ArrowLeft'); // the 26th → the 25th
    await expect(popup.locator('[data-tm-day="25"]')).toBeFocused();
    await page.keyboard.press('Enter');

    await expect(page.getByTestId('model-json')).toContainText('"dueDate":"2026-06-25"');
    await expect(cell(page, 0, DUE_COL)).toContainText('6/25/2026');
    await expect(cell(page, 0, DUE_COL)).toBeFocused();
  });

  test('one Alt+ArrowDown reaches the calendar, like an enum cell reaches its panel', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, DUE_COL);
    await page.keyboard.press('Alt+ArrowDown'); // ONE press: editor + calendar

    await expect(page.locator('.tm-date-picker__input')).toBeVisible();
    await expect(page.locator('.tm-date-popup')).toBeVisible();

    // F2 is the other half of the contract: it edits without popping the
    // calendar, because typing is a date cell's primary path.
    await page.keyboard.press('Escape'); // popup
    await page.keyboard.press('Escape'); // session
    await expect(page.locator('.tm-date-picker__input')).toHaveCount(0);
    await page.keyboard.press('F2');
    await expect(page.locator('.tm-date-picker__input')).toBeVisible();
    await expect(page.locator('.tm-date-popup')).toHaveCount(0);
  });

  test('arrowing onto the last column clears the scrollbar gutter', async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, 0);
    for (let col = 0; col < DUE_COL; col++) {
      await page.keyboard.press('ArrowRight');
    }
    await expect(cell(page, 0, DUE_COL)).toBeFocused();

    // The scroller's border box counts the vertical scrollbar's gutter as
    // visible space; the cell has to clear the CLIENT box.
    const viewport = await gridScroller(page).evaluate((element: HTMLElement) => {
      const rect = element.getBoundingClientRect();
      const gutter = element.offsetWidth - element.clientWidth;
      const rtl = getComputedStyle(element).direction === 'rtl';
      return { left: rect.left + (rtl ? gutter : 0), right: rect.right - (rtl ? 0 : gutter) };
    });
    const box = (await cell(page, 0, DUE_COL).boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(viewport.left - 1);
    expect(box.x + box.width).toBeLessThanOrEqual(viewport.right + 1);
  });

  test('Alt+ArrowDown opens the popup anchored to the cell; two-stage Esc composes', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, DUE_COL);
    await page.keyboard.press('F2');
    const input = page.locator('.tm-date-picker__input');
    await expect(input).toBeFocused();
    await page.keyboard.press('Alt+ArrowDown');

    const popup = page.locator('.tm-date-popup');
    await expect(popup).toBeVisible();
    // Anchored to the cell, on the edge the DATE sits against: a
    // right-aligned column opens a right-aligned calendar, so the popup
    // appears under the text it edits rather than off the far side.
    const cellBox = (await cell(page, 0, DUE_COL).boundingBox())!;
    const popupBox = (await popup.boundingBox())!;
    const alignedToEnd = await cell(page, 0, DUE_COL).evaluate((el) => {
      const style = getComputedStyle(el);
      const end = style.direction === 'rtl' ? 'left' : 'right';
      return style.textAlign === 'end' || style.textAlign === end;
    });
    const delta = alignedToEnd
      ? popupBox.x + popupBox.width - (cellBox.x + cellBox.width)
      : popupBox.x - cellBox.x;
    expect(Math.abs(delta)).toBeLessThan(2);
    // Vertically it hangs off the cell, not somewhere else on the page.
    expect(Math.abs(popupBox.y - (cellBox.y + cellBox.height))).toBeLessThan(2);

    // Esc №1 closes the popup only; the editor stays.
    await page.keyboard.press('Escape');
    await expect(popup).toBeHidden();
    await expect(input).toBeVisible();

    // Esc №2 cancels the session without writing; focus returns to the
    // active cell (roving tabindex).
    const before = await page.getByTestId('model-json').textContent();
    await page.keyboard.press('Escape');
    await expect(input).toBeHidden();
    await expect(page.getByTestId('model-json')).toHaveText(before!);
    await expect(cell(page, 0, DUE_COL)).toBeFocused();
  });

  test('paste parses dates through the built-in parse; junk becomes an invalid input', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, DUE_COL);
    await syntheticPaste(page, { text: '4/1/2026\njunk' });

    await expect(page.getByTestId('model-json')).toContainText('"dueDate":"2026-04-01"');
    await expect(cell(page, 1, DUE_COL)).toContainText('junk');
    await expect(cell(page, 1, DUE_COL)).toHaveClass(/tm-grid__cell--error/);
  });
});
