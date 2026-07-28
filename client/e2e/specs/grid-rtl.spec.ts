// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { syntheticPaste } from '../support/clipboard';
import {
  activateCell,
  activeCell,
  cell,
  cellText,
  colHeader,
  findBar,
  gotoGrid,
  gridScroller,
  modelJson,
  syntheticCopy,
} from '../support/grid';

/**
 * RTL battery (spec 0004 §15, DoD 12): logical column order mirrors,
 * physical arrows map to inline directions in the engine, deliberate
 * physical alignment stays physical, tree indentation and the find bar's
 * inline-end anchoring mirror, the clipboard TSV stays in logical column
 * order, and a live dir flip keeps the grid coherent.
 */

test.describe('mirrored layout', () => {
  test('the first column renders at the inline start — the RIGHT side in RTL', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });

    const first = (await colHeader(page, 0).boundingBox())!;
    const second = (await colHeader(page, 1).boundingBox())!;
    expect(first.x).toBeGreaterThan(second.x);

    // Cells mirror with their headers.
    const cellFirst = (await cell(page, 0, 0).boundingBox())!;
    const cellSecond = (await cell(page, 0, 1).boundingBox())!;
    expect(cellFirst.x).toBeGreaterThan(cellSecond.x);
  });

  test('number columns keep their deliberate physical right alignment', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });

    const align = await cell(page, 0, 2).evaluate((el) => getComputedStyle(el).textAlign);
    expect(align).toBe('right');
  });
});

test.describe('direction-mapped arrows (§15)', () => {
  test('ArrowLeft moves toward inline-end (a HIGHER data-col) in RTL', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });
    await cell(page, 2, 1).click();

    await page.keyboard.press('ArrowLeft'); // physically left = logically forward
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
    await page.keyboard.press('ArrowLeft');
    await expect(activeCell(page)).toHaveAttribute('data-col', '3');

    await page.keyboard.press('ArrowRight'); // physically right = logically back
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
  });

  test('Home is the logical row start: data-col 0, sitting at the right edge', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-readonly', { dir: 'rtl' });
    await cell(page, 2, 3).click();

    await page.keyboard.press('Home');
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');

    const home = (await cell(page, 2, 0).boundingBox())!;
    const next = (await cell(page, 2, 1).boundingBox())!;
    expect(home.x).toBeGreaterThan(next.x); // logical start = physical right
  });
});

test.describe('tree indentation (§15)', () => {
  /** The fixed-size twisty slot inside a row's hierarchy cell. */
  function twisty(page: Page, row: number): Locator {
    return cell(page, row, 0).locator('.tm-grid__twisty');
  }

  test('the hierarchy indent pads the inline start — deeper rows shift LEFT from the right edge in RTL', async ({
    page,
  }) => {
    // Rows 0/1 of the expanded seed: 'Assets' (level 1) and its child
    // 'Current assets' (level 2). Both hierarchy cells span the same
    // column, so the twisty offsets isolate the indent geometry.
    await gotoGrid(page, 'tree-grid', { dir: 'rtl' });
    const rootCell = (await cell(page, 0, 0).boundingBox())!;
    const rootTwisty = (await twisty(page, 0).boundingBox())!;
    const childTwisty = (await twisty(page, 1).boundingBox())!;

    // Inline-start = the RIGHT side: the deeper row's affordance sits one
    // indent step further from the cell's right edge…
    const rightEdge = rootCell.x + rootCell.width;
    const rootInset = rightEdge - (rootTwisty.x + rootTwisty.width);
    const childInset = rightEdge - (childTwisty.x + childTwisty.width);
    expect(childInset).toBeGreaterThan(rootInset + 8);

    // …which mirrors the LTR geometry, where the same indent grows from
    // the LEFT edge instead.
    await gotoGrid(page, 'tree-grid');
    const rootCellLtr = (await cell(page, 0, 0).boundingBox())!;
    const rootTwistyLtr = (await twisty(page, 0).boundingBox())!;
    const childTwistyLtr = (await twisty(page, 1).boundingBox())!;
    expect(childTwistyLtr.x - rootCellLtr.x).toBeGreaterThan(rootTwistyLtr.x - rootCellLtr.x + 8);
  });
});

test.describe('find bar corner (§15)', () => {
  test('the find bar floats at the inline-end corner — visually LEFT in RTL', async ({ page }) => {
    await gotoGrid(page, 'grid-list-screen', { dir: 'rtl' });
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    await expect(findBar(page)).toBeVisible();

    const grid = (await gridScroller(page).boundingBox())!;
    const bar = (await findBar(page).boundingBox())!;
    expect(bar.x + bar.width / 2).toBeLessThan(grid.x + grid.width / 2);

    // The LTR contrast: the same inline-end anchor is the RIGHT corner.
    await gotoGrid(page, 'grid-list-screen');
    await activateCell(page, 0, 0);
    await page.keyboard.press('Control+f');
    await expect(findBar(page)).toBeVisible();

    const gridLtr = (await gridScroller(page).boundingBox())!;
    const barLtr = (await findBar(page).boundingBox())!;
    expect(barLtr.x + barLtr.width / 2).toBeGreaterThan(gridLtr.x + gridLtr.width / 2);
  });
});

test.describe('clipboard round-trip (DoD 12)', () => {
  interface Line {
    readonly description: string | null;
    readonly quantity: number | null;
  }

  test('copy exports logical column order under RTL — byte-identical to LTR — and paste lands logically', async ({
    page,
  }) => {
    // The LTR export of rows 1–2 × columns 0–1 (description, quantity)…
    await gotoGrid(page, 'grid-editable');
    await activateCell(page, 1, 0);
    await cell(page, 2, 1).click({ modifiers: ['Shift'] });
    const ltr = await syntheticCopy(page);

    // …and the same range under dir=rtl.
    await gotoGrid(page, 'grid-editable', { dir: 'rtl' });
    const d1 = await cellText(page, 1, 0);
    const q1 = await cellText(page, 1, 1);
    const d2 = await cellText(page, 2, 0);
    const q2 = await cellText(page, 2, 1);
    await activateCell(page, 1, 0);
    await cell(page, 2, 1).click({ modifiers: ['Shift'] });
    const rtl = await syntheticCopy(page);

    // Each TSV record leads with the first LOGICAL column, mirroring never
    // reorders the export, and the bytes match the LTR copy exactly.
    expect(rtl.text).toBe(`${d1}\t${q1}\r\n${d2}\t${q2}\r\n`);
    expect(rtl.text).toBe(ltr.text);

    // Pasting the block elsewhere lands each value in the SAME logical
    // column: descriptions in `description`, quantities in `quantity`.
    await activateCell(page, 10, 0);
    await syntheticPaste(page, { text: rtl.text });
    await expect
      .poll(async () => (await modelJson<Line[]>(page))[10].description)
      .toBe(d1);
    const lines = await modelJson<Line[]>(page);
    expect(lines[10].quantity).toBe(Number(q1));
    expect(lines[11].description).toBe(d2);
    expect(lines[11].quantity).toBe(Number(q2));
  });
});

test.describe('live dir flip (shell Dir wrapper)', () => {
  test('a runtime flip re-maps arrows and mirrors the template', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly'); // fresh LTR load
    await cell(page, 1, 1).click();
    await page.keyboard.press('ArrowLeft'); // LTR: left = back
    await expect(activeCell(page)).toHaveAttribute('data-col', '0');

    await page.getByTestId('lang-ar').click(); // flip at runtime — no reload
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    // Template mirrored live…
    await expect
      .poll(async () => {
        const first = (await colHeader(page, 0).boundingBox())!;
        const second = (await colHeader(page, 1).boundingBox())!;
        return first.x > second.x;
      })
      .toBe(true);

    // …and the engine's arrow mapping followed the live Directionality.
    await cell(page, 1, 1).click();
    await page.keyboard.press('ArrowLeft'); // now inline-end
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
  });
});
