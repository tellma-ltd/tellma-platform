// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import {
  cell,
  cellText,
  gotoGrid,
  liveRegion,
  readClipboard,
  rowHeader,
  syntheticCopy,
} from '../support/grid';

/**
 * Copy formats (spec 0004 §9.2). Two layers:
 *
 * - The REAL clipboard group (Chromium, granted permissions) proves the
 *   native Ctrl+C path end to end: TSV fidelity via `text/plain` (which the
 *   async read API returns verbatim) and dual-flavor presence. Chromium
 *   SANITIZES `text/html` on `navigator.clipboard.read()` — custom `data-*`
 *   attributes may be stripped by the reader — so metadata assertions live
 *   in the synthetic group instead.
 * - The synthetic group (tagged @cross-engine: it needs no system clipboard
 *   or permissions, so the future firefox/webkit projects run it as-is)
 *   dispatches a `ClipboardEvent('copy')` with a DataTransfer and asserts
 *   the exact flavor contents the grid wrote.
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

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-readonly');
});

test.describe('real system clipboard (Chromium permissions)', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

  test('a 2×2 copy writes spreadsheet TSV: CRLF rows and a trailing CRLF', async ({ page }) => {
    const texts = await selectTwoByTwo(page);
    await page.keyboard.press('Control+c');

    const { text } = await readClipboard(page);
    expect(text).toBe(
      `${texts[0][0]}\t${texts[0][1]}\r\n${texts[1][0]}\t${texts[1][1]}\r\n`,
    );
    await expect(liveRegion(page)).toContainText('4 cells copied');
  });

  test('both flavors always land: text/html rides alongside the TSV', async ({ page }) => {
    await selectTwoByTwo(page);
    await page.keyboard.press('Control+c');

    const { text, html } = await readClipboard(page);
    expect(text.length).toBeGreaterThan(0);
    expect(html).toContain('<table');
    expect(html).toContain('<td');
  });

  test('boolean cells copy the spreadsheet TRUE/FALSE literals', async ({ page }) => {
    const displayed = await cellText(page, 1, 4); // Active column, boolean
    expect(displayed).toMatch(/^(TRUE|FALSE)$/);

    await cell(page, 1, 4).click();
    await page.keyboard.press('Control+c');

    const { text } = await readClipboard(page);
    expect(text).toBe(`${displayed}\r\n`);
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

    const { text } = await readClipboard(page);
    expect(text).toBe(`${displayed}\r\n`);
    expect(text).toMatch(/\d{1,3},\d{3}/); // localized en-US grouping, not a raw number
  });

  test('two Ctrl-selected full rows compact into one aligned copy', async ({ page }) => {
    await rowHeader(page, 1).click();
    await rowHeader(page, 3).click({ modifiers: ['Control'] });
    await page.keyboard.press('Control+c');

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
