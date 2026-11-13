// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import {
  activateCell,
  activeCell,
  cell,
  cellPaint,
  editorInput,
  findBar,
  findCounter,
  findInput,
  gotoGrid,
  rowCheckbox,
  statusChip,
} from '../support/grid';
import { appStable } from '../support/settle';

/**
 * The axe static floor over the EDITABLE grid states (spec 0004 §14):
 * populated editable, an open text editor, the enum editor with its panel
 * open, the error state with the active-cell overlay visible, and the
 * selectable list screen with checked rows and the find bar open; and the
 * forced-colors paint of errored and readonly cells inside a range, of the
 * open editor, and of the active cell on a find match. The
 * readonly/loading/empty/tree scans live in grid-a11y.spec.ts and
 * tree-grid.spec.ts.
 */

/** Types unparseable text into a quantity cell and commits it (§10). */
async function makeInvalidQuantity(page: Page, row: number, text: string): Promise<void> {
  await activateCell(page, row, 1);
  await page.keyboard.press(text[0]);
  if (text.length > 1) {
    await page.keyboard.type(text.slice(1));
  }
  await page.keyboard.press('Enter');
  await expect(cell(page, row, 1)).toHaveClass(/tm-grid__cell--error/);
}

test.describe('editable grid', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`the populated editable grid is axe-clean (${theme})`, async ({ page }) => {
      await gotoGrid(page, 'grid-editable', { theme });
      await activateCell(page, 1, 1); // active + selected state in the scan
      await expectNoAxeViolations(page);
    });
  }

  test('an open text editor is axe-clean', async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, 0);
    await page.keyboard.press('F2'); // edit mode, seeded with the display text
    await expect(editorInput(page)).toBeFocused();
    await expectNoAxeViolations(page);
  });

  test('the enum editor with its panel open is axe-clean', async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 0, 5); // the category enum column
    await page.keyboard.press('Enter');
    await expect(page.locator('.tm-select__panel')).toBeVisible();
    await expectNoAxeViolations(page);
  });
});

test.describe('error state', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`field error + invalid input with the overlay visible are axe-clean (${theme})`, async ({
      page,
    }) => {
      await gotoGrid(page, 'grid-editable', { theme });

      // A validator (field) error: clearing a required description.
      await activateCell(page, 3, 0);
      await page.keyboard.press('Delete');
      await expect(cell(page, 3, 0)).toHaveClass(/tm-grid__cell--error/);

      // An invalid input, then activate it so the error overlay renders.
      await makeInvalidQuantity(page, 2, 'abc');
      await page.keyboard.press('ArrowUp'); // back onto the errored cell
      await expect(cell(page, 2, 1)).toHaveAttribute('aria-describedby', /.+/);
      const describedBy = await cell(page, 2, 1).getAttribute('aria-describedby');
      await expect(page.locator(`#${describedBy}`)).toBeVisible();

      await expect(statusChip(page)).toContainText('2 errors');
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('forced colors', () => {
  test('errored and readonly cells inside a range keep the fill, legible ink and their outlines', async ({
    page,
  }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await gotoGrid(page, 'grid-editable');
    await makeInvalidQuantity(page, 2, 'abc'); // (2,1) errored

    // Rows 2-3 × columns 0-7: the errored quantity, the readonly Total
    // column (7), and the active cell at the anchor (3,0).
    await activateCell(page, 3, 0);
    await cell(page, 2, 7).click({ modifiers: ['Shift'] });
    await expect(cell(page, 2, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 2, 1)).toHaveAttribute('aria-invalid', 'true');
    await expect(cell(page, 2, 7)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 2, 7)).toHaveAttribute('aria-readonly', 'true');

    const filled = await cellPaint(page, 3, 2);
    const unselected = await cellPaint(page, 5, 2);
    const errored = await cellPaint(page, 2, 1);
    const readonly = await cellPaint(page, 2, 7);
    const active = await cellPaint(page, 3, 0);

    // One fill across the range, whatever base state a cell has.
    expect(filled.background).not.toBe(unselected.background);
    for (const paint of [errored, readonly, active]) {
      expect(paint.background).toBe(filled.background);
      expect(paint.ink).not.toBe(paint.background);
    }
    // The error keeps its dashed outline, and it shows on the fill.
    expect(errored.outline).toBe('dashed');
    expect(errored.outlineColor).not.toBe(errored.background);
    // The active ring shows on the fill too.
    expect(active.outline).toBe('solid');
    expect(active.outlineColor).not.toBe(active.background);

    // Editing the active cell keeps the range selected; the editor is a
    // field of its own, and its text stays legible over it.
    await page.keyboard.press('F2');
    await expect(editorInput(page)).toBeFocused();
    await expect(cell(page, 3, 0)).toHaveAttribute('aria-selected', 'true');
    await appStable(page);
    const field = await editorInput(page).evaluate((input) => {
      // The paint under the text: the nearest box that paints a background.
      let box: Element | null = input;
      let background = 'rgba(0, 0, 0, 0)';
      while (box !== null) {
        const color = getComputedStyle(box).backgroundColor;
        if (!/^rgba\(.*, 0\)$/.test(color)) {
          background = color;
          break;
        }
        box = box.parentElement;
      }
      return { ink: getComputedStyle(input).color, background };
    });
    expect(field.ink).not.toBe(field.background);
  });

  test('the active cell keeps its solid ring on a find match that is not the active match', async ({
    page,
  }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await gotoGrid(page, 'grid-list-screen');
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    await findInput(page).fill('Item'); // every row matches; one is the active match
    await expect(findCounter(page)).toContainText('of');

    // Click a rendered match other than the active one: activity moves,
    // the find index does not.
    const match = page
      .locator('[data-tm-cell].tm-grid__cell--find:not(.tm-grid__cell--find-active)')
      .nth(2);
    const row = Number(await match.getAttribute('data-row'));
    const col = Number(await match.getAttribute('data-col'));
    await match.click();
    await expect(activeCell(page)).toHaveAttribute('data-row', String(row));
    await expect(cell(page, row, col)).toHaveClass(/tm-grid__cell--find(?!-)/);
    await expect(cell(page, row, col)).not.toHaveClass(/tm-grid__cell--find-active/);

    const active = await cellPaint(page, row, col);
    expect(active.outline).toBe('solid'); // the ring, not the match's dots
    expect(active.outlineColor).not.toBe(active.background);
  });
});

test.describe('selectable list screen', () => {
  test('checked rows plus the open find bar are axe-clean', async ({ page }) => {
    await gotoGrid(page, 'grid-list-screen');

    await rowCheckbox(page, 1).click();
    await rowCheckbox(page, 3).click();
    await expect(page.getByTestId('selected-count')).toHaveText('2 selected');

    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    await expect(findBar(page)).toBeVisible();
    await findInput(page).fill('Item 9'); // highlights + a live match counter
    await expect(findCounter(page)).toContainText('of');

    await expectNoAxeViolations(page);
  });
});
