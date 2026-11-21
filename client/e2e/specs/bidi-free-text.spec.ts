// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { syntheticPaste } from '../support/clipboard';
import { activateCell, cell, gotoGrid, modelJson, syntheticCopy } from '../support/grid';
import { storyUrl } from '../support/story-map';

// Formatted text carries no bidirectional control character (spec 0010
// §D.14): markup sets direction instead. In the browser that means a minus
// still shows at the left of its digits in a right-to-left page — in a grid
// number cell and in a number input, with Latin and with Arabic-Indic
// digits — and a copied Arabic date pastes as plain text that reads back
// as the same date.

/** The bidirectional controls: the marks, the embeddings and overrides, the isolates. */
const BIDI_CONTROLS = new RegExp(
  `[${[0x061c, 0x200e, 0x200f, 0x202a, 0x202b, 0x202c, 0x202d, 0x202e, 0x2066, 0x2067, 0x2068, 0x2069]
    .map((codePoint) => String.fromCodePoint(codePoint))
    .join('')}]`,
);

/** The editable story's data columns this battery touches. */
const UNIT_PRICE_COL = 2;
const DUE_COL = 8;

/** The line shape of the editable story's model dump, as far as this battery reads it. */
interface InvoiceLine {
  readonly unitPrice: number | null;
  readonly dueDate: string | null;
}

/**
 * Whether the first character of the element's text is drawn to the left
 * of every other character — the minus of a negative number, at the left
 * of its digits.
 */
async function firstCharacterIsLeftmost(text: Locator): Promise<boolean> {
  return text.evaluate((element) => {
    const node = element.firstChild;
    if (!(node instanceof Text) || node.length < 2) {
      throw new Error('firstCharacterIsLeftmost: the element holds no text of two characters.');
    }
    const range = document.createRange();
    range.setStart(node, 0);
    range.setEnd(node, 1);
    const first = range.getBoundingClientRect();
    range.setStart(node, 1);
    range.setEnd(node, node.length);
    const rest = range.getBoundingClientRect();
    // Half a pixel of tolerance for sub-pixel glyph positions.
    return first.right <= rest.left + 0.5;
  });
}

/**
 * The caret position after a click in the blank space at the left edge of
 * an input. The click lands left of all the (right-aligned) text, so the
 * caret goes to the line's left end: offset 0 exactly when the first
 * character is the one drawn at the left.
 */
async function caretAtLeftEdge(page: Page, input: Locator): Promise<number | null> {
  const box = (await input.boundingBox())!;
  await page.mouse.click(box.x + 3, box.y + box.height / 2);
  return input.evaluate((element) => (element as HTMLInputElement).selectionStart);
}

test.describe('a negative number keeps its minus at the left in RTL', () => {
  test('grid number cell: Latin digits, then Arabic-Indic digits', async ({ page }) => {
    // English in a right-to-left page: Latin digits.
    await gotoGrid(page, 'grid-editable', { dir: 'rtl' });
    await activateCell(page, 0, UNIT_PRICE_COL);
    await page.keyboard.type('-1234.5');
    await page.keyboard.press('Enter');

    const text = cell(page, 0, UNIT_PRICE_COL).locator('.tm-grid__text');
    await expect(text).toHaveText('-1,234.5');
    // The cell's text is its own left-to-right run...
    await expect(text).toHaveAttribute('dir', 'ltr');
    expect(await text.evaluate((element) => getComputedStyle(element).direction)).toBe('ltr');
    // ...so the minus is drawn at the left, with no mark in the text.
    expect(await firstCharacterIsLeftmost(text)).toBe(true);
    expect(await text.textContent()).not.toMatch(BIDI_CONTROLS);

    // Arabic: Arabic-Indic digits and separators, the same minus position.
    await page.getByTestId('lang-ar').click();
    await expect(text).toHaveText('-١٬٢٣٤٫٥');
    expect(await firstCharacterIsLeftmost(text)).toBe(true);
    expect(await text.textContent()).not.toMatch(BIDI_CONTROLS);

    // A text cell beside it carries no direction of its own.
    await expect(cell(page, 0, 0).locator('.tm-grid__text')).not.toHaveAttribute('dir');
  });

  test('input[tmNumber]: Latin digits, then Arabic-Indic digits', async ({ page }) => {
    await page.goto(storyUrl('number', { dir: 'rtl' }));
    const amount = page.getByTestId('input-amount');
    // The field resolves its direction from its own text.
    await expect(amount).toHaveAttribute('dir', 'auto');

    await amount.fill('-1234.5');
    await amount.blur();
    await expect(amount).toHaveValue('-1,234.50');
    expect(await amount.evaluate((element) => getComputedStyle(element).direction)).toBe('ltr');
    expect(await caretAtLeftEdge(page, amount)).toBe(0);
    await amount.blur();

    // Arabic: the digits are Arabic-Indic, the field still left-to-right.
    await page.getByTestId('lang-ar').click();
    await expect(amount).toHaveValue('-١٬٢٣٤٫٥٠');
    expect(await amount.inputValue()).not.toMatch(BIDI_CONTROLS);
    expect(await amount.evaluate((element) => getComputedStyle(element).direction)).toBe('ltr');
    expect(await caretAtLeftEdge(page, amount)).toBe(0);
  });
});

test.describe('number grouping', () => {
  test('a year field writes no group separators and still reads a typed one', async ({ page }) => {
    await page.goto(storyUrl('number'));
    const year = page.getByTestId('input-year');
    await expect(year).toHaveValue('2026');

    await year.fill('2,027');
    await year.blur();
    await expect(year).toHaveValue('2027');
    await expect(page.getByTestId('model-json')).toContainText('"year":2027');

    // The digits stay the locale's.
    await page.getByTestId('lang-ar').click();
    await expect(year).toHaveValue('٢٠٢٧');
  });
});

test.describe('copied text is plain text', () => {
  test('a copied Arabic date range carries no direction mark and pastes back as the same dates', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-editable');
    await page.getByTestId('lang-ar').click();
    const before = await modelJson<InvoiceLine[]>(page);

    // Copy the first three due dates.
    await activateCell(page, 0, DUE_COL);
    await page.keyboard.press('Shift+ArrowDown');
    await page.keyboard.press('Shift+ArrowDown');
    const copied = await syntheticCopy(page);

    // Plain digits and slashes: no mark a spreadsheet would choke on.
    expect(copied.text).not.toMatch(BIDI_CONTROLS);
    expect(copied.html).not.toMatch(BIDI_CONTROLS);
    const lines = copied.text.split('\r\n').filter((line) => line !== '');
    expect(lines).toHaveLength(3);
    for (const line of lines) {
      expect(line).toMatch(/^[٠-٩]{1,2}\/[٠-٩]{1,2}\/[٠-٩]{4}$/);
    }

    // Pasted as plain text three rows down, the text reads back as the
    // same three dates.
    await activateCell(page, 3, DUE_COL);
    await syntheticPaste(page, { text: copied.text });
    await expect
      .poll(async () =>
        (await modelJson<InvoiceLine[]>(page)).slice(3, 6).map((line) => line.dueDate),
      )
      .toEqual(before.slice(0, 3).map((line) => line.dueDate));
  });
});
