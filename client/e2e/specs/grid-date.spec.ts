// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { activateCell, cell, cellText, gotoGrid } from '../support/grid';
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
