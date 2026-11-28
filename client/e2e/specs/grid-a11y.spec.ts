// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import {
  activeCell,
  cell,
  cellPaint,
  gotoGrid,
  gridScroller,
  liveRegion,
  selectedCells,
  syntheticCopy,
} from '../support/grid';

/**
 * Accessibility battery (spec 0004 §14): the axe static floor over the
 * grid's states and themes, the role/announcement mechanics, roving-focus
 * discipline, and the forced-colors / reduced-motion media contracts.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`the populated readonly grid is axe-clean (${theme})`, async ({ page }) => {
      await gotoGrid(page, 'grid-readonly', { theme });
      await cell(page, 1, 1).click(); // active + selected state included in the scan
      await expectNoAxeViolations(page);
    });
  }

  test('the loading state is axe-clean', async ({ page }) => {
    await gotoGrid(page, 'grid-states');
    await page.getByTestId('set-loading').click();
    await expect(page.locator('[data-tm-loading]')).toBeVisible();
    await expect(gridScroller(page)).toHaveAttribute('aria-busy', 'true');
    await expectNoAxeViolations(page);
  });

  test('the empty state is axe-clean', async ({ page }) => {
    await gotoGrid(page, 'grid-states');
    await page.getByTestId('set-empty').click();
    await expect(page.locator('[data-tm-empty]')).toBeVisible();
    await expect(page.locator('[data-tm-empty]')).toContainText('No records to display');
    await expectNoAxeViolations(page);
  });
});

test.describe('role audit (§14)', () => {
  test('grid roles and full model counts under virtualization', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
    const scroller = gridScroller(page);

    await expect(scroller).toHaveAttribute('aria-rowcount', '100001'); // model, not DOM
    await expect(scroller).toHaveAttribute('aria-colcount', '13'); // 12 columns + row header
    await expect(scroller).toHaveAttribute('aria-multiselectable', 'true');

    const headerRow = page.locator('.tm-grid__header');
    await expect(headerRow).toHaveRole('row');
    await expect(headerRow).toHaveAttribute('aria-rowindex', '1');
    await expect(page.locator('[role="row"][aria-rowindex="2"]')).toHaveCount(1); // first data row

    await expect(page.locator('[role="columnheader"]')).toHaveCount(13); // corner + 12
    await expect(page.locator('[role="rowheader"][data-row="0"]')).toBeVisible();
    await expect(cell(page, 0, 0)).toHaveRole('gridcell');
    await expect(cell(page, 0, 0)).toHaveAttribute('aria-colindex', '2'); // offset by row header
  });

  test('aria-selected appears on selected cells only', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
    await cell(page, 1, 1).click();

    await expect(selectedCells(page)).toHaveCount(1);
    await expect(cell(page, 1, 1)).toHaveAttribute('aria-selected', 'true');
    await expect(cell(page, 1, 2)).not.toHaveAttribute('aria-selected');
    await expect(cell(page, 2, 1)).not.toHaveAttribute('aria-selected');
  });
});

test.describe('roving focus discipline', () => {
  test('exactly one cell is a tab stop after activation', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
    await expect(gridScroller(page)).toHaveAttribute('tabindex', '0'); // container first

    await cell(page, 3, 2).click();

    await expect(activeCell(page)).toHaveCount(1);
    await expect(activeCell(page)).toHaveAttribute('data-row', '3');
    await expect(gridScroller(page)).toHaveAttribute('tabindex', '-1'); // handoff complete
  });
});

test.describe('forced colors (§14)', () => {
  test('selected and active cells stay distinguishable from normal cells', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await gotoGrid(page, 'grid-readonly');
    await cell(page, 1, 1).click();
    await cell(page, 2, 2).click({ modifiers: ['Shift'] }); // (1,1) active, 2×2 selected
    // The range is drawn on the render after the click; cellPaint then
    // settles the app before each read.
    await expect(selectedCells(page)).toHaveCount(4);

    const active = await cellPaint(page, 1, 1); // a zebra row
    const selectedZebra = await cellPaint(page, 1, 2);
    const selectedEven = await cellPaint(page, 2, 1);
    const selected = await cellPaint(page, 2, 2);
    const plainZebra = await cellPaint(page, 5, 5); // same zebra parity as row 1
    const plainEven = await cellPaint(page, 6, 6); // same parity as row 2

    // Selection paints with a system color, not the tint the theme uses,
    // on both row parities: the zebra stripe never masks the range fill.
    expect(selected.background).not.toBe(plainEven.background);
    expect(selected.background).not.toBe(plainZebra.background);
    for (const paint of [active, selectedZebra, selectedEven]) {
      expect(paint.background).toBe(selected.background);
    }
    // Text stays legible on the fill.
    for (const paint of [active, selectedZebra, selectedEven, selected]) {
      expect(paint.ink).not.toBe(paint.background);
    }
    // The active cell swaps its inset shadow for a real outline, in a color
    // that shows against the fill it shares with the rest of the range.
    expect(active.outline).toBe('solid');
    expect(active.outlineColor).not.toBe(active.background);
    expect(selected.outline).toBe('none');
    expect(plainZebra.outline).toBe('none');
  });

  test('a lone active cell keeps the plain background and a visible ring', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await gotoGrid(page, 'grid-readonly');
    await cell(page, 1, 1).click(); // a lone selected cell paints no range fill
    await expect(cell(page, 1, 1)).toHaveAttribute('aria-selected', 'true');

    const active = await cellPaint(page, 1, 1);
    const plainZebra = await cellPaint(page, 5, 5);
    expect(active.background).toBe(plainZebra.background);
    expect(active.outline).toBe('solid');
    expect(active.outlineColor).not.toBe(active.background);
  });

  test('the copy marquee stays visible over the range fill', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await gotoGrid(page, 'grid-readonly');
    await cell(page, 1, 1).click();
    await cell(page, 2, 2).click({ modifiers: ['Shift'] });
    await expect(selectedCells(page)).toHaveCount(4);
    await syntheticCopy(page); // a copy arms the marquee on any grid, readonly included
    await expect(page.locator('[data-tm-cell][data-tm-cut]')).toHaveCount(4);

    for (const [row, col] of [
      [1, 1],
      [1, 2],
      [2, 1],
      [2, 2],
    ] as const) {
      const paint = await cellPaint(page, row, col);
      expect(paint.marquee).not.toBe(paint.background);
    }
  });
});

test.describe('reduced motion', () => {
  test('the grid loads and navigates under prefers-reduced-motion', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await gotoGrid(page, 'grid-readonly');

    await cell(page, 0, 0).click();
    await page.keyboard.press('Control+End'); // scroll jumps are instant either way
    await expect(cell(page, 99999, 11)).toBeFocused();
  });
});

test.describe('announcements (§14, CDK live region)', () => {
  test('a multi-cell selection announces its R × C shape', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
    await cell(page, 1, 1).click();
    await cell(page, 3, 2).click({ modifiers: ['Shift'] });

    await expect(liveRegion(page)).toContainText('3 × 2 selected');
  });

  test('select-all announces the whole-grid shape', async ({ page }) => {
    await gotoGrid(page, 'grid-readonly');
    await cell(page, 1, 1).click();
    await page.keyboard.press('Control+a');

    await expect(liveRegion(page)).toContainText('All cells selected');
  });

  test('loading transitions announce: Loading, then the loaded record count', async ({ page }) => {
    await gotoGrid(page, 'grid-states');
    await page.getByTestId('set-loading').click();
    await expect(liveRegion(page)).toContainText('Loading');

    await page.getByTestId('set-loading').click(); // toggle back off
    await expect(liveRegion(page)).toContainText('records loaded');
  });
});
