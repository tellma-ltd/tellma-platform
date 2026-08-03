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
  await page.keyboard.press('Z');
  await expect(pickerInput(page)).toBeVisible();
  await page.keyboard.type('ebra'); // the search finds nothing — the resolver's question
  await expect(pickerInput(page)).toHaveValue('Zebra'); // every keystroke landed
  const searchesBefore = await searchCalls(page);
  await activateCell(page, 2, 3); // click-elsewhere commit
  // The editor tore down while the resolution is still pending — the
  // pending affordance is the GRID's, and exactly one resolver call runs.
  await expect(editor(page)).toHaveCount(0);
  await expect(cell(page, 2, 1).locator('.tm-grid__cell-spin')).toBeVisible();
  await expect(page.getByTestId('resolver-calls')).toHaveText('1');
  // No duplicate round-trip through the picker either.
  expect(await searchCalls(page)).toBe(searchesBefore);

  // 'Zebra' is neither a label nor a code: raw text kept, error-tinted.
  await expect(cell(page, 2, 1)).toHaveClass(/tm-grid__cell--error/);
  expect(await cellText(page, 2, 1)).toBe('Zebra');
  expect((await lines(page))[2].agentId).toBeNull();
  await expect(page.getByTestId('resolver-calls')).toHaveText('1');

  // One undo restores the pre-edit state.
  await activateCell(page, 2, 1);
  await pressUndo(page);
  await expect(cell(page, 2, 1)).not.toHaveClass(/tm-grid__cell--error/);
  expect((await lines(page))[2].agentId).toBeNull();
});

test('a commit while the search is in flight is decided BY that search, not the resolver', async ({
  page,
}) => {
  // The search the user was already waiting for outlives the editor: the
  // cell shows what they typed, the answer lands, and the column's resolver
  // — which asks a different question — is never troubled.
  await page.getByTestId('search-delay').fill('700');
  await activateCell(page, 2, 1);
  await page.keyboard.press('D');
  await page.keyboard.type('ana Ree'); // unique, but nothing is on screen yet
  await activateCell(page, 2, 3);
  await expect(editor(page)).toHaveCount(0);
  await expect(cell(page, 2, 1).locator('.tm-grid__cell-spin')).toBeVisible();
  expect(await cellText(page, 2, 1)).toBe('Dana Ree');
  await expect.poll(async () => (await lines(page))[2].agentId).toBe(7);
  expect(await cellText(page, 2, 1)).toBe('Dana Reed');
  await expect(page.getByTestId('resolver-calls')).toHaveText('0');
});

test('text the search cannot answer still reaches the resolver, which knows codes', async ({
  page,
}) => {
  // A search miss is not proof of no match: the resolver is an identity
  // lookup over labels AND codes, so it resolves what the name-search
  // type-ahead never offers.
  await page.getByTestId('resolver-delay').fill('100');
  await activateCell(page, 2, 1);
  await page.keyboard.press('A');
  await page.keyboard.type('G-007');
  await activateCell(page, 2, 3);
  await expect(editor(page)).toHaveCount(0);
  await expect(page.getByTestId('resolver-calls')).toHaveText('1');
  await expect.poll(async () => (await lines(page))[2].agentId).toBe(7);
  expect(await cellText(page, 2, 1)).toBe('Dana Reed');
});

test("a multi-hit still asks the resolver; the search's verdict is only the fallback", async ({
  page,
}) => {
  // 'Al' matches two agents and is neither of their labels. That is a dead
  // end for the SEARCH and still an open identity question — a consumer
  // whose type-ahead matches codes would return several rows for a code the
  // resolver knows. So the resolver is asked, and the search's "more than
  // one" only stands because this resolver cannot name 'Al' either. It is
  // the better message: the resolver alone would have said "no match".
  await page.getByTestId('resolver-delay').fill('100');
  await activateCell(page, 2, 1);
  await page.keyboard.press('A');
  await page.keyboard.type('l');
  await activateCell(page, 2, 3);
  await expect(editor(page)).toHaveCount(0);
  await expect(page.getByTestId('resolver-calls')).toHaveText('1');
  await expect(cell(page, 2, 1)).toHaveClass(/tm-grid__cell--error/);
  expect(await cellText(page, 2, 1)).toBe('Al');
  expect((await lines(page))[2].agentId).toBeNull();
  await activateCell(page, 2, 1);
  await expect(page.locator('.tm-grid__error-msg')).toContainText('matches more than one');
});

test('two rows carrying the SAME label are ambiguous outright, at no resolver cost', async ({
  page,
}) => {
  // 'Adam Brown' IS the label of two agents. That is an identity fact, not a
  // ranking one, so no lookup can improve on it.
  await activateCell(page, 2, 1);
  await page.keyboard.press('A');
  await page.keyboard.type('dam Brown');
  await activateCell(page, 2, 3);
  await expect(editor(page)).toHaveCount(0);
  await expect(cell(page, 2, 1)).toHaveClass(/tm-grid__cell--error/);
  expect((await lines(page))[2].agentId).toBeNull();
  await expect(page.getByTestId('resolver-calls')).toHaveText('0');
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

test('dismissing the dropdown does not un-fetch its answer: the commit still costs no resolver call', async ({
  page,
}) => {
  // Esc #1 in a cell closes the LIST, it does not cancel the edit — and the
  // unique result the user just saw is still the answer. Paying a resolver
  // round trip for it (and, for a partial query, getting notFound back) is
  // a regression the user experiences as "it found it a second ago".
  await activateCell(page, 2, 1);
  await page.keyboard.press('A');
  await page.keyboard.type('lice');
  await expect(activeOption(page)).toHaveText('Alice Green');
  await page.keyboard.press('Escape'); // list dismissed, session alive
  await expect(panel(page)).toHaveCount(0);
  await expect(pickerInput(page)).toBeVisible();
  await activateCell(page, 2, 3); // click-elsewhere commit
  await expect(editor(page)).toHaveCount(0);
  await expect.poll(async () => (await lines(page))[2].agentId).toBe(3);
  await expect(page.getByTestId('resolver-calls')).toHaveText('0');
  expect(await cellText(page, 2, 1)).toBe('Alice Green');
});

test('a cell awaiting resolution shows the text being resolved, not an empty cell', async ({
  page,
}) => {
  await page.getByTestId('resolver-delay').fill('700');
  await activateCell(page, 2, 1);
  await page.keyboard.press('Z');
  await page.keyboard.type('ebra'); // no search match — the resolver path
  await activateCell(page, 2, 3);
  await expect(editor(page)).toHaveCount(0);
  // Mid-flight: the spinner AND what the user committed.
  await expect(cell(page, 2, 1).locator('.tm-grid__cell-spin')).toBeVisible();
  expect(await cellText(page, 2, 1)).toBe('Zebra');
  // The cell is not errored YET — the answer has not come back.
  await expect(cell(page, 2, 1)).not.toHaveClass(/tm-grid__cell--error/);
  await expect(cell(page, 2, 1)).toHaveClass(/tm-grid__cell--error/);
  expect(await cellText(page, 2, 1)).toBe('Zebra');
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
