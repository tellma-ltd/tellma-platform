// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import {
  blockCentreOffset,
  EPSILON,
  expectLucideStroke,
  expectTokenSquare,
  isMirrored,
  measure,
  type GlyphMeasure,
} from '../support/glyph-geometry';
import { activateCell, cell, findBar, gotoGrid, gridScroller, statusChip } from '../support/grid';
import { appStable } from '../support/settle';

/**
 * Geometry battery for the grid's glyphs, drawn through tm-icon: the find
 * bar's chevrons and close cross, the status bar's error chevrons and mark,
 * the tree expander and the context menu's icons. Each keeps its box (a
 * token-sized square centred in its button) and the Lucide stroke in both
 * directions; the reading-direction chevrons mirror in right-to-left text
 * and follow a live direction flip, and nothing else mirrors.
 */

// Section: the Lucide drawings the grid uses, as the measure reports them
// (a rect's geometry is not among the attributes it reads).
const CHEVRON_UP = ['d=m18 15-6-6-6 6'];
const CHEVRON_DOWN = ['d=m6 9 6 6 6-6'];
const CHEVRON_LEFT = ['d=m15 18-6-6 6-6'];
const CHEVRON_RIGHT = ['d=m9 18 6-6-6-6'];
const X = ['d=M18 6 6 18', 'd=m6 6 12 12'];
const CIRCLE_ALERT = ['cx=12 cy=12 r=10', 'x1=12 x2=12 y1=8 y2=12', 'x1=12 x2=12.01 y1=16 y2=16'];
const MENU_GLYPHS: Readonly<Record<string, readonly string[]>> = {
  scissors: [
    'cx=6 cy=6 r=3',
    'd=M8.12 8.12 12 12',
    'd=M20 4 8.12 15.88',
    'cx=6 cy=18 r=3',
    'd=M14.8 14.8 20 20',
  ],
  copy: ['', 'd=M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2'],
  'copy-plus': [
    'x1=15 x2=15 y1=12 y2=18',
    'x1=12 x2=18 y1=15 y2=15',
    '',
    'd=M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2',
  ],
  'clipboard-paste': [
    'd=M11 14h10',
    'd=M16 4h2a2 2 0 0 1 2 2v1.344',
    'd=m17 18 4-4-4-4',
    'd=M8 4H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 1.793-1.113',
    '',
  ],
  plus: ['d=M5 12h14', 'd=M12 5v14'],
  'corner-down-right': ['d=m15 10 5 5-5 5', 'd=M4 4v7a4 4 0 0 0 4 4h12'],
  'trash-2': [
    'd=M10 11v6',
    'd=M14 11v6',
    'd=M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6',
    'd=M3 6h18',
    'd=M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2',
  ],
};

/** The gap between the centres of a glyph and its container, along the inline axis. */
function inlineCentreOffset(measured: GlyphMeasure): number {
  return Math.abs(
    measured.svg.x + measured.svg.width / 2 - (measured.container.x + measured.container.width / 2),
  );
}

/** Asserts a glyph is a token square, centred in its button, drawn with these shapes. */
async function expectCentredGlyph(
  button: Locator,
  token: string,
  geometry: readonly string[],
): Promise<GlyphMeasure> {
  const measured = await measure(button.locator('tm-icon'), button, token);
  expectTokenSquare(measured);
  expectLucideStroke(measured, geometry);
  expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
  expect(inlineCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
  return measured;
}

/**
 * Opens a cell's context menu and asserts every icon it draws: a known
 * Lucide glyph, a token square centred in the icon box with the Lucide
 * stroke, never mirrored. Returns the glyph names it saw.
 */
async function expectMenuIcons(page: Page, row: number): Promise<string[]> {
  await cell(page, row, 0).click({ button: 'right' });
  const panel = page.locator('.tm-menu__panel');
  await expect(panel).toBeVisible();
  await appStable(page);

  const seen = new Set<string>();
  const boxes = panel.locator('.tm-menu__icon:has(tm-icon)');
  const count = await boxes.count();
  expect(count).toBeGreaterThan(0);
  for (let index = 0; index < count; index++) {
    const box = boxes.nth(index);
    const measured = await measure(box.locator('tm-icon'), box, '--menu-icon-size');
    const geometry = measured.shapes.map((shape) => shape.geometry);
    const name = Object.keys(MENU_GLYPHS).find(
      (glyph) => JSON.stringify(MENU_GLYPHS[glyph]) === JSON.stringify(geometry),
    );
    expect(name, `menu icon ${index}: ${JSON.stringify(geometry)}`).toBeDefined();
    seen.add(name!);
    expectTokenSquare(measured);
    expectLucideStroke(measured, geometry);
    expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
    expect(inlineCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
    expect(await isMirrored(box.locator('tm-icon'))).toBe(false);
  }
  return [...seen].sort();
}

/** Clears a required cell of the editable story, so the status bar shows its tally. */
async function raiseOneError(page: Page): Promise<void> {
  await activateCell(page, 3, 0);
  await page.keyboard.press('Delete');
  await expect(statusChip(page)).toContainText('1 error');
}

/** The expander of the tree story's first row ('Assets', expanded by default). */
function firstExpander(page: Page): Locator {
  return cell(page, 0, 0).locator('[data-tm-expander]');
}

/** The tree story's first row. */
function firstRow(page: Page): Locator {
  return page.locator('.tm-grid__row[aria-rowindex="2"]');
}

/** Flips the document direction the way an app's language switch does. */
async function flipDocumentTo(page: Page, dir: 'ltr' | 'rtl'): Promise<void> {
  await page.evaluate((value) => {
    document.documentElement.dir = value;
  }, dir);
}

for (const dir of ['ltr', 'rtl'] as const) {
  const rtl = dir === 'rtl';

  test.describe(`grid glyph geometry (${dir})`, () => {
    test('the find bar draws its chevrons and cross centred, unmirrored', async ({ page }) => {
      await gotoGrid(page, 'grid-list-screen', { dir });
      await activateCell(page, 0, 0);
      await page.keyboard.press('Control+f');
      const bar = findBar(page);
      await expect(bar).toBeVisible();
      await appStable(page);

      const buttons = [
        [bar.locator('.tm-grid__find-nav').nth(0), CHEVRON_UP],
        [bar.locator('.tm-grid__find-nav').nth(1), CHEVRON_DOWN],
        [bar.locator('.tm-grid__find-close'), X],
      ] as const;
      for (const [button, geometry] of buttons) {
        await expectCentredGlyph(button, '--grid-status-icon-size', geometry);
        expect(await isMirrored(button.locator('tm-icon'))).toBe(false);
      }
    });

    test('the status bar mirrors its chevrons in right-to-left text, not its error mark', async ({
      page,
    }) => {
      await gotoGrid(page, 'grid-editable', { dir });
      await raiseOneError(page);
      await appStable(page);

      const previous = page.locator('[data-tm-status-prev]');
      const next = page.locator('[data-tm-status-next]');
      await expectCentredGlyph(previous, '--grid-status-icon-size', CHEVRON_LEFT);
      await expectCentredGlyph(next, '--grid-status-icon-size', CHEVRON_RIGHT);
      expect(await isMirrored(previous.locator('tm-icon'))).toBe(rtl);
      expect(await isMirrored(next.locator('tm-icon'))).toBe(rtl);
      // The error mark is symmetric: it never mirrors.
      const mark = await measure(
        statusChip(page).locator('tm-icon'),
        statusChip(page),
        '--grid-status-icon-size',
      );
      expectTokenSquare(mark);
      expectLucideStroke(mark, CIRCLE_ALERT);
      expect(blockCentreOffset(mark)).toBeLessThanOrEqual(EPSILON);
      expect(await isMirrored(statusChip(page).locator('tm-icon'))).toBe(false);
    });

    test('the tree expander swaps a down chevron for a reading-direction one', async ({ page }) => {
      await gotoGrid(page, 'tree-grid', { dir });
      const expander = firstExpander(page);
      await expect(expander).toBeVisible();

      // Expanded: a down chevron, never mirrored.
      await expectCentredGlyph(expander, '--grid-twisty-size', CHEVRON_DOWN);
      expect(await isMirrored(expander.locator('tm-icon'))).toBe(false);

      // Collapsed: a right chevron, mirrored to point along right-to-left text.
      await expander.click();
      await expect(firstRow(page)).toHaveAttribute('aria-expanded', 'false');
      await expectCentredGlyph(expander, '--grid-twisty-size', CHEVRON_RIGHT);
      expect(await isMirrored(expander.locator('tm-icon'))).toBe(rtl);
    });

    test('the context menu draws its icons in the icon box, unmirrored', async ({ page }) => {
      await gotoGrid(page, 'grid-editable', { dir });
      // Cut, copy, copy with headers, paste, the two inserts and delete.
      expect(await expectMenuIcons(page, 2)).toEqual(
        ['clipboard-paste', 'copy', 'copy-plus', 'plus', 'scissors', 'trash-2'].sort(),
      );
    });

    test("the tree's context menu adds the insert-child arrow, unmirrored", async ({ page }) => {
      await gotoGrid(page, 'tree-grid', { dir });
      expect(await expectMenuIcons(page, 1)).toContain('corner-down-right');
    });
  });
}

test.describe('grid glyphs after a live direction flip', () => {
  test('a collapsed expander mirrors when the document flips, and back', async ({ page }) => {
    await gotoGrid(page, 'tree-grid');
    const expander = firstExpander(page);
    await expander.click();
    await expect(firstRow(page)).toHaveAttribute('aria-expanded', 'false');
    const glyph = expander.locator('tm-icon');
    expect(await isMirrored(glyph)).toBe(false);

    await flipDocumentTo(page, 'rtl');
    await expect.poll(() => isMirrored(glyph)).toBe(true);
    // An expanded row's down chevron stays as it is.
    await expect
      .poll(() => isMirrored(cell(page, 1, 0).locator('[data-tm-expander] tm-icon')))
      .toBe(false);

    await flipDocumentTo(page, 'ltr');
    await expect.poll(() => isMirrored(glyph)).toBe(false);
  });

  test("the status bar's chevrons mirror when the document flips, and back", async ({ page }) => {
    await gotoGrid(page, 'grid-editable');
    await raiseOneError(page);
    const previous = page.locator('[data-tm-status-prev] tm-icon');
    const next = page.locator('[data-tm-status-next] tm-icon');
    expect(await isMirrored(previous)).toBe(false);
    expect(await isMirrored(next)).toBe(false);

    await flipDocumentTo(page, 'rtl');
    await expect.poll(() => isMirrored(previous)).toBe(true);
    await expect.poll(() => isMirrored(next)).toBe(true);
    expect(await isMirrored(statusChip(page).locator('tm-icon'))).toBe(false);

    await flipDocumentTo(page, 'ltr');
    await expect.poll(() => isMirrored(previous)).toBe(false);
    await expect.poll(() => isMirrored(next)).toBe(false);
  });
});

// Quarantined at the bar spec 0010 §C.13 sets: the grid's scroll cost already
// exceeds 50 ms per task on a flat grid with no icons in its rows, so this
// probe runs in the non-blocking quarantine job until that budget is met.
test.describe('the expanded tree under scroll', () => {
  test('@perf @quarantine scrolling a 21,000-row expanded tree produces no long task over 50 ms', async ({
    page,
    browserName,
  }) => {
    test.skip(browserName !== 'chromium', 'Long-task timing is a Chromium API.');
    await gotoGrid(page, 'tree-grid-large');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '21001');
    await expect(page.locator('[data-tm-expander]').first()).toBeVisible();
    await appStable(page);

    // Section: record long tasks from here on.
    await page.evaluate(() => {
      const record = window as unknown as { tmLongTasks: number[] };
      record.tmLongTasks = [];
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries()) {
          record.tmLongTasks.push(entry.duration);
        }
      }).observe({ type: 'longtask' });
    });

    // Section: wheel down through the tree a screen at a time, then back.
    const box = (await gridScroller(page).boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    for (const direction of [1, -1]) {
      for (let step = 0; step < 40; step++) {
        await page.mouse.wheel(0, direction * box.height);
        await page.evaluate(() => new Promise((resolve) => requestAnimationFrame(resolve)));
      }
    }
    await appStable(page);

    const longTasks = await page.evaluate(
      () => (window as unknown as { tmLongTasks: number[] }).tmLongTasks,
    );
    expect(longTasks.filter((duration) => duration > 50)).toEqual([]);
  });
});
