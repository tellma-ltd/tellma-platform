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
  editor,
  editorCaret,
  editorInput,
  gotoGrid,
  gridScroller,
  modelJson,
  rowHeader,
  scrollTopOf,
  setScrollTop,
} from '../support/grid';

/**
 * The editing lifecycle (spec 0004 §8.2 editing table, §8.4) with real
 * browser input against the editable invoice-lines story: type-to-edit and
 * the edit/enter mode split, every commit path (Enter/Tab/arrows, blur,
 * cell click), Esc cancel, the enum editor's two-stage Esc, parse errors,
 * the placeholder row, the readonly flip, and IME composition through CDP.
 * The TestBed layer already covers the synchronous mechanics; this battery
 * proves them against trusted input and the real focus/scroll machinery.
 */

interface Line {
  readonly id: number;
  readonly description: string | null;
  readonly quantity: number | null;
  readonly unitPrice: number | null;
  readonly discount: number | null;
  readonly isPosted: boolean;
  readonly category: string | null;
  readonly agentId: number | null;
}

async function lineAt(page: Page, index: number): Promise<Line> {
  return (await modelJson<Line[]>(page))[index];
}

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-editable');
});

test.describe('type-to-edit and commit paths', () => {
  test('a printable key opens the editor seeded with it, caret at end; Enter commits and moves down', async ({
    page,
  }) => {
    await activateCell(page, 0, 0);
    await page.keyboard.press('x');

    const input = editorInput(page);
    await expect(input).toBeVisible();
    await expect(input).toHaveValue('x');
    await expect(input).toBeFocused();
    expect(await editorCaret(page)).toBe(1);

    await page.keyboard.type('-ray');
    await page.keyboard.press('Enter');
    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 0)).description).toBe('x-ray');
    await expect(activeCell(page)).toHaveAttribute('data-row', '1');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
  });

  test('F2 opens edit mode with the full display text; ArrowLeft moves the caret, not the cell', async ({
    page,
  }) => {
    const displayText = await cellText(page, 0, 0);
    await activateCell(page, 0, 0);
    await page.keyboard.press('F2');

    const input = editorInput(page);
    await expect(input).toHaveValue(displayText);
    expect(await editorCaret(page)).toBe(displayText.length);

    await page.keyboard.press('ArrowLeft');
    // The caret moved inside the editor; the active cell did not.
    await expect.poll(() => editorCaret(page)).toBe(displayText.length - 1);
    await expect(editor(page)).toBeVisible();
    await expect(activeCell(page)).toHaveAttribute('data-row', '0');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
  });

  test('enter-mode ArrowRight commits and moves inline-end', async ({ page }) => {
    await activateCell(page, 1, 0);
    await page.keyboard.press('q'); // enter mode
    await page.keyboard.press('ArrowRight');

    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 1)).description).toBe('q');
    await expect(activeCell(page)).toHaveAttribute('data-row', '1');
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');
  });

  test('ArrowDown and ArrowUp commit and move in BOTH modes', async ({ page }) => {
    // Enter mode: typing opened the session.
    await activateCell(page, 2, 1);
    await page.keyboard.press('7');
    await page.keyboard.press('ArrowDown');
    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 2)).quantity).toBe(7);
    await expect(activeCell(page)).toHaveAttribute('data-row', '3');

    // Edit mode: F2 opened the session (caret keys own ←/→, but ↑/↓ commit).
    const qty = (await lineAt(page, 3)).quantity ?? 0;
    await page.keyboard.press('F2');
    await page.keyboard.type('5'); // appended at the end-of-text caret
    await page.keyboard.press('ArrowUp');
    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 3)).quantity).toBe(Number(`${qty}5`));
    await expect(activeCell(page)).toHaveAttribute('data-row', '2');
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');
  });

  test('Tab commits and moves the selection without opening an editor; Enter returns to the run origin', async ({
    page,
  }) => {
    await activateCell(page, 5, 0);
    await page.keyboard.press('A'); // editor seeded 'A'
    await page.keyboard.press('Tab');
    await expect(editor(page)).toHaveCount(0); // no editor on the Tab target (Excel)
    await expect(activeCell(page)).toHaveAttribute('data-col', '1');

    await page.keyboard.press('Tab'); // navigation Tab continues the run
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');

    await page.keyboard.press('9'); // one keystroke re-enters editing
    await page.keyboard.press('Enter');
    // Enter after a Tab run: next row, back at the run's ORIGIN column.
    await expect(activeCell(page)).toHaveAttribute('data-row', '6');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
    await expect.poll(async () => (await lineAt(page, 5)).description).toBe('A');
    await expect.poll(async () => (await lineAt(page, 5)).unitPrice).toBe(9);
  });

  test('Esc cancels the session and never writes the model', async ({ page }) => {
    const before = await page.getByTestId('model-json').textContent();
    await activateCell(page, 0, 1);
    await page.keyboard.press('F2');
    await page.keyboard.type('99');
    await page.keyboard.press('Escape');

    await expect(editor(page)).toHaveCount(0);
    await expect(page.getByTestId('model-json')).toHaveText(before ?? '');
    await expect(cell(page, 0, 1)).toBeFocused();
  });
  test('double-click opens the editor in edit mode', async ({ page }) => {
    const displayText = await cellText(page, 1, 0);
    await cell(page, 1, 0).dblclick();

    await expect(editorInput(page)).toHaveValue(displayText);
    await page.keyboard.press('ArrowLeft'); // edit mode: the caret moves, the session stays
    await expect(editor(page)).toBeVisible();
    await expect(activeCell(page)).toHaveAttribute('data-row', '1');
    await page.keyboard.press('Escape');
  });

  test('commit-on-blur: clicking outside the grid commits the open editor', async ({ page }) => {
    await activateCell(page, 2, 0);
    await page.keyboard.press('B');
    await page.keyboard.type('lurred');
    await page.getByRole('heading', { name: 'Grid (editable)' }).click(); // outside the grid

    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 2)).description).toBe('Blurred');
  });

  test('clicking another cell commits the open editor first', async ({ page }) => {
    await activateCell(page, 0, 0);
    await page.keyboard.press('C');
    await cell(page, 3, 2).click();

    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 0)).description).toBe('C');
    await expect(activeCell(page)).toHaveAttribute('data-row', '3');
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
  });

  test('a number parse error keeps the raw text visible in error state; a valid re-entry clears it', async ({
    page,
  }) => {
    await activateCell(page, 0, 1);
    await page.keyboard.press('a');
    await page.keyboard.type('bc');
    await page.keyboard.press('Enter');

    // The raw text stays in place, styled as an error; the model is cleared.
    expect(await cellText(page, 0, 1)).toBe('abc');
    await expect(cell(page, 0, 1)).toHaveClass(/tm-grid__cell--error/);
    await expect(cell(page, 0, 1)).toHaveAttribute('aria-invalid', 'true');
    await expect.poll(async () => (await lineAt(page, 0)).quantity).toBeNull();

    await page.keyboard.press('ArrowUp'); // back to the errored cell
    await page.keyboard.press('5');
    await page.keyboard.press('Enter');
    await expect.poll(async () => (await lineAt(page, 0)).quantity).toBe(5);
    expect(await cellText(page, 0, 1)).toBe('5');
    await expect(cell(page, 0, 1)).not.toHaveClass(/tm-grid__cell--error/);
  });

  test('an editing keystroke on a scrolled-away editor scrolls the cell back into view', async ({
    page,
  }) => {
    await activateCell(page, 0, 2);
    await page.keyboard.press('F2');
    await expect(editor(page)).toBeVisible();

    await setScrollTop(page, 600);
    await expect.poll(() => scrollTopOf(page)).toBeGreaterThan(500);
    // The editing row stays rendered (active-row outlier) and keeps focus.
    await expect(editorInput(page)).toBeFocused();

    await page.keyboard.type('1'); // any editing keystroke reveals first (§4)
    await expect.poll(() => scrollTopOf(page)).toBe(0);
    await page.keyboard.press('Escape');
  });
});

test.describe('boolean cells', () => {
  test('Space toggles atomically (no editor session); undo reverts the toggle', async ({
    page,
  }) => {
    const before = (await lineAt(page, 1)).isPosted;
    await activateCell(page, 1, 4);
    await page.keyboard.press(' ');

    await expect(editor(page)).toHaveCount(0); // no session — an atomic toggle
    await expect.poll(async () => (await lineAt(page, 1)).isPosted).toBe(!before);

    await page.keyboard.press('Control+z');
    await expect.poll(async () => (await lineAt(page, 1)).isPosted).toBe(before);
  });
});

test.describe('enum cells (built-in tm-select editor)', () => {
  test('Enter opens the panel, typing seeds the typeahead, Enter commits the active option and stays', async ({
    page,
  }) => {
    await activateCell(page, 0, 5);
    await page.keyboard.press('Enter');
    await expect(page.locator('.tm-select__panel')).toBeVisible();
    // The option rows land one render pass after the overlay attaches —
    // typeahead only works once they exist.
    await expect(page.locator('.tm-option__row')).toHaveCount(4);

    await page.keyboard.press('f'); // typeahead → 'Freight'
    await expect(page.locator('.tm-option__row[data-active="true"]')).toContainText('Freight');
    await page.keyboard.press('Enter'); // activates the option: commit-and-close, no move

    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 0)).category).toBe('freight');
    await expect(cell(page, 0, 5)).toBeFocused();
    expect(await cellText(page, 0, 5)).toBe('Freight');
  });

  test('two-stage Esc: the first closes the panel, the second cancels the session', async ({
    page,
  }) => {
    const before = (await lineAt(page, 1)).category;
    await activateCell(page, 1, 5);
    await page.keyboard.press('Enter');
    await expect(page.locator('.tm-select__panel')).toBeVisible();

    await page.keyboard.press('Escape'); // №1: the select consumes it, panel closes
    await expect(page.locator('.tm-select__panel')).toHaveCount(0);
    await expect(editor(page)).toBeVisible(); // the session is still open

    await page.keyboard.press('Escape'); // №2: reaches the grid, cancels
    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 1)).category).toBe(before);
    await expect(cell(page, 1, 5)).toBeFocused();
  });

  test('Alt+ArrowDown opens the dropdown from navigation state', async ({ page }) => {
    await activateCell(page, 2, 5);
    await page.keyboard.press('Alt+ArrowDown');

    await expect(editor(page)).toBeVisible();
    await expect(page.locator('.tm-select__panel')).toBeVisible();

    await page.keyboard.press('Escape');
    await page.keyboard.press('Escape');
  });
});

test.describe('new-row placeholder', () => {
  test('typing in the placeholder materializes exactly one row; a single undo removes it entirely', async ({
    page,
  }) => {
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '42'); // header + 40 + placeholder

    // Scroll the placeholder row into view and click its description cell.
    await setScrollTop(page, 10_000);
    await expect(rowHeader(page, 40)).toHaveText('*');
    await activateCell(page, 40, 0);

    await page.keyboard.press('N');
    await page.keyboard.type('ew line');
    await page.keyboard.press('Enter');

    // Exactly ONE row materialized, with a factory-minted negative temp id…
    await expect.poll(async () => (await modelJson<Line[]>(page)).length).toBe(41);
    const added = await lineAt(page, 40);
    expect(added.description).toBe('New line');
    expect(added.id).toBe(-1);
    // …and a fresh placeholder appeared beneath it.
    await expect(rowHeader(page, 41)).toHaveText('*');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '43');

    // One undo removes the materialization AND the write together.
    await expect(cell(page, 41, 0)).toBeFocused(); // Enter moved onto the fresh placeholder
    await page.keyboard.press('Control+z');
    await expect.poll(async () => (await modelJson<Line[]>(page)).length).toBe(40);
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '42');
  });
  test('Ctrl+End keeps keyboard focus on the active cell (editable grid)', async ({
    page,
  }) => {
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+End');
    await expect(cell(page, 39, 7)).toBeFocused();
  });
});

test.describe('readonly flip', () => {
  test('flipping readonly mid-edit closes the editor without changing the model; flipping back keeps the selection', async ({
    page,
  }) => {
    await activateCell(page, 0, 0);
    await page.keyboard.press('F2'); // open, seeded with the display text, nothing typed
    await expect(editor(page)).toBeVisible();
    const before = await page.getByTestId('model-json').textContent();

    // Clicking the toggle blurs the grid (commit-on-blur runs with the
    // unchanged text) and then flips the mode; the programmatic-flip
    // cancel path is pinned by the TestBed spec.
    await page.getByTestId('toggle-readonly').click();

    await expect(editor(page)).toHaveCount(0);
    await expect(page.getByTestId('model-json')).toHaveText(before ?? '');
    // The placeholder row is gone and the status bar hides in readonly mode.
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '41');
    await expect(page.locator('tm-grid-status-bar')).toHaveCount(0);

    await page.getByTestId('toggle-readonly').click(); // back to editable
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '42');
    await expect(page.locator('tm-grid-status-bar')).toHaveCount(1);
    // The active cell and its selection survived the round trip.
    await expect(activeCell(page)).toHaveAttribute('data-row', '0');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');
    await expect(cell(page, 0, 0)).toHaveAttribute('aria-selected', 'true');
  });
});

test.describe('IME composition (CDP)', () => {
  test('a composition keydown opens an UNSEEDED editor and the composition lands inside it', async ({
    page,
  }) => {
    await cell(page, 0, 0).click();
    await expect(cell(page, 0, 0)).toBeFocused();

    const session = await page.context().newCDPSession(page);
    // A real IME's first keydown carries keyCode 229 ('Process') — dispatch
    // it as a trusted key event so the grid's IME branch (§8.4) opens the
    // editor and moves focus into its input before any composition starts.
    await session.send('Input.dispatchKeyEvent', {
      type: 'rawKeyDown',
      key: 'Process',
      code: 'KeyA',
      windowsVirtualKeyCode: 229,
      nativeVirtualKeyCode: 229,
    });

    const input = editorInput(page);
    await expect(input).toBeVisible();
    await expect(input).toHaveValue(''); // UNSEEDED — the composition supplies content
    await expect(input).toBeFocused();

    // The composition targets the focused editable — the editor's input,
    // never the non-editable cell.
    await session.send('Input.imeSetComposition', {
      text: 'に',
      selectionStart: -1,
      selectionEnd: -1,
    });
    await expect(input).toHaveValue('に');

    await session.send('Input.insertText', { text: '日本' }); // commit the composition
    await expect(input).toHaveValue('日本');

    await page.keyboard.press('Enter');
    await expect(editor(page)).toHaveCount(0);
    await expect.poll(async () => (await lineAt(page, 0)).description).toBe('日本');
  });
});

test.describe('custom editor story (grid-custom-editor)', () => {
  interface Review {
    readonly id: number;
    readonly product: string | null;
    readonly rating: number | null;
    readonly priority: string | null;
  }

  test('a consumer TmCellEditor control edits through the registration path', async ({ page }) => {
    await gotoGrid(page, 'grid-custom-editor');

    await activateCell(page, 0, 1); // the rating column
    await page.keyboard.press('Enter');
    const ratingEditor = page.locator('[data-tm-editor] .rating-editor');
    await expect(ratingEditor).toBeVisible();

    await ratingEditor.locator('.rating-editor__star').nth(3).click(); // ★★★★
    await page.keyboard.press('Enter');

    await expect(editor(page)).toHaveCount(0);
    await expect
      .poll(async () => (await modelJson<Review[]>(page))[0].rating)
      .toBe(4);
    expect(await cellText(page, 0, 1)).toBe('4');
  });
});
