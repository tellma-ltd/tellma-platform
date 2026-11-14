// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { cell, expectCellText, gotoGrid, gridScroller, liveRegion } from '../support/grid';
import { holdResponses, releaseResponses } from '../support/held-responses';

/**
 * The tree grid's remembered expansion and `scrollToRow` against the
 * expansion-restore story: an all-lazy cost-centre tree whose expansion the
 * screen keeps (`expansionSource="model"`). Mounting restores it with one
 * `loadChildren` call per level, in data order, keeping an id whose row no
 * longer exists; collapses and expands write back; the lazy-loading
 * gestures (reserved-slot spinner, re-collapse during a load, failure); and
 * the `scrollToRow` flash under reduced motion and forced colors, which
 * never takes focus. Every path runs left-to-right and right-to-left.
 *
 * Restored shape: Operations › Plant (Line A, Line B), Logistics; Finance ›
 * Treasury (Cash pool, Hedging), Payroll; Archive (collapsed) — 11 rows,
 * readonly (no placeholder) → aria-rowcount 12.
 */

/** The rendered row element at a view-space row index. */
function row(page: Page, viewIndex: number): Locator {
  return page.locator(`.tm-grid__row[aria-rowindex="${viewIndex + 2}"]`);
}

/** The rendered row element of a row id (numeric ids key as `#<id>`). */
function rowById(page: Page, id: number): Locator {
  return page.locator(`.tm-grid__row[data-tm-key="#${id}"]`);
}

/** The expander button inside a row's hierarchy cell. */
function expander(page: Page, viewIndex: number): Locator {
  return cell(page, viewIndex, 0).locator('[data-tm-expander]');
}

/** The lazy-loading spinner inside a row's reserved slot. */
function childSpinner(page: Page, viewIndex: number): Locator {
  return cell(page, viewIndex, 0).locator('[data-tm-childspin]');
}

/** The story's log of `loadChildren` calls, one entry per call. */
function batchLog(page: Page): Locator {
  return page.getByTestId('batch-log').locator('li');
}

/** The story's readout of the remembered expansion (ids ascending). */
function remembered(page: Page): Locator {
  return page.getByTestId('remembered');
}

/** Waits for the mount's restore to settle: both levels loaded. */
async function restored(page: Page): Promise<void> {
  await expect(batchLog(page)).toHaveText(['1, 2', '11, 21']);
  await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '12');
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`expansion restore (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await gotoGrid(page, 'tree-grid-restore', { dir });
      await restored(page);
    });

    test('@cross-engine a restore loads one batch per level and keeps an id it cannot place', async ({
      page,
    }) => {
      await expectCellText(page, 0, 0, 'Operations');
      await expectCellText(page, 1, 0, 'Plant');
      await expectCellText(page, 2, 0, 'Line A');
      await expect(row(page, 1)).toHaveAttribute('aria-expanded', 'true');
      await expect(row(page, 4)).toHaveAttribute('aria-expanded', 'false'); // Logistics
      await expect(row(page, 10)).toHaveAttribute('aria-expanded', 'false'); // Archive
      // 404 has no row, and the bound set still holds it.
      await expect(remembered(page)).toHaveText('1, 2, 11, 21, 404');
    });

    test('@cross-engine a collapse writes back only that row; a remount restores the rest', async ({
      page,
    }) => {
      await expander(page, 0).click(); // collapse Operations
      await expect(remembered(page)).toHaveText('2, 11, 21, 404');
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '8');

      await page.getByTestId('toggle-mounted').uncheck();
      await expect(gridScroller(page)).toHaveCount(0);
      await page.getByTestId('toggle-mounted').check(); // the roots are fetched again
      await expect(batchLog(page)).toHaveText(['2', '21']);
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '8');

      // Plant (11) stayed remembered under the collapsed Operations: one
      // expand brings both levels back, a batch each.
      await expander(page, 0).click();
      await expect(batchLog(page)).toHaveText(['2', '21', '1', '11']);
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '12');
      await expect(remembered(page)).toHaveText('1, 2, 11, 21, 404');
    });

    test('@cross-engine expanding a lazy row shows the spinner in reserved space while it loads', async ({
      page,
    }) => {
      // The load is held open until released: the in-flight state is
      // asserted while it provably lasts, never against a delay.
      await holdResponses(page, 'children');
      const textSpan = cell(page, 10, 0).locator('.tm-grid__text');
      // The inline extent only: activating the bottom row may scroll the
      // grid by a pixel to keep it clear of the edge, which moves it
      // vertically but has nothing to do with the spinner's slot.
      const inline = async (): Promise<readonly [number, number] | null> => {
        const box = await textSpan.boundingBox();
        return box === null ? null : [box.x, box.width];
      };
      const before = await inline();
      expect(before).not.toBeNull();

      await expander(page, 10).click(); // Archive
      await expect(childSpinner(page, 10)).toBeVisible();
      // The spinner filled RESERVED space: the cell content did not move.
      expect(await inline()).toEqual(before);
      // Expanded at once; the children follow the load.
      await expect(row(page, 10)).toHaveAttribute('aria-expanded', 'true');
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '12');

      await releaseResponses(page, 'children');
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '14');
      await expect(childSpinner(page, 10)).toHaveCount(0);
      await expectCellText(page, 11, 0, 'Archive 2019');
      await expect(row(page, 11)).toHaveAttribute('aria-level', '2');
      expect(await inline()).toEqual(before);
    });

    test('@cross-engine re-collapsing during the load wins; the next expand is instant', async ({
      page,
    }) => {
      await holdResponses(page, 'children');
      await expander(page, 10).click(); // expand Archive → the load starts
      await expect(childSpinner(page, 10)).toBeVisible();
      await expander(page, 10).click(); // re-collapse while loading
      await expect(row(page, 10)).toHaveAttribute('aria-expanded', 'false');
      await releaseResponses(page, 'children');

      await expect(childSpinner(page, 10)).toHaveCount(0);
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '12');

      // The children are loaded now: expanding renders them with no call.
      await expander(page, 10).click();
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '14');
      await expect(batchLog(page)).toHaveText(['1, 2', '11, 21', '3']);
    });

    test('@cross-engine a failed load collapses the row, forgets it, and announces', async ({
      page,
    }) => {
      await page.getByTestId('fail-toggle').check();
      await expander(page, 10).click();
      await expect(liveRegion(page)).toContainText('Could not load child rows');
      await expect(childSpinner(page, 10)).toHaveCount(0);
      await expect(row(page, 10)).toHaveAttribute('aria-expanded', 'false');
      await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '12');
      await expect(remembered(page)).toHaveText('1, 2, 11, 21, 404');
    });

    test('scrollToRow expands the ancestors and flashes the row, still, under reduced motion', async ({
      page,
    }) => {
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await expander(page, 5).click(); // collapse Finance: Hedging (212) hides
      await expect(remembered(page)).toHaveText('1, 11, 21, 404');
      await expect(rowById(page, 212)).toHaveCount(0);

      const go = page.getByTestId('scroll-go');
      await go.click(); // row 212, flash on
      await expect(page.getByTestId('scroll-result')).toHaveText('true');
      await expect(remembered(page)).toHaveText('1, 2, 11, 21, 404');

      const target = rowById(page, 212);
      await expect(target).toHaveClass(/tm-grid__row--flash/);
      // Reduced motion holds the tint still: the cell paints the token's
      // colour itself rather than a frame of a fade.
      const flashColour = await gridScroller(page).evaluate((scroller) => {
        const probe = document.createElement('div');
        probe.style.backgroundColor = 'var(--grid-flash-bg)';
        scroller.append(probe);
        const colour = getComputedStyle(probe).backgroundColor;
        probe.remove();
        return colour;
      });
      const cellColour = await target
        .locator('[data-tm-cell]')
        .first()
        .evaluate((el) => getComputedStyle(el).backgroundColor);
      expect(cellColour).toBe(flashColour);

      // Activation moved to the row; focus stayed on the button.
      await expect(target.locator('[data-tm-cell][tabindex="0"]')).toHaveCount(1);
      await expect(go).toBeFocused();
      // The flash ends after --grid-flash-duration.
      await expect(target).not.toHaveClass(/tm-grid__row--flash/);
    });

    test('scrollToRow outlines the flashing row under forced colors', async ({ page }) => {
      await page.emulateMedia({ forcedColors: 'active' });
      await page.getByTestId('scroll-go').click();
      const target = rowById(page, 212);
      await expect(target).toHaveClass(/tm-grid__row--flash/);
      expect(await target.evaluate((el) => getComputedStyle(el).outlineStyle)).toBe('solid');
    });

    test('scrollToRow reports a row that has not loaded', async ({ page }) => {
      await page.getByTestId('scroll-row-id').fill('31'); // under the collapsed Archive
      await page.getByTestId('scroll-go').click();
      await expect(page.getByTestId('scroll-result')).toHaveText('false');
    });
  });
}
