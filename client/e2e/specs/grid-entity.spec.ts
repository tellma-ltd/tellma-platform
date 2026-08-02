// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { pressUndo, syntheticPaste } from '../support/clipboard';
import { activeCell, activateCell, cell, cellText, editor, gotoGrid, modelJson } from '../support/grid';

/**
 * The built-in entity editor inside the grid (the grid-entity story):
 * type-to-edit search, the cell-anchored dropdown, pick-IS-the-edit,
 * Tab-commit-move, the two-stage Esc, the typed-commit resolution pipeline
 * (exactly one resolver call after editor teardown under the grid's
 * pending affordance), the no-resolver column, paste regression, mid-edit
 * modal round trips, and the axe/RTL gates.
 *
 * Story columns: 0 description (text), 1 agentId (entity, full config),
 * 2 otherId (entity, search only — no resolver), 3 quantity (number).
 * Row 0 starts with agentId 5 ('Bob Stone').
 */

interface OrderLine {
  readonly id: number;
  readonly description: string | null;
  readonly agentId: number | null;
  readonly otherId: number | null;
  readonly quantity: number | null;
}

const lines = modelJson<OrderLine[]>;

/** The mounted picker's input inside the editing cell. */
function pickerInput(page: Page): Locator {
  return page.locator('[data-tm-editor] .tm-entity-picker__input');
}

/** The picker's top-layer dropdown panel. */
function panel(page: Page): Locator {
  return page.locator('.tm-entity-picker__panel');
}

/** The highlighted option, if any. */
function activeOption(page: Page): Locator {
  return page.locator('.tm-entity-picker__option[data-active="true"]');
}

async function searchCalls(page: Page): Promise<number> {
  return Number(await page.getByTestId('search-calls').textContent());
}

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-entity');
});

test('type-to-edit searches the seed; Enter on the highlight commits with NO move', async ({
  page,
}) => {
  await activateCell(page, 2, 1); // row 2's agent starts empty
  await page.keyboard.press('A');
  await expect(pickerInput(page)).toBeVisible();
  await expect(pickerInput(page)).toHaveValue('A');
  await page.keyboard.type('lice');
  await expect(activeOption(page)).toHaveText('Alice Green');
  await page.keyboard.press('Enter');
  await expect(editor(page)).toHaveCount(0);
  await expect.poll(async () => (await lines(page))[2].agentId).toBe(3);
  // The pick IS the edit: no move.
  await expect(activeCell(page)).toHaveAttribute('data-row', '2');
  await expect(activeCell(page)).toHaveAttribute('data-col', '1');
  expect(await cellText(page, 2, 1)).toBe('Alice Green');
});

test('Alt+ArrowDown opens editor + browse dropdown anchored to the cell box', async ({ page }) => {
  await activateCell(page, 0, 1); // row 0's agent is Bob Stone
  await page.keyboard.press('Alt+ArrowDown');
  await expect(pickerInput(page)).toBeVisible();
  await expect(panel(page)).toBeVisible();
  // The pristine browse mirrors the committed row as selected, highlighting nothing.
  await expect(page.locator('.tm-entity-picker__option[aria-selected="true"]')).toContainText(
    'Bob Stone',
  );
  await expect(activeOption(page)).toHaveCount(0);
  // Cell-anchored with matchWidth: the panel tracks the cell's box, floored
  // by the min-width token (the panel may exceed the cell, never undershoot).
  const cellBox = (await cell(page, 0, 1).boundingBox())!;
  const panelBox = (await panel(page).boundingBox())!;
  expect(Math.abs(panelBox.x - cellBox.x)).toBeLessThanOrEqual(2);
  // The 180px column sits BELOW the token floor, so this open exercises the
  // floor branch — pin the floor itself, not just matchWidth.
  const floor = await panel(page).evaluate((el) => parseFloat(getComputedStyle(el).minInlineSize));
  expect(floor).toBeGreaterThan(cellBox.width);
  expect(panelBox.width).toBeGreaterThanOrEqual(floor - 1);
});

test('Tab with a highlighted result commits and the grid moves to the next cell', async ({
  page,
}) => {
  await activateCell(page, 1, 1);
  await page.keyboard.press('A');
  await page.keyboard.type('lan');
  await expect(activeOption(page)).toHaveText('Alan Grey');
  await page.keyboard.press('Tab');
  await expect(editor(page)).toHaveCount(0);
  await expect.poll(async () => (await lines(page))[1].agentId).toBe(4);
  await expect(activeCell(page)).toHaveAttribute('data-col', '2'); // commit-and-move
});

test('the two-stage Esc: dropdown first, then the grid cancel', async ({ page }) => {
  await activateCell(page, 0, 1);
  await page.keyboard.press('F2');
  await expect(pickerInput(page)).toHaveValue('Bob Stone'); // quiet display-text open
  await expect(panel(page)).toHaveCount(0);
  await page.keyboard.press('Alt+ArrowDown');
  await expect(panel(page)).toBeVisible();

  await page.keyboard.press('Escape'); // №1: the dropdown only
  await expect(panel(page)).toHaveCount(0);
  await expect(pickerInput(page)).toBeVisible();

  await page.keyboard.press('Escape'); // №2: the grid cancels the session
  await expect(editor(page)).toHaveCount(0);
  expect((await lines(page))[0].agentId).toBe(5); // nothing was written
  expect(await cellText(page, 0, 1)).toBe('Bob Stone');
});

test('unresolved commit text resolves through the column resolver AFTER editor teardown', async ({
  page,
}) => {
  await page.getByTestId('resolver-delay').fill('600');
  await activateCell(page, 2, 1);
  await page.keyboard.press('A');
  await page.keyboard.type('dam Brown'); // two on-screen matches — no fast path
  await expect(activeOption(page)).toBeVisible();
  const searchesBefore = await searchCalls(page);
  await activateCell(page, 2, 3); // click-elsewhere commit
  // The editor tore down while the resolution is still pending — the
  // pending affordance is the GRID's, and exactly one resolver call runs.
  await expect(editor(page)).toHaveCount(0);
  await expect(cell(page, 2, 1).locator('.tm-grid__cell-spin')).toBeVisible();
  await expect(page.getByTestId('resolver-calls')).toHaveText('1');
  // No duplicate round-trip through the picker either.
  expect(await searchCalls(page)).toBe(searchesBefore);

  // 'Adam Brown' is ambiguous: raw text kept, error-tinted, model cleared.
  await expect(cell(page, 2, 1)).toHaveClass(/tm-grid__cell--error/);
  expect(await cellText(page, 2, 1)).toBe('Adam Brown');
  expect((await lines(page))[2].agentId).toBeNull();
  await expect(page.getByTestId('resolver-calls')).toHaveText('1');

  // One undo restores the pre-edit state.
  await activateCell(page, 2, 1);
  await pressUndo(page);
  await expect(cell(page, 2, 1)).not.toHaveClass(/tm-grid__cell--error/);
  expect((await lines(page))[2].agentId).toBeNull();
});

test('a commit while the search is in flight resolves the raw text to a value', async ({
  page,
}) => {
  await page.getByTestId('search-delay').fill('1500'); // the search never lands in time
  await page.getByTestId('resolver-delay').fill('100');
  await activateCell(page, 2, 1);
  await page.keyboard.press('D');
  await page.keyboard.type('ana Reed');
  await activateCell(page, 2, 3); // commit with no on-screen results
  await expect.poll(async () => (await lines(page))[2].agentId).toBe(7);
  expect(await cellText(page, 2, 1)).toBe('Dana Reed');
});

test('the unique on-screen result commits synchronously with zero resolver calls', async ({
  page,
}) => {
  await activateCell(page, 2, 1);
  await page.keyboard.press('A');
  await page.keyboard.type('lice Gr'); // unique on screen
  await expect(activeOption(page)).toHaveText('Alice Green');
  await activateCell(page, 2, 3); // blur-commit, no activation
  await expect.poll(async () => (await lines(page))[2].agentId).toBe(3);
  await expect(page.getByTestId('resolver-calls')).toHaveText('0');
});

test('a no-resolver entity column records the invalid input directly', async ({ page }) => {
  await activateCell(page, 1, 2); // otherId: search, no resolver
  await page.keyboard.press('Z');
  await page.keyboard.type('ebra');
  await page.keyboard.press('Enter'); // no highlight (empty set) → the grid commits
  await expect(editor(page)).toHaveCount(0);
  await expect(cell(page, 1, 2)).toHaveClass(/tm-grid__cell--error/);
  expect(await cellText(page, 1, 2)).toBe('Zebra');
  expect((await lines(page))[1].otherId).toBeNull();
  await expect(cell(page, 1, 2).locator('.tm-grid__cell-spin')).toHaveCount(0); // no pending
});

test('paste into the entity column still runs the batched deduped resolver', async ({ page }) => {
  await page.getByTestId('resolver-delay').fill('0');
  await activateCell(page, 3, 1);
  await syntheticPaste(page, { text: 'Alice Green\r\nBob Stone\r\n' });
  await expect(page.getByTestId('resolver-calls')).toHaveText('1');
  await expect
    .poll(async () => {
      const model = await lines(page);
      return [model[3].agentId, model[4].agentId];
    })
    .toEqual([3, 5]);
});

test('a mid-edit modal holds the session; a pick commits through it with no move', async ({
  page,
}) => {
  await activateCell(page, 0, 1);
  await page.keyboard.press('F2');
  await expect(pickerInput(page)).toBeVisible();
  await page.locator('[data-tm-editor] .tm-entity-picker__magnifier').click();
  await expect(page.getByTestId('gp-pick')).toBeVisible();
  await expect(pickerInput(page)).toBeVisible(); // the session is still open behind it

  await page.getByTestId('gp-pick').click();
  await expect(editor(page)).toHaveCount(0);
  await expect.poll(async () => (await lines(page))[0].agentId).toBe(3);
  await expect(activeCell(page)).toHaveAttribute('data-row', '0'); // no move
  await expect(activeCell(page)).toHaveAttribute('data-col', '1');
});

test('a mid-edit modal dismissal returns to the intact session; close(null) clears', async ({
  page,
}) => {
  await activateCell(page, 0, 1);
  await page.keyboard.press('F2');
  await page.locator('[data-tm-editor] .tm-entity-picker__magnifier').click();
  await page.getByTestId('gp-cancel').click();
  await expect(pickerInput(page)).toBeVisible(); // the session survived
  await expect(pickerInput(page)).toHaveValue('Bob Stone');
  await expect(pickerInput(page)).toBeFocused();

  // Edit… (footer row) → the page reports the entity gone → the field
  // clears; committing writes null through the value channel.
  await page.keyboard.press('Alt+ArrowDown');
  await expect(panel(page)).toBeVisible();
  await page.keyboard.press('ArrowUp'); // wraps straight to the last — Edit…
  await expect(activeOption(page)).toHaveText('Edit…');
  await page.keyboard.press('Enter');
  await expect(page.getByTestId('gp-null')).toBeVisible();
  await page.getByTestId('gp-null').click();
  await expect(pickerInput(page)).toHaveValue('');
  await page.keyboard.press('Enter');
  await expect(editor(page)).toHaveCount(0);
  await expect.poll(async () => (await lines(page))[0].agentId).toBeNull();
});

test.describe('axe & RTL', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`no violations with the in-cell editor and dropdown open (${theme})`, async ({
      page,
    }) => {
      await gotoGrid(page, 'grid-entity', { theme });
      await activateCell(page, 1, 1);
      await page.keyboard.press('Alt+ArrowDown');
      await expect(panel(page)).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }

  test('under dir=rtl the cell-anchored panel mirrors', async ({ page }) => {
    await gotoGrid(page, 'grid-entity', { dir: 'rtl' });
    await activateCell(page, 1, 1);
    await page.keyboard.press('Alt+ArrowDown');
    await expect(panel(page)).toBeVisible();
    await expect(
      page.locator('.cdk-overlay-connected-position-bounding-box'),
    ).toHaveAttribute('dir', 'rtl');
    const magnifier = page.locator('[data-tm-editor] .tm-entity-picker__magnifier');
    const inputBox = (await pickerInput(page).boundingBox())!;
    const magnifierBox = (await magnifier.boundingBox())!;
    expect(magnifierBox.x).toBeLessThan(inputBox.x); // inline end = physical left
  });
});
