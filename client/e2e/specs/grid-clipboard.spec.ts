// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { pressUndo, seedClipboard } from '../support/clipboard';
import { useExclusiveClipboard } from '../support/clipboard-lock';
import {
  activateCell,
  cell,
  cellText,
  gotoGrid,
  liveRegion,
  modelJson,
  readClipboard,
  rowHeader,
  syntheticCopy,
  waitForClipboardWrite,
} from '../support/grid';

/**
 * Copy formats (spec 0004 §9.2) and the real-clipboard paste/cut round
 * trips (§9.3–§9.6). Three layers:
 *
 * - The REAL clipboard copy group (Chromium, granted permissions, readonly
 *   story) proves the native Ctrl+C path end to end: TSV fidelity via
 *   `text/plain` (which the async read API returns verbatim) and
 *   dual-flavor presence. Chromium SANITIZES `text/html` on
 *   `navigator.clipboard.read()` — custom `data-*` attributes may be
 *   stripped by the reader — so metadata assertions live in the synthetic
 *   group instead.
 * - The synthetic group (tagged @cross-engine: it needs no system clipboard
 *   or permissions, so the firefox/webkit projects run it as-is) dispatches
 *   a `ClipboardEvent('copy')` with a DataTransfer and asserts the exact
 *   flavor contents the grid wrote.
 * - The round-trip group (Chromium, editable story) drives the OS clipboard
 *   through real Ctrl+C/X/V: same-grid typed paste, the deferred cut-move
 *   with its marquee, the full-row move, the menu paste path with its §8.5
 *   degradation, and the §9.1 copy-failure transient notice. Fixture-driven
 *   foreign-payload pastes live in grid-clipboard-synthetic.spec.ts.
 */

/** Unescapes the HTML attribute encoding used by the grid's serializer. */
function unescapeAttribute(value: string): string {
  return value
    .replaceAll('&quot;', '"')
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&amp;', '&');
}

/** The `data-tm-grid` metadata parsed out of a copied HTML flavor. */
function parseGridMeta(html: string): {
  v: number;
  locale?: string;
  cols?: ReadonlyArray<{ key: string | null; type: string }>;
} {
  const match = /data-tm-grid="([^"]*)"/.exec(html);
  expect(match, 'the HTML flavor must carry data-tm-grid').not.toBeNull();
  return JSON.parse(unescapeAttribute(match![1])) as {
    v: number;
    locale?: string;
    cols?: ReadonlyArray<{ key: string | null; type: string }>;
  };
}

/** Selects the 2×2 qty×price block at rows 1–2 and returns its display texts. */
async function selectTwoByTwo(page: Page): Promise<string[][]> {
  const texts = [
    [await cellText(page, 1, 2), await cellText(page, 1, 3)],
    [await cellText(page, 2, 2), await cellText(page, 2, 3)],
  ];
  await cell(page, 1, 2).click();
  await cell(page, 2, 3).click({ modifiers: ['Shift'] });
  return texts;
}

test.describe('real system clipboard (Chromium permissions)', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });
  useExclusiveClipboard();

  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
  });

  test('a 2×2 copy writes spreadsheet TSV: CRLF rows and a trailing CRLF', async ({ page }) => {
    const texts = await selectTwoByTwo(page);
    await page.keyboard.press('Control+c');

    // Retrying read: the clipboard write is async, so a one-shot read can
    // beat it to the payload under CI load.
    await expect
      .poll(async () => (await readClipboard(page)).text)
      .toBe(`${texts[0][0]}\t${texts[0][1]}\r\n${texts[1][0]}\t${texts[1][1]}\r\n`);
    await expect(liveRegion(page)).toContainText('4 cells copied');
  });

  test('both flavors always land: text/html rides alongside the TSV', async ({ page }) => {
    await selectTwoByTwo(page);
    await page.keyboard.press('Control+c');

    await expect.poll(async () => (await readClipboard(page)).html).toContain('<table');
    const { text, html } = await readClipboard(page);
    expect(text.length).toBeGreaterThan(0);
    expect(html).toContain('<td');
  });

  test('boolean cells copy the spreadsheet TRUE/FALSE literals', async ({ page }) => {
    const displayed = await cellText(page, 1, 4); // Active column, boolean
    expect(displayed).toMatch(/^(TRUE|FALSE)$/);

    await cell(page, 1, 4).click();
    await page.keyboard.press('Control+c');

    await expect.poll(async () => (await readClipboard(page)).text).toBe(`${displayed}\r\n`);
  });

  test('number cells copy the localized display string, grouping included', async ({ page }) => {
    // Find a rendered Total (accessor, number) big enough to carry a group
    // separator — the dataset is seeded, so one always exists near the top.
    let row = -1;
    let displayed = '';
    for (let candidate = 0; candidate < 14; candidate++) {
      const text = await cellText(page, candidate, 10);
      if (text.includes(',')) {
        row = candidate;
        displayed = text;
        break;
      }
    }
    expect(row, 'expected a grouped Total among the first rows').toBeGreaterThanOrEqual(0);

    await cell(page, row, 10).click();
    await page.keyboard.press('Control+c');

    await expect.poll(async () => (await readClipboard(page)).text).toBe(`${displayed}\r\n`);
    expect(displayed).toMatch(/\d{1,3},\d{3}/); // localized en-US grouping, not a raw number
  });

  test('two Ctrl-selected full rows compact into one aligned copy', async ({ page }) => {
    const clipboardBefore = (await readClipboard(page)).text;
    await rowHeader(page, 1).click();
    await rowHeader(page, 3).click({ modifiers: ['Control'] });
    await page.keyboard.press('Control+c');

    await waitForClipboardWrite(page, clipboardBefore);
    const { text } = await readClipboard(page);
    expect(text.endsWith('\r\n')).toBe(true);
    const lines = text.slice(0, -2).split('\r\n');
    expect(lines).toHaveLength(2); // compacted: both rows, nothing between
    expect(lines[0].split('\t')).toHaveLength(12);
    expect(lines[1].split('\t')).toHaveLength(12);
    expect(lines[0].startsWith('PRD-1\t')).toBe(true);
    expect(lines[1].startsWith('PRD-3\t')).toBe(true);
  });

  test('a misaligned multi-range copy is refused, announced, and writes nothing', async ({
    page,
  }) => {
    await page.evaluate(() => navigator.clipboard.writeText('sentinel'));

    await cell(page, 1, 1).click();
    await cell(page, 3, 4).click({ modifiers: ['Control'] }); // shares neither span
    await page.keyboard.press('Control+c');

    // grid.announce.copyRefused, resolved through the active locale.
    await expect(liveRegion(page)).toContainText(
      'Cannot copy a multi-range selection of this shape',
    );
    const { text } = await readClipboard(page);
    expect(text).toBe('sentinel'); // the refused copy left the clipboard alone
  });
});

test.describe('flavor fidelity via synthetic ClipboardEvent', () => {
  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
  });

  test('@cross-engine the TSV and HTML flavors carry the §9.2 contract', async ({ page }) => {
    const texts = await selectTwoByTwo(page);
    const { text, html } = await syntheticCopy(page);

    expect(text).toBe(
      `${texts[0][0]}\t${texts[0][1]}\r\n${texts[1][0]}\t${texts[1][1]}\r\n`,
    );

    const meta = parseGridMeta(html);
    expect(meta.v).toBe(1);
    expect(meta.locale).toBeTruthy();
    expect(meta.cols).toEqual([
      { key: 'qty', type: 'number' },
      { key: 'price', type: 'number' },
    ]);
    // Raw typed values ride per cell: one data-tm-v per copied number cell.
    expect(html.match(/data-tm-v="/g)).toHaveLength(4);
    // No header row by default (Excel parity).
    expect(html).not.toContain('<thead>');
  });

  test('@cross-engine full-row copies stamp data-tm-rowid per row', async ({ page }) => {
    await rowHeader(page, 1).click();
    await rowHeader(page, 3).click({ modifiers: ['Control'] });

    const { text, html } = await syntheticCopy(page);
    expect(text.slice(0, -2).split('\r\n')).toHaveLength(2);
    expect(html).toContain('data-tm-rowid="1"');
    expect(html).toContain('data-tm-rowid="3"');
  });

  test('@cross-engine a cell-range copy carries no row identities', async ({ page }) => {
    await selectTwoByTwo(page);
    const { html } = await syntheticCopy(page);
    expect(html).not.toContain('data-tm-rowid'); // §9.2: full-row copies only
  });

  test('@cross-engine boolean cells serialize TRUE/FALSE with a raw JSON value', async ({
    page,
  }) => {
    const displayed = await cellText(page, 1, 4);
    await cell(page, 1, 4).click();

    const { text, html } = await syntheticCopy(page);
    expect(text).toBe(`${displayed}\r\n`);
    expect(html).toMatch(/data-tm-v="(true|false)"/);
  });
});

/** The editable story's row shape (the model-json dump the specs assert). */
interface InvoiceLine {
  readonly id: number;
  readonly description: string | null;
  readonly quantity: number | null;
  readonly unitPrice: number | null;
  readonly discount: number | null;
  readonly isPosted: boolean;
  readonly category: string | null;
  readonly agentId: number | null;
}

function menuPanel(page: Page) {
  return page.locator('.tm-menu__panel');
}

function menuItem(page: Page, name: string) {
  return page.getByRole('menuitem', { name, exact: true });
}

/** Polls the model dump until it deep-equals `expected`. */
async function expectModel(page: Page, expected: readonly InvoiceLine[]): Promise<void> {
  await expect
    .poll(async () => JSON.stringify(await modelJson<InvoiceLine[]>(page)))
    .toBe(JSON.stringify(expected));
}

test.describe('paste, cut & menu round-trips (real system clipboard, editable)', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });
  useExclusiveClipboard();

  test.beforeEach(async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
  });

  test('Tellma→Tellma same-grid paste writes typed values; ONE undo reverts it', async ({
    page,
  }) => {
    const before = await modelJson<InvoiceLine[]>(page);
    const clipboardBefore = (await readClipboard(page)).text;

    // Copy the 2×2 qty × unit-price block of rows 1–2…
    await cell(page, 1, 1).click();
    await cell(page, 2, 2).click({ modifiers: ['Shift'] });
    await page.keyboard.press('Control+c');
    await expect(liveRegion(page)).toContainText('4 cells copied');
    await waitForClipboardWrite(page, clipboardBefore);

    // …and paste it at rows 5–6 of the same columns.
    await activateCell(page, 5, 1);
    await page.keyboard.press('Control+v');

    await expect
      .poll(async () => {
        const lines = await modelJson<InvoiceLine[]>(page);
        return [lines[5].quantity, lines[5].unitPrice, lines[6].quantity, lines[6].unitPrice];
      })
      .toEqual([
        before[1].quantity,
        before[1].unitPrice,
        before[2].quantity,
        before[2].unitPrice,
      ]);

    // The whole paste is ONE undo op (§9.3).
    await pressUndo(page);
    await expectModel(page, before);
  });

  test('cut arms the marquee; paste moves the cells; ONE undo restores both ends', async ({
    page,
  }) => {
    const before = await modelJson<InvoiceLine[]>(page);
    const clipboardBefore = (await readClipboard(page)).text;

    await cell(page, 1, 1).click();
    await cell(page, 2, 2).click({ modifiers: ['Shift'] });
    await page.keyboard.press('Control+x');

    // The deferred move is armed: marching-ants marquee on the source cells,
    // nothing moved yet (§9.5).
    await expect(cell(page, 1, 1)).toHaveClass(/tm-grid__cell--cut/);
    await expect(cell(page, 2, 2)).toHaveClass(/tm-grid__cell--cut/);
    expect(JSON.stringify(await modelJson<InvoiceLine[]>(page))).toBe(JSON.stringify(before));
    await waitForClipboardWrite(page, clipboardBefore);

    await activateCell(page, 10, 1);
    await page.keyboard.press('Control+v');

    await expect
      .poll(async () => {
        const lines = await modelJson<InvoiceLine[]>(page);
        return [
          lines[10].quantity,
          lines[11].unitPrice,
          lines[1].quantity,
          lines[1].unitPrice,
          lines[2].quantity,
          lines[2].unitPrice,
        ];
      })
      .toEqual([before[1].quantity, before[2].unitPrice, null, null, null, null]);
    await expect(cell(page, 1, 1)).not.toHaveClass(/tm-grid__cell--cut/);

    // ONE undo restores the source values AND removes the target writes.
    await pressUndo(page);
    await expectModel(page, before);
  });

  test('Esc disarms the cut marquee; a later paste is a plain copy', async ({ page }) => {
    const before = await modelJson<InvoiceLine[]>(page);
    const clipboardBefore = (await readClipboard(page)).text;

    await cell(page, 1, 1).click();
    await page.keyboard.press('Control+x');
    await expect(cell(page, 1, 1)).toHaveClass(/tm-grid__cell--cut/);
    await waitForClipboardWrite(page, clipboardBefore);

    await page.keyboard.press('Escape');
    await expect(cell(page, 1, 1)).not.toHaveClass(/tm-grid__cell--cut/);
    expect(JSON.stringify(await modelJson<InvoiceLine[]>(page))).toBe(JSON.stringify(before));

    // The clipboard still holds the payload; pasting now copies — the
    // source cell keeps its value.
    await activateCell(page, 5, 1);
    await page.keyboard.press('Control+v');
    await expect
      .poll(async () => (await modelJson<InvoiceLine[]>(page))[5].quantity)
      .toBe(before[1].quantity);
    expect((await modelJson<InvoiceLine[]>(page))[1].quantity).toBe(before[1].quantity);
  });

  test('a full-row cut pasted in the same grid MOVES the row; one undo restores the order', async ({
    page,
  }) => {
    const before = await modelJson<InvoiceLine[]>(page);
    const beforeIds = before.map((line) => line.id);

    const clipboardBefore = (await readClipboard(page)).text;
    await rowHeader(page, 1).click(); // full row 1 (id 2)
    await page.keyboard.press('Control+x');
    await expect(cell(page, 1, 0)).toHaveClass(/tm-grid__cell--cut/);
    // The move needs the cut payload on the clipboard: wait out the async
    // write, else the paste reads stale content and lands as a value paste.
    await waitForClipboardWrite(page, clipboardBefore);

    await activateCell(page, 5, 0); // row 5 holds id 6
    await page.keyboard.press('Control+v');

    // The row was re-inserted above the paste target (§9.6) — a move, not a
    // write: values travel with the row identity.
    const movedIds = [...beforeIds];
    movedIds.splice(1, 1); // id 2 leaves position 1…
    movedIds.splice(4, 0, 2); // …and lands above id 6
    await expect
      .poll(async () => (await modelJson<InvoiceLine[]>(page)).map((line) => line.id))
      .toEqual(movedIds);
    await expect(liveRegion(page)).toContainText('1 row moved');

    await pressUndo(page);
    await expectModel(page, before);
  });

  test('a rejected async clipboard write surfaces the transient failure notice', async ({
    page,
  }) => {
    // Menu copies ride navigator.clipboard.write (§9.1); reject it before
    // the app boots so the failure path is deterministic.
    await page.addInitScript(() => {
      Object.defineProperty(navigator.clipboard, 'write', {
        configurable: true,
        value: () => Promise.reject(new Error('write denied')),
      });
    });
    await gotoGrid(page, 'grid-editable');

    await activateCell(page, 1, 1);
    await page.keyboard.press('Shift+F10');
    await expect(menuPanel(page)).toBeVisible();
    await menuItem(page, 'Copy').click();

    // A failed copy is never silent (§9.1): the localized notice appears on
    // the status surface and clears on its own (~6s; generous ceiling).
    const notice = page.locator('[data-tm-status-notice]');
    await expect(notice).toBeVisible();
    await expect(notice).toHaveText('Copy failed — select the cells and copy again');
    await expect(notice).toBeHidden({ timeout: 8000 });
  });

  test('menu Paste reads the async clipboard and pastes', async ({ page }) => {
    await seedClipboard(page, { text: 'MenuPasted\r\n' });

    await cell(page, 3, 0).click({ button: 'right' });
    await expect(menuPanel(page)).toBeVisible();
    await menuItem(page, 'Paste').click();

    await expect
      .poll(async () => (await modelJson<InvoiceLine[]>(page))[3].description)
      .toBe('MenuPasted');
  });

  test('a denied clipboard read degrades menu Paste to the shortcut hint', async ({ page }) => {
    const before = await modelJson<InvoiceLine[]>(page);
    await page.evaluate(() => {
      Object.defineProperty(navigator.clipboard, 'read', {
        configurable: true,
        value: () => Promise.reject(new Error('read denied')),
      });
    });

    await cell(page, 2, 0).click({ button: 'right' });
    await expect(menuPanel(page)).toBeVisible();
    await menuItem(page, 'Paste').click();
    await expect(menuPanel(page)).toHaveCount(0);
    expect(JSON.stringify(await modelJson<InvoiceLine[]>(page))).toBe(JSON.stringify(before));

    // On the next open the item is the disabled keyboard hint (§8.5, the
    // Sheets-established fallback) — per grid instance, not per page.
    await page.keyboard.press('Shift+F10');
    await expect(menuPanel(page)).toBeVisible();
    const hint = menuItem(page, 'Press Ctrl+V to paste');
    await expect(hint).toBeVisible();
    await expect(hint).toHaveAttribute('aria-disabled', 'true');
    await expect(menuItem(page, 'Paste')).toHaveCount(0);
  });
});
