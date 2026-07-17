// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { pressUndo, readFixture, syntheticPaste } from '../support/clipboard';
import {
  activateCell,
  cell,
  cellText,
  gotoGrid,
  modelJson,
  setScrollTop,
  syntheticCopy,
} from '../support/grid';

/**
 * The paste resolution ladder (spec 0004 §9.3) against the authored
 * clipboard fixtures (`e2e/fixtures/clipboard` — Excel / Google Sheets /
 * Tellma payload shapes). Everything here dispatches a synthetic
 * `ClipboardEvent('paste')` — no OS clipboard, no permissions — so the
 * whole file is tagged @cross-engine and runs on chromium, firefox, and
 * webkit alike.
 *
 * Story: grid-editable. Columns: 0 description (text), 1 quantity (number),
 * 2 unitPrice (number), 3 discount (number, readonly on posted rows),
 * 4 isPosted (boolean), 5 category (enum), 6 agentId (entity + resolver),
 * 7 Total (accessor, never written). Rows 1–4 are not posted, so their
 * discount cells are editable; the story grid's tenant is 't1'.
 */

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

const lines = modelJson<InvoiceLine[]>;

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-editable');
});

test.describe('foreign payloads (Excel / Sheets shapes)', () => {
  test('@cross-engine Excel TSV (CRLF, trailing terminator) pastes into typed cells', async ({
    page,
  }) => {
    await activateCell(page, 1, 1);
    await syntheticPaste(page, { text: readFixture('excel/simple-2x2.txt') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].quantity, model[1].unitPrice, model[2].quantity, model[2].unitPrice];
      })
      .toEqual([3, 4.5, 5, 6.25]);
  });

  test('@cross-engine Excel quoting round-trips byte-for-byte through paste → copy', async ({
    page,
  }) => {
    const fixture = readFixture('excel/quoted-cells.txt');
    await activateCell(page, 1, 0);
    await syntheticPaste(page, { text: fixture });

    // Quoted fields unwrap: embedded tab, bare-LF line break, doubled
    // quotes. The number column rejects the quote cell — §10: the raw text
    // stays visible in place, and copy exports it back as text.
    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].description, model[1].quantity, model[2].description, model[2].quantity];
      })
      .toEqual(['tab\there', 12, 'line\nbreak', null]);
    await expect(cell(page, 2, 1)).toHaveClass(/tm-grid__cell--error/);
    expect(await cellText(page, 2, 1)).toBe('quote "q"');

    // Re-select the pasted 2×2 from the still-active anchor with the
    // keyboard (pointer-free — immune to under-load stability rechecks).
    await page.keyboard.press('Shift+ArrowDown');
    await page.keyboard.press('Shift+ArrowRight');
    const { text } = await syntheticCopy(page);
    expect(text).toBe(fixture); // byte-identical re-serialization
  });

  test('@cross-engine an Excel CF_HTML table pastes its display strings', async ({ page }) => {
    await activateCell(page, 1, 1);
    await syntheticPaste(page, { html: readFixture('excel/simple-2x2.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].quantity, model[1].unitPrice, model[2].quantity, model[2].unitPrice];
      })
      .toEqual([3, 4.5, 5, 6.25]);
  });

  test('@cross-engine German-locale Excel TSV has no locale hint — an en grid misparses it', async ({
    page,
  }) => {
    await activateCell(page, 1, 1);
    // Plain TSV carries no source-locale metadata, so the 'en' grid parses
    // '1.234,56' by its own rules ('.' decimal, ',' grouping) → 1.23456, not
    // 1234.56 (and '2.500,75' → 2.50075). Recovering the German values needs
    // the HTML metadata path (tellma/numbers-de.html), covered below.
    await syntheticPaste(page, { text: readFixture('excel/numbers-de.txt') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].quantity, model[1].unitPrice];
      })
      .toEqual([1.23456, 2.50075]);
  });

  test('@cross-engine Sheets HTML pastes the display text and IGNORES data-sheets-value', async ({
    page,
  }) => {
    await activateCell(page, 1, 1);
    // The fixture's data-sheets-value JSON says 999/888/777 — the visible
    // text says 7/8.25/9/10. The display text must win (§9.3).
    await syntheticPaste(page, { html: readFixture('sheets/simple-2x2.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].quantity, model[1].unitPrice, model[2].quantity, model[2].unitPrice];
      })
      .toEqual([7, 8.25, 9, 10]);
  });
});

test.describe('header-row content heuristic (§9.3)', () => {
  test('@cross-engine a leading row matching the target headers is skipped', async ({ page }) => {
    const before = await lines(page);
    await activateCell(page, 1, 0);
    // Anchored at column 0, the fixture's first row ('Description' | 'Qty')
    // matches both target headers position-wise → treated as headers.
    await syntheticPaste(page, { html: readFixture('excel/headers-roundtrip.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].description, model[1].quantity, model[2].description, model[2].quantity];
      })
      .toEqual(['Paste A', 3, 'Paste B', 4]);
    // Only the two DATA rows landed — row 3 was never touched.
    expect((await lines(page))[3].description).toBe(before[3].description);
  });

  test('@cross-engine the same payload at non-matching columns keeps its first row', async ({
    page,
  }) => {
    await activateCell(page, 1, 2);
    // Anchored at unitPrice/discount, 'Description' ≠ 'Unit price' — one
    // non-empty mismatch disproves the header row, so all THREE rows land.
    await syntheticPaste(page, { html: readFixture('excel/headers-roundtrip.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[2].discount, model[3].discount];
      })
      .toEqual([3, 4]); // data rows shifted DOWN one — the first row landed
    expect(await cellText(page, 1, 2)).toBe('Description'); // as an invalid input
    await expect(cell(page, 1, 2)).toHaveClass(/tm-grid__cell--error/);
  });

  test('@cross-engine a single-column payload equal to a header is data, not a header', async ({
    page,
  }) => {
    await activateCell(page, 1, 1);
    // 'Qty' equals the quantity header, but single-column pastes never
    // trigger the heuristic (§9.3) — the cell becomes an invalid input.
    await syntheticPaste(page, { text: 'Qty\r\n5\r\n' });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].quantity, model[2].quantity];
      })
      .toEqual([null, 5]);
    expect(await cellText(page, 1, 1)).toBe('Qty');
    await expect(cell(page, 1, 1)).toHaveClass(/tm-grid__cell--error/);
  });
});

test.describe('Tellma metadata payloads (§9.2 → §9.3)', () => {
  test('@cross-engine same-tenant typed metadata pastes raw values without parsing', async ({
    page,
  }) => {
    await activateCell(page, 1, 1);
    // The fixture's first cell displays '1,5' with raw 1.5: an 'en' parse
    // would yield 15, so only the typed fast path produces 1.5.
    await syntheticPaste(page, { html: readFixture('tellma/typed-2x3.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [
          model[1].quantity,
          model[1].unitPrice,
          model[1].discount,
          model[2].quantity,
          model[2].unitPrice,
          model[2].discount,
        ];
      })
      .toEqual([1.5, 200, 10, 3, 4.5, 0]);
  });

  test('@cross-engine a full-row payload writes every column typed — entity ids skip the resolver', async ({
    page,
  }) => {
    const before = await lines(page);
    await activateCell(page, 1, 0);
    await syntheticPaste(page, { html: readFixture('tellma/full-rows.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [
          model[1].description,
          model[1].quantity,
          model[1].unitPrice,
          model[1].discount,
          model[1].isPosted,
          model[1].category,
          model[1].agentId,
          model[2].description,
          model[2].agentId,
        ];
      })
      .toEqual(['Moved A', 2, 3.5, 1, true, 'goods', 11, 'Moved B', 12]);
    // Same tenant + typed entity values: the resolver never ran.
    await expect(page.getByTestId('resolver-calls')).toHaveText('0');
    // A plain paste of a full-row payload (no armed cut) writes cells — it
    // does not move rows.
    expect((await lines(page)).map((line) => line.id)).toEqual(before.map((line) => line.id));
  });

  test('@cross-engine a marked header row (thead + headers flag) is skipped', async ({ page }) => {
    const before = await lines(page);
    await activateCell(page, 3, 1);
    await syntheticPaste(page, { html: readFixture('tellma/with-headers.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[3].quantity, model[3].unitPrice, model[4].quantity, model[4].unitPrice];
      })
      .toEqual([11, 12.5, 13, 14.25]);
    // Two data rows only — 'Qty'/'Unit price' never landed anywhere.
    expect((await lines(page))[5].quantity).toBe(before[5].quantity);
  });

  test('@cross-engine the metadata locale drives number parsing (German source)', async ({
    page,
  }) => {
    await activateCell(page, 1, 1);
    // locale:"de" in data-tm-grid, no raw values: '1.234,56' must parse via
    // the sourceLocale hint (an 'en' parse would yield 1.23456 and 25).
    await syntheticPaste(page, { html: readFixture('tellma/numbers-de.html') });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].quantity, model[2].quantity];
      })
      .toEqual([1234.56, 2.5]);
  });

  test('@cross-engine cross-tenant raw entity ids are refused — labels re-resolve', async ({
    page,
  }) => {
    await activateCell(page, 1, 6);
    // Tenant t2 payload: raw ids 13/16 are WRONG for the labels. Trusting
    // them would write 13/16; re-resolving writes 11/12 (§9.4).
    await syntheticPaste(page, { html: readFixture('tellma/cross-tenant.html') });

    await expect(page.getByTestId('resolver-calls')).toHaveText('1');
    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].agentId, model[2].agentId];
      })
      .toEqual([11, 12]);
  });
});

test.describe('target shaping (§9.3)', () => {
  test('@cross-engine a single value fills every cell of the selection', async ({ page }) => {
    await cell(page, 1, 1).click();
    await cell(page, 3, 2).click({ modifiers: ['Shift'] }); // 3×2 selection
    await syntheticPaste(page, { text: '7\r\n' });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [1, 2, 3].flatMap((row) => [model[row].quantity, model[row].unitPrice]);
      })
      .toEqual([7, 7, 7, 7, 7, 7]);
  });

  test('@cross-engine a source tiles a selection that is an exact multiple of its shape', async ({
    page,
  }) => {
    await cell(page, 1, 1).click();
    await cell(page, 4, 1).click({ modifiers: ['Shift'] }); // 4×1 selection
    await syntheticPaste(page, { text: '5\r\n6\r\n' }); // 2×1 source

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model[1].quantity, model[2].quantity, model[3].quantity, model[4].quantity];
      })
      .toEqual([5, 6, 5, 6]);
  });

  test('@cross-engine overflow past the last row materializes rows; ONE undo removes them', async ({
    page,
  }) => {
    const before = await lines(page);
    expect(before).toHaveLength(40);

    await setScrollTop(page, 100_000); // bring the last data row into view
    await activateCell(page, 39, 1);
    await syntheticPaste(page, { text: '100\r\n200\r\n300\r\n' });

    await expect
      .poll(async () => {
        const model = await lines(page);
        return [model.length, model[39].quantity, model[40]?.quantity, model[41]?.quantity];
      })
      .toEqual([42, 100, 200, 300]);
    // Overflow rows are minted by the newRow factory (negative temp ids).
    expect((await lines(page))[40].id).toBeLessThan(0);

    await pressUndo(page); // ONE undo: the writes AND the materialized rows
    await expect
      .poll(async () => JSON.stringify(await lines(page)))
      .toBe(JSON.stringify(before));
  });
});
