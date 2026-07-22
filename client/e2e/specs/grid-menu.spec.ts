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
  centerOf,
  gotoGrid,
  liveRegion,
  modelJson,
  readClipboard,
  selectedCells,
} from '../support/grid';

/**
 * The grid context menu (spec 0004 §8.5) against the editable story:
 * right-click targeting and pointer placement, the localized built-in
 * items with their row-count pluralization, the row operations through the
 * `newRow` factory, the async-Clipboard copy path (both flavors, the
 * copy-with-headers metadata flag), the disabled Paste item, and the
 * Esc/Shift+F10 keyboard mechanics.
 */

interface Line {
  readonly id: number;
  readonly description: string | null;
}

/** The gap between a point and a rectangle (0 while the point is inside). */
function distanceToBox(
  point: { x: number; y: number },
  box: { x: number; y: number; width: number; height: number },
): number {
  const dx = Math.max(box.x - point.x, 0, point.x - (box.x + box.width));
  const dy = Math.max(box.y - point.y, 0, point.y - (box.y + box.height));
  return Math.hypot(dx, dy);
}

function menuPanel(page: Page) {
  return page.locator('.tm-menu__panel');
}

function menuItem(page: Page, name: string) {
  return page.getByRole('menuitem', { name, exact: true });
}

async function menuLabels(page: Page): Promise<string[]> {
  return menuPanel(page).locator('.tm-menu__label').allInnerTexts();
}

test.beforeEach(async ({ page }) => {
  await gotoGrid(page, 'grid-editable');
});

test.describe('opening', () => {
  test('right-click outside the selection selects the target, then opens at the pointer', async ({
    page,
  }) => {
    await cell(page, 0, 0).click();
    const target = cell(page, 8, 2);
    const point = await centerOf(target);
    await target.click({ button: 'right' });

    // The press target became the (collapsed) selection — Excel behavior.
    await expect(activeCell(page)).toHaveAttribute('data-row', '8');
    await expect(activeCell(page)).toHaveAttribute('data-col', '2');
    await expect(selectedCells(page)).toHaveCount(1);

    const panel = menuPanel(page);
    await expect(panel).toBeVisible();
    const box = await panel.boundingBox();
    expect(box).not.toBeNull();
    // Anchored at the pointer (flexible positioning may flip around it,
    // so measure the gap between the point and the panel's rectangle).
    expect(distanceToBox(point, box!)).toBeLessThan(40);
  });

  test('Shift+F10 opens the menu at the active cell', async ({ page }) => {
    await activateCell(page, 5, 1);
    const cellBox = await cell(page, 5, 1).boundingBox();
    await page.keyboard.press('Shift+F10');

    const panel = menuPanel(page);
    await expect(panel).toBeVisible();
    const box = await panel.boundingBox();
    expect(box).not.toBeNull();
    // Anchored to the cell's rect (either side of it when flipped).
    expect(
      distanceToBox({ x: cellBox!.x + cellBox!.width / 2, y: cellBox!.y + cellBox!.height / 2 }, box!),
    ).toBeLessThan(cellBox!.height + 40);
  });

  test('Esc closes the menu and returns focus to the cell', async ({ page }) => {
    await activateCell(page, 4, 1);
    await page.keyboard.press('Shift+F10');
    await expect(menuPanel(page)).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(menuPanel(page)).toHaveCount(0);
    await expect(cell(page, 4, 1)).toBeFocused();
  });
});

test.describe('items', () => {
  test('the localized built-in items render, and Paste is enabled', async ({ page }) => {
    await activateCell(page, 2, 0);
    await page.keyboard.press('Shift+F10');

    expect(await menuLabels(page)).toEqual([
      'Cut',
      'Copy',
      'Copy with headers',
      'Paste',
      'Insert 1 row above',
      'Insert 1 row below',
      'Delete 1 row',
    ]);
    // Chromium exposes navigator.clipboard.read, so the Paste item rides
    // the async read path and is enabled on an editable grid (§8.5); the
    // denied-read degradation is covered in grid-clipboard.spec.ts.
    await expect(menuItem(page, 'Paste')).toHaveAttribute('aria-disabled', 'false');
    await page.keyboard.press('Escape');
  });

  test('a 3-row selection pluralizes the row items', async ({ page }) => {
    await cell(page, 1, 0).click();
    await cell(page, 3, 0).click({ modifiers: ['Shift'] }); // rows 1–3
    await cell(page, 2, 0).click({ button: 'right' }); // inside the selection: kept

    const labels = await menuLabels(page);
    expect(labels).toContain('Insert 3 rows above');
    expect(labels).toContain('Insert 3 rows below');
    expect(labels).toContain('Delete 3 rows');
    await page.keyboard.press('Escape');
  });
});

test.describe('row operations', () => {
  test('Delete rows removes the selected rows through the field and announces', async ({
    page,
  }) => {
    const before = await modelJson<Line[]>(page);
    expect(before.length).toBe(40);

    await cell(page, 1, 0).click();
    await cell(page, 3, 0).click({ modifiers: ['Shift'] });
    await cell(page, 2, 0).click({ button: 'right' });
    await menuItem(page, 'Delete 3 rows').click();

    await expect.poll(async () => (await modelJson<Line[]>(page)).length).toBe(37);
    const after = await modelJson<Line[]>(page);
    expect(after[0].id).toBe(before[0].id);
    expect(after[1].id).toBe(before[4].id); // rows 1–3 are gone
    await expect(liveRegion(page)).toContainText('3 rows deleted');
  });

  test('Insert row above adds a factory-minted row (negative temp id)', async ({ page }) => {
    await activateCell(page, 2, 0);
    await page.keyboard.press('Shift+F10');
    await menuItem(page, 'Insert 1 row above').click();

    await expect.poll(async () => (await modelJson<Line[]>(page)).length).toBe(41);
    const lines = await modelJson<Line[]>(page);
    expect(lines[2].id).toBe(-1); // minted by the newRow factory
    expect(lines[2].description).toBeNull();
    await expect(liveRegion(page)).toContainText('1 row inserted');
  });
});

test.describe('menu copy (async Clipboard API)', () => {
  test.use({ permissions: ['clipboard-read', 'clipboard-write'] });

  /**
   * Wraps `navigator.clipboard.write` so the test can read the EXACT
   * flavors the grid wrote. Chromium sanitizes `text/html` on
   * `clipboard.read()` (custom attributes may be stripped), so metadata
   * assertions read the captured ClipboardItems instead; the original
   * write still goes through to the real clipboard.
   */
  async function captureClipboardWrites(page: Page): Promise<void> {
    await page.evaluate(() => {
      const w = window as unknown as { __copies: Array<Record<string, string>> };
      w.__copies = [];
      const original = navigator.clipboard.write.bind(navigator.clipboard);
      navigator.clipboard.write = (items: ClipboardItem[]) => {
        const result = original(items);
        void (async () => {
          const record: Record<string, string> = {};
          for (const item of items) {
            for (const type of item.types) {
              record[type] = await (await item.getType(type)).text();
            }
          }
          w.__copies.push(record);
        })();
        return result;
      };
    });
  }

  async function capturedCopies(page: Page): Promise<Array<Record<string, string>>> {
    return page.evaluate(
      () => (window as unknown as { __copies: Array<Record<string, string>> }).__copies,
    );
  }

  test('menu Copy writes both flavors to the real clipboard', async ({ page }) => {
    const texts = [
      [await cellText(page, 1, 0), await cellText(page, 1, 1)],
      [await cellText(page, 2, 0), await cellText(page, 2, 1)],
    ];
    await cell(page, 1, 0).click();
    await cell(page, 2, 1).click({ modifiers: ['Shift'] }); // 2×2 range
    await cell(page, 1, 0).click({ button: 'right' });
    await menuItem(page, 'Copy').click();

    await expect(liveRegion(page)).toContainText('4 cells copied');
    const { text, html } = await readClipboard(page);
    expect(text).toBe(
      `${texts[0][0]}\t${texts[0][1]}\r\n${texts[1][0]}\t${texts[1][1]}\r\n`,
    );
    expect(html).toContain('<table'); // dual flavor present
  });

  test('Copy with headers marks the HTML flavor: <thead> plus the headers metadata flag', async ({
    page,
  }) => {
    await captureClipboardWrites(page);
    await cell(page, 1, 0).click();
    await cell(page, 2, 1).click({ modifiers: ['Shift'] });
    await cell(page, 1, 0).click({ button: 'right' });
    await menuItem(page, 'Copy with headers').click();

    await expect.poll(async () => (await capturedCopies(page)).length).toBe(1);
    const record = (await capturedCopies(page))[0];
    // The header row rides the HTML flavor's <thead>…
    expect(record['text/html']).toContain('<thead><tr><th>Description</th><th>Qty</th></tr></thead>');
    // …and the paste-side flag lands in data-tm-grid so a Tellma grid
    // skips the header row on paste-back (§9.3).
    expect(record['text/html']).toContain('data-tm-grid');
    expect(record['text/html']).toContain('headers&quot;:true');
  });
  test('Copy with headers prepends the header row to the text/plain TSV', async ({
    page,
  }) => {
    await cell(page, 1, 0).click();
    await cell(page, 2, 1).click({ modifiers: ['Shift'] });
    await cell(page, 1, 0).click({ button: 'right' });
    await menuItem(page, 'Copy with headers').click();

    // Retrying read: the clipboard write is async, so a one-shot read can
    // beat it to the payload under CI load.
    await expect
      .poll(async () => (await readClipboard(page)).text.startsWith('Description\tQty\r\n'))
      .toBe(true);
  });
});
