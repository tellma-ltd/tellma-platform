// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import {
  activateCell,
  activeCell,
  cell,
  cellText,
  gotoGrid,
  liveRegion,
  modelJson,
  readClipboard,
  rowHeightOf,
  scrollTopOf,
  setScrollTop,
  statusChip,
} from '../support/grid';

/**
 * Cell error states and the error tally (spec 0004 §10) against the
 * editable invoice-lines story: field-validation errors, invalid inputs
 * with their in-place raw text, the active-cell overlay message (layout-
 * shift-free), the status-bar tally with row-major cycling navigation, the
 * clearing rules, and raw-text copy fidelity.
 *
 * Resolver-pending states (§9.4 — the spinner chip, abort-on-flip) are
 * exercised by PASTE, which is the next milestone; pending-count e2e is
 * deliberately deferred to it.
 */

interface Line {
  readonly id: number;
  readonly description: string | null;
  readonly quantity: number | null;
}

async function lineAt(page: Page, index: number): Promise<Line> {
  return (await modelJson<Line[]>(page))[index];
}

/** Types unparseable text into a quantity cell and commits it. */
async function makeInvalidQuantity(page: Page, row: number, text: string): Promise<void> {
  await activateCell(page, row, 1);
  await page.keyboard.press(text[0]);
  if (text.length > 1) {
    await page.keyboard.type(text.slice(1));
  }
  await page.keyboard.press('Enter');
  await expect(cell(page, row, 1)).toHaveClass(/tm-grid__cell--error/);
}

/**
 * Resolves the active cell's `aria-describedby` to the overlay message
 * text, or `''` while the cell references none (poll-friendly — a missing
 * overlay fails the caller's content assertion).
 */
async function overlayMessage(page: Page, row: number, col: number): Promise<string> {
  const describedBy = await cell(page, row, col).getAttribute('aria-describedby');
  if (describedBy === null) {
    return '';
  }
  return (await page.locator(`#${describedBy}`).textContent())?.trim() ?? '';
}

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-editable');
});

test.describe('error sources', () => {
  test('clearing a required cell surfaces a field error: tint, aria, and the tally chip', async ({
    page,
  }) => {
    await activateCell(page, 3, 0);
    await page.keyboard.press('Delete');

    await expect.poll(async () => (await lineAt(page, 3)).description).toBeNull();
    await expect(cell(page, 3, 0)).toHaveClass(/tm-grid__cell--error/);
    await expect(cell(page, 3, 0)).toHaveAttribute('aria-invalid', 'true');
    await expect(statusChip(page)).toContainText('1 error');

    // The active errored cell describes itself through the overlay message.
    await expect.poll(() => overlayMessage(page, 3, 0)).toContain('required');
  });

  test('an invalid input shows its distinct message on the active cell without shifting layout', async ({
    page,
  }) => {
    await makeInvalidQuantity(page, 2, 'abc');
    expect(await cellText(page, 2, 1)).toBe('abc'); // the raw text stays visible in place
    await expect.poll(async () => (await lineAt(page, 2)).quantity).toBeNull();

    // No overlay yet: the commit moved the active cell down.
    const before = await page.getByTestId('grid-editable').boundingBox();
    await page.keyboard.press('ArrowUp'); // activate the errored cell → overlay appears

    // The invalid-input string names the raw text — not a field error.
    await expect.poll(() => overlayMessage(page, 2, 1)).toContain('is not a valid Qty');
    expect(await overlayMessage(page, 2, 1)).toContain('abc');

    // The message renders in a top layer: the grid's box is untouched.
    const after = await page.getByTestId('grid-editable').boundingBox();
    expect(after).toEqual(before);
  });
});

test.describe('the tally', () => {
  test('three errors tally, and next/previous cycle row-major, activating and scrolling', async ({
    page,
  }) => {
    const rowHeight = await rowHeightOf(page);

    // Error 1: a required field error at (1,0).
    await activateCell(page, 1, 0);
    await page.keyboard.press('Delete');
    // Error 2: an invalid input at (5,1).
    await makeInvalidQuantity(page, 5, 'x');
    // Error 3: an invalid input at (30,1) — created after scrolling down.
    await setScrollTop(page, 26 * rowHeight);
    await makeInvalidQuantity(page, 30, 'z');

    // Back to the top: error 3 is now scrolled out of view.
    await setScrollTop(page, 0);
    await cell(page, 0, 0).click(); // position before every error
    await expect(statusChip(page)).toContainText('3 errors');

    // Chip click = next.
    await statusChip(page).click();
    await expect(activeCell(page)).toHaveAttribute('data-row', '1');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
    await expect(liveRegion(page)).toContainText('Error 1 of 3');

    await page.locator('[data-tm-status-next]').click();
    await expect(activeCell(page)).toHaveAttribute('data-row', '5');
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');
    await expect(liveRegion(page)).toContainText('Error 2 of 3');

    // The out-of-view error is activated AND scrolled into view.
    await page.locator('[data-tm-status-next]').click();
    await expect(activeCell(page)).toHaveAttribute('data-row', '30');
    await expect(liveRegion(page)).toContainText('Error 3 of 3');
    await expect(cell(page, 30, 1)).toBeVisible();
    expect(await scrollTopOf(page)).toBeGreaterThan(0);

    // Cycling: past the last wraps to the first; previous wraps back.
    await page.locator('[data-tm-status-next]').click();
    await expect(activeCell(page)).toHaveAttribute('data-row', '1');
    await expect(liveRegion(page)).toContainText('Error 1 of 3');

    await page.locator('[data-tm-status-prev]').click();
    await expect(activeCell(page)).toHaveAttribute('data-row', '30');
    await expect(liveRegion(page)).toContainText('Error 3 of 3');
  });
});

test.describe('clearing rules', () => {
  test('undo clears an invalid input and restores the prior value', async ({ page }) => {
    const before = await cellText(page, 4, 1);
    await makeInvalidQuantity(page, 4, 'x');

    await page.keyboard.press('Control+z');
    await expect(cell(page, 4, 1)).not.toHaveClass(/tm-grid__cell--error/);
    expect(await cellText(page, 4, 1)).toBe(before);
    await expect(statusChip(page)).toHaveCount(0); // no errors → no chip
  });

  test('committing a valid value clears the invalid input', async ({ page }) => {
    await makeInvalidQuantity(page, 6, 'x');

    await page.keyboard.press('ArrowUp'); // back onto the errored cell
    await page.keyboard.press('8');
    await page.keyboard.press('Enter');

    await expect(cell(page, 6, 1)).not.toHaveClass(/tm-grid__cell--error/);
    await expect.poll(async () => (await lineAt(page, 6)).quantity).toBe(8);
    await expect(statusChip(page)).toHaveCount(0);
  });

  test('Delete clears the raw text — and the required field error surfaces in its place', async ({
    page,
  }) => {
    await makeInvalidQuantity(page, 7, 'x');
    await page.keyboard.press('ArrowUp');
    await expect.poll(() => overlayMessage(page, 7, 1)).toContain('is not a valid Qty');

    await page.keyboard.press('Delete');

    // The raw text is gone, but the cell is still errored — the message
    // SWAPPED from the invalid-input string to the field's required error.
    expect(await cellText(page, 7, 1)).toBe('');
    await expect(cell(page, 7, 1)).toHaveClass(/tm-grid__cell--error/);
    await expect.poll(() => overlayMessage(page, 7, 1)).toContain('required');
    await expect(statusChip(page)).toContainText('1 error');
  });
});

test.describe('copy fidelity (real clipboard)', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

  test('copying an invalid-input cell exports its raw text', async ({ page }) => {
    await makeInvalidQuantity(page, 2, 'abc');
    await page.keyboard.press('ArrowUp'); // select the errored cell

    await page.keyboard.press('Control+c');
    const { text } = await readClipboard(page);
    expect(text).toBe('abc\r\n'); // the raw text, spreadsheet-terminated
  });
});
