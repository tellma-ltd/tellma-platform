// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { pressUndo, syntheticPaste } from '../support/clipboard';
import {
  activeCell,
  activateCell,
  cell,
  cellText,
  editor,
  gotoGrid,
  modelJson,
} from '../support/grid';

/**
 * The async label→value resolution pipeline (spec 0004 §9.4) against the
 * editable story's agent entity column (column 6): batched deduped resolver
 * calls, the pending affordance (§10), notFound/ambiguous invalid inputs
 * with their distinct messages, one-undo-op integrity across resolutions,
 * and the interleaving guards (manual edit and undo during pending).
 *
 * The story's resolver resolves against a fixed directory after a
 * configurable delay (`resolver-delay` testid) and counts its calls
 * (`resolver-calls`): 'Alice Green' → 11, 'Bob Stone' → 12, 'Adam Brown' →
 * ambiguous (two ids), anything else → notFound.
 */

interface InvoiceLine {
  readonly id: number;
  readonly description: string | null;
  readonly agentId: number | null;
}

const lines = modelJson<InvoiceLine[]>;

/** Sets the story resolver's artificial delay (ms). */
async function setResolverDelay(page: Page, ms: number): Promise<void> {
  await page.getByTestId('resolver-delay').fill(String(ms));
}

function resolverCalls(page: Page) {
  return page.getByTestId('resolver-calls');
}

/** The status bar's pending tally ("N cells resolving"). */
function pendingStatus(page: Page) {
  return page.locator('.tm-grid__status-pending');
}

/** The active-cell error overlay (rendered for the active cell only). */
function errorOverlay(page: Page) {
  return page.locator('.tm-grid__error-msg');
}

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-editable');
});

test('a pasted label column costs exactly ONE deduped resolver call', async ({ page }) => {
  await setResolverDelay(page, 300);
  await activateCell(page, 1, 6);
  // Four cells, two DISTINCT labels — §9.4: one batched call per column.
  await syntheticPaste(page, { text: 'Alice Green\r\nBob Stone\r\nAlice Green\r\nBob Stone\r\n' });

  await expect(resolverCalls(page)).toHaveText('1');
  await expect
    .poll(async () => {
      const model = await lines(page);
      return [model[1].agentId, model[2].agentId, model[3].agentId, model[4].agentId];
    })
    .toEqual([11, 12, 11, 12]);
  // Still one call after everything landed — no per-cell round trips.
  await expect(resolverCalls(page)).toHaveText('1');
});

test('pending cells spin, the status bar counts them, and the grid stays interactive', async ({
  page,
}) => {
  await setResolverDelay(page, 1500);
  await activateCell(page, 1, 6);
  await syntheticPaste(page, { text: 'Alice Green\r\nBob Stone\r\nDana Reed\r\nAlice Green\r\n' });

  // The §10 pending affordance: inline spinners + the status-bar tally.
  await expect(cell(page, 1, 6).locator('.tm-grid__cell-spin')).toBeVisible();
  await expect(cell(page, 2, 6).locator('.tm-grid__cell-spin')).toBeVisible();
  await expect(pendingStatus(page)).toHaveText('4 cells resolving');

  // The grid stays fully interactive while resolutions are in flight.
  await page.keyboard.press('ArrowLeft');
  await expect(activeCell(page)).toHaveAttribute('data-col', '5');
  await expect(activeCell(page)).toHaveAttribute('data-row', '1');

  // Resolution lands: values written, spinners and tally gone.
  await expect
    .poll(async () => {
      const model = await lines(page);
      return [model[1].agentId, model[2].agentId, model[3].agentId, model[4].agentId];
    })
    .toEqual([11, 12, 16, 11]);
  await expect(pendingStatus(page)).toHaveCount(0);
  await expect(cell(page, 1, 6).locator('.tm-grid__cell-spin')).toHaveCount(0);
});

test('notFound and ambiguous labels become invalid inputs with distinct messages', async ({
  page,
}) => {
  await setResolverDelay(page, 0);
  await activateCell(page, 1, 6);
  await syntheticPaste(page, { text: 'Nobody Real\r\nAdam Brown\r\n' });

  // Both outcomes: raw label stays visible in place, error-tinted, model
  // cleared (§10) — never a stale or guessed id.
  await expect(cell(page, 1, 6)).toHaveClass(/tm-grid__cell--error/);
  await expect(cell(page, 2, 6)).toHaveClass(/tm-grid__cell--error/);
  expect(await cellText(page, 1, 6)).toBe('Nobody Real');
  expect(await cellText(page, 2, 6)).toBe('Adam Brown');
  const model = await lines(page);
  expect(model[1].agentId).toBeNull();
  expect(model[2].agentId).toBeNull();

  // Activating each cell surfaces its own localized message (§9.4).
  await activateCell(page, 1, 6);
  await expect(errorOverlay(page)).toContainText('No Agent named');
  await expect(errorOverlay(page)).toContainText('Nobody Real');

  await activateCell(page, 2, 6);
  await expect(errorOverlay(page)).toContainText('matches more than one Agent');
});

test('the whole paste — including async resolutions — is ONE undo op', async ({ page }) => {
  await setResolverDelay(page, 0);
  const before = await lines(page);

  await activateCell(page, 1, 6);
  await syntheticPaste(page, { text: 'Alice Green\r\nNobody Real\r\n' });
  await expect
    .poll(async () => (await lines(page))[1].agentId)
    .toBe(11);
  await expect(cell(page, 2, 6)).toHaveClass(/tm-grid__cell--error/);

  await pressUndo(page);
  await expect
    .poll(async () => JSON.stringify(await lines(page)))
    .toBe(JSON.stringify(before));
  await expect(cell(page, 2, 6)).not.toHaveClass(/tm-grid__cell--error/);
});

test('a value typed into a pending cell survives the late resolution', async ({ page }) => {
  await setResolverDelay(page, 1500);
  await activateCell(page, 1, 6);
  await syntheticPaste(page, { text: 'Alice Green\r\nAlice Green\r\n' });
  await expect(cell(page, 1, 6).locator('.tm-grid__cell-spin')).toBeVisible();

  // Type-to-edit into the still-pending anchor cell: 'D' seeds the story's
  // agent select to 'Dana Reed' (id 16); Enter commits — a LATER write that
  // bumps the cell's sequence token (§9.4 interleaving guard).
  await page.keyboard.press('D');
  await expect(editor(page)).toBeVisible();
  await page.keyboard.press('Enter');
  await expect.poll(async () => (await lines(page))[1].agentId).toBe(16);

  // The other cell resolves normally; the edited cell keeps the user's
  // value — the late resolver result for it is discarded.
  await expect.poll(async () => (await lines(page))[2].agentId).toBe(11);
  expect((await lines(page))[1].agentId).toBe(16);
  await expect(pendingStatus(page)).toHaveCount(0);
});

test('undo during pending cancels the resolutions and restores pre-paste state', async ({
  page,
}) => {
  await setResolverDelay(page, 1500);
  const before = await lines(page);

  await activateCell(page, 1, 6);
  await syntheticPaste(page, { text: 'Alice Green\r\nBob Stone\r\n' });
  await expect(pendingStatus(page)).toHaveText('2 cells resolving');

  await pressUndo(page);

  // Pre-paste state is back and the pending tally is zero — the aborted
  // resolutions can never land (§9.4).
  await expect(pendingStatus(page)).toHaveCount(0);
  await expect(cell(page, 1, 6).locator('.tm-grid__cell-spin')).toHaveCount(0);
  await expect
    .poll(async () => JSON.stringify(await lines(page)))
    .toBe(JSON.stringify(before));
});
