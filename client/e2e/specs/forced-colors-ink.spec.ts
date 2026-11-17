// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { cell, colHeader, gotoGrid, rowCheckbox, rowHeader, selectedCells } from '../support/grid';
import { expectInkOnFill, expectInkShows } from '../support/ink';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Text and glyphs on a highlight fill under forced colors. A selected day,
 * option or grid range draws its fill in Highlight (or SelectedItem), and
 * the computed styles report its ink faithfully. Chromium, though, paints a
 * Canvas backplate behind the text of any element that keeps forced
 * colours, and the fill's paired ink (HighlightText, SelectedItemText) is
 * Canvas in the common palettes, so the text can vanish while every style
 * reads right. Each case is read from the pixels, in the light and the dark
 * forced palette. A picker's selected row draws its paired ink directly on
 * the fill; a grid cell keeps forced colours for what it holds, so its text
 * shows in CanvasText on its plate, while its glyphs, which get no plate,
 * draw in the paired ink on the fill.
 */

/**
 * The least ink a tick read at twice the pixel density must show. The
 * smallest (the tree's and the tiles') carry nearly thirty pixels of ink
 * there, and a tick drawn in the wrong ink none.
 */
const TICK_INK = 10;

/** Settles the page and parks the pointer, so no hover state is in the shots. */
async function settle(page: Page): Promise<void> {
  await page.mouse.move(0, 0);
  await appStable(page);
}

for (const scheme of ['light', 'dark'] as const) {
  test.describe(`on the ${scheme} forced palette`, () => {
    test.beforeEach(async ({ page }) => {
      await page.emulateMedia({ forcedColors: 'active', colorScheme: scheme });
    });

    test('the selected day, month and year show their figures on the highlight', async ({
      page,
    }) => {
      await page.goto(storyUrl('date-picker'));
      await page.getByTestId('picker-due').locator('.tm-date-picker__toggle').click();
      const popup = page.locator('.tm-date-popup');
      await expect(popup).toBeVisible();
      const heading = popup.locator('.tm-date-popup__view-switch');

      // Section: the day grid, opened on the committed day.
      await settle(page);
      await expectInkOnFill(
        page,
        popup.locator('.tm-date-popup__day--selected'),
        'the selected day',
      );

      // Section: the month and year grids, each with the committed one selected.
      await heading.click();
      await expect(popup.locator('.tm-date-popup__months')).toBeVisible();
      await settle(page);
      await expectInkOnFill(
        page,
        popup.locator('.tm-date-popup__month[aria-selected="true"]'),
        'the selected month',
      );
      await heading.click();
      await expect(popup.locator('.tm-date-popup__years')).toBeVisible();
      await settle(page);
      await expectInkOnFill(
        page,
        popup.locator('.tm-date-popup__year[aria-selected="true"]'),
        'the selected year',
      );
    });

    test('a selected month outside the bounds shows its name in GrayText on its plate', async ({
      page,
    }) => {
      await page.goto(storyUrl('date-picker'));
      const picker = page.getByTestId('picker-bounded');
      // A committed value past the bounds: the month view selects its
      // month and disables it.
      await picker.locator('.tm-date-picker__input').fill('4/10/2026');
      await picker.locator('.tm-date-picker__input').press('Tab');
      await expect(page.getByTestId('model-json')).toContainText('"delivery":"2026-04-10"');
      await picker.locator('.tm-date-picker__toggle').click();
      const popup = page.locator('.tm-date-popup');
      await popup.locator('.tm-date-popup__view-switch').click();
      const month = popup.locator('.tm-date-popup__month[aria-selected="true"]');
      await expect(month).toBeDisabled();
      await settle(page);
      await expectInkShows(page, month, 'the disabled selected month', {
        inkColour: 'GrayText',
        plateColour: 'Canvas',
      });
    });

    test('the committed entity row shows its label and check on the highlight', async ({
      page,
    }) => {
      await page.goto(storyUrl('entity-picker'));
      await page.getByTestId('search-async').uncheck();
      await page.getByTestId('picker-populated').locator('.tm-entity-picker__input').click();
      const row = page.locator('.tm-entity-picker__option[aria-selected="true"]');
      await expect(row).toBeVisible();
      await settle(page);
      await expectInkOnFill(page, row.locator('.tm-entity-picker__option-label'), 'the row label');
      await expectInkOnFill(page, row.locator('.tm-entity-picker__check'), 'the row check');
    });

    test('a selected option shows its label and check on the highlight', async ({ page }) => {
      await page.goto(storyUrl('select'));
      const trigger = page.getByTestId('select-grouped').locator('.tm-select__trigger');
      await trigger.click();
      await page.locator('.tm-option__row', { hasText: 'Sale' }).click();
      await expect(trigger).toContainText('Sale');
      await trigger.click();
      const row = page.locator('.tm-select__panel .tm-option__row[aria-selected="true"]');
      await expect(row).toBeVisible();
      await settle(page);
      await expectInkOnFill(page, row.locator('.tm-option__content'), 'the option label');
      await expectInkOnFill(page, row.locator('.tm-option__check'), 'the option check');
    });

    test('a selected multi-select row shows its label and tick on the highlight', async ({
      page,
    }) => {
      await page.goto(storyUrl('select'));
      await page.getByTestId('multi-grouped').locator('.tm-select__trigger').click();
      const row = page.locator('.tm-select__panel .tm-option__row[aria-selected="true"]');
      await expect(row).toHaveCount(1);
      await settle(page);
      await expectInkOnFill(page, row.locator('.tm-option__content'), 'the row label');
      await expectInkOnFill(page, row.locator('.tm-option__box'), 'the row tick');
    });

    test.describe('read at twice the pixel density', () => {
      // A tick's strokes are thinner than a pixel at 1x: a dozen
      // anti-aliased pixels, whose summed ink a rasteriser's rounding can
      // tip by one. At 2x the same tick spans four times the pixels.
      test.use({ deviceScaleFactor: 2 });

      test('the ticks of a checked checkbox, tree row and tile show on the highlight', async ({
        page,
      }) => {
        // Section: tm-checkbox.
        await page.goto(storyUrl('checkbox'));
        await page.getByTestId('cb-simple').getByRole('checkbox').check();
        await settle(page);
        const tick = { minInk: TICK_INK };
        await expectInkOnFill(
          page,
          page.getByTestId('cb-simple').locator('.tm-checkbox__box'),
          'the checkbox tick',
          tick,
        );

        // Section: a selected row of a multiple-selection tree.
        await page.goto(storyUrl('tree'));
        const selected = page
          .getByTestId('tree-multiple')
          .locator('[role="treeitem"][aria-selected="true"] > .tm-tree__row .tm-tree__check')
          .first();
        await expect(selected).toBeVisible();
        await settle(page);
        await expectInkOnFill(page, selected, 'the tree row tick', tick);

        // Section: a selected tile.
        await page.goto(storyUrl('tiles'));
        const tile = page.getByTestId('tiles').locator('.tm-tiles__item[data-index="1"]');
        await tile.locator('[role="checkbox"]').click();
        await expect(tile).toHaveAttribute('aria-selected', 'true');
        await settle(page);
        await expectInkOnFill(page, tile.locator('.tm-tiles__box'), 'the tile tick', tick);
      });
    });

    test('the range fill and its hit headers show their text on its plate', async ({ page }) => {
      await gotoGrid(page, 'grid-readonly');
      // Rows 1-2 x columns 0-2, the active cell at (1,2). Column 0 holds
      // the story's record links, so the range grows by keyboard: a
      // shift-click there would open the link.
      await cell(page, 1, 2).click();
      await page.keyboard.press('Shift+ArrowDown');
      await page.keyboard.press('Shift+ArrowLeft');
      await page.keyboard.press('Shift+ArrowLeft');
      await expect(selectedCells(page)).toHaveCount(6);
      // The active cell's ring is not the fill: read the range's other cells.
      await settle(page);
      const plate = { inkColour: 'CanvasText', plateColour: 'Canvas' };
      await expectInkShows(page, cell(page, 2, 1), 'a cell in the range', plate);
      await expectInkShows(page, cell(page, 2, 2), 'another cell in the range', plate);
      await expectInkShows(page, rowHeader(page, 2), 'a hit row header', plate);
      await expectInkShows(
        page,
        colHeader(page, 1).locator('.tm-grid__colhdr-label'),
        'a hit column header',
        plate,
      );
      // A consumer's link keeps its forced link colour, which its plate shows.
      await expectInkShows(page, cell(page, 2, 0).locator('a'), 'a link in the range', {
        inkColour: 'LinkText',
        plateColour: 'Canvas',
      });
    });

    test('a sort arrow on a hit column header shows on the highlight', async ({ page }) => {
      await gotoGrid(page, 'grid-search-page');
      await colHeader(page, 1).click();
      await expect(page.getByTestId('order')).toHaveText('name asc');
      // The active cell's column header is hit.
      await cell(page, 2, 1).click();
      await expect(colHeader(page, 1)).toHaveClass(/tm-grid__colhdr--hit/);
      await settle(page);
      await expectInkOnFill(
        page,
        colHeader(page, 1).locator('.tm-grid__sort-glyph').first(),
        'the sort arrow',
        { inkColour: 'SelectedItemText' },
      );
    });

    test('the chevron of a tree row in the range fill shows on the highlight', async ({ page }) => {
      await gotoGrid(page, 'tree-grid');
      // Rows 0-1 x columns 0-1, the active cell at (1,1): the first row's
      // hierarchy cell is in the fill without the active ring.
      await cell(page, 1, 1).click();
      await page.keyboard.press('Shift+ArrowUp');
      await page.keyboard.press('Shift+ArrowLeft');
      await expect(selectedCells(page)).toHaveCount(4);
      await settle(page);
      await expectInkOnFill(
        page,
        cell(page, 0, 0).locator('.tm-grid__expander-glyph'),
        'the chevron',
      );
    });

    test('a checked row shows its text on its plate', async ({ page }) => {
      await gotoGrid(page, 'grid-list-screen');
      await rowCheckbox(page, 0).click();
      await expect(rowCheckbox(page, 0)).toHaveClass(/tm-grid__check--on/);
      await settle(page);
      await expectInkShows(page, cell(page, 0, 1), 'a checked row cell', {
        inkColour: 'CanvasText',
        plateColour: 'Canvas',
      });
    });
  });
}
