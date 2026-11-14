// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';
import { Check, CircleAlert, Search } from 'lucide';

import {
  blockCentreOffset,
  EPSILON,
  expectLucideStroke,
  expectTokenSquare,
  inlineCentreOffset,
  inlineEndGap,
  isMirrored,
  lucideGeometry,
  measure,
} from '../support/glyph-geometry';
import { activateCell, gotoGrid } from '../support/grid';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Geometry battery for the entity picker's glyphs, drawn through tm-icon:
 * the invalid mark beside a field's error, the magnifier at the inline end,
 * and the chosen option's check, in a field and in a grid cell's dropdown.
 * Each keeps its box (the token-sized square its container gives it, in its
 * place in that container) and the Lucide stroke, in both directions; none
 * of them mirrors.
 *
 * Every expectation derives from the computed tokens and paddings of the
 * glyph's container and from the Lucide data the glyph draws, so a token
 * change moves the expectation with it.
 */

/** The field-wrapped picker's input. */
function fieldInput(page: Page): Locator {
  return page.getByTestId('picker-field').locator('.tm-entity-picker__input');
}

/** The chosen row of the open dropdown. */
function chosenRow(page: Page): Locator {
  return page.locator('.tm-entity-picker__option[aria-selected="true"]');
}

/** Asserts the chosen row's check: a token square at the row's inline end, centred on it. */
async function expectCheckGlyph(row: Locator, dir: 'ltr' | 'rtl'): Promise<void> {
  const glyph = row.locator('.tm-entity-picker__check');
  await expect(glyph).toBeVisible();
  const measured = await measure(glyph, row, '--entity-picker-icon-size');
  expectTokenSquare(measured);
  expectLucideStroke(measured, lucideGeometry(Check));
  expect(measured.shapes.every((shape) => shape.painted)).toBe(true);
  expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
  expect(Math.abs(inlineEndGap(measured, dir) - measured.containerEndInset)).toBeLessThanOrEqual(
    EPSILON,
  );
  expect(await isMirrored(glyph)).toBe(false);
}

/** Asserts the magnifier: a token square centred in its button. */
async function expectMagnifierGlyph(button: Locator): Promise<void> {
  const glyph = button.locator(':scope > tm-icon');
  const measured = await measure(glyph, button, '--entity-picker-icon-size');
  expectTokenSquare(measured);
  expectLucideStroke(measured, lucideGeometry(Search));
  expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
  expect(inlineCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
  expect(await isMirrored(glyph)).toBe(false);
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`entity picker glyph geometry in a field (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await page.goto(storyUrl('entity-picker', { dir }));
      await expect(fieldInput(page)).toBeVisible();
      // The synchronous search: every expectation reads a settled panel.
      await page.getByTestId('search-async').uncheck();
    });

    test('the magnifier sits at the inline end, centred in its button', async ({ page }) => {
      const button = page.getByTestId('picker-field').locator('.tm-entity-picker__magnifier');
      await expectMagnifierGlyph(button);
      // The button is the control's last box along the inline axis.
      const inputBox = (await fieldInput(page).boundingBox())!;
      const buttonBox = (await button.boundingBox())!;
      if (dir === 'ltr') {
        expect(buttonBox.x).toBeGreaterThan(inputBox.x);
      } else {
        expect(buttonBox.x).toBeLessThan(inputBox.x);
      }
    });

    test('the invalid mark is a token square, centred on the control', async ({ page }) => {
      await fieldInput(page).fill('Zebra');
      await page.getByTestId('submit').focus();
      await expect(page.getByTestId('ff').locator('.tm-form-field__error')).toContainText(
        'No match for',
      );
      const host = page.getByTestId('picker-field');
      const glyph = host.locator('.tm-form-field__error-icon');
      await expect(glyph).toBeVisible();
      const measured = await measure(glyph, host, '--field-error-icon-size');
      expectTokenSquare(measured);
      expectLucideStroke(measured, lucideGeometry(CircleAlert));
      expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
      expect(await isMirrored(glyph)).toBe(false);
      // It sits between the text and the magnifier, so the button never moves.
      const glyphBox = (await glyph.boundingBox())!;
      const buttonBox = (await host.locator('.tm-entity-picker__magnifier').boundingBox())!;
      if (dir === 'ltr') {
        expect(glyphBox.x + glyphBox.width).toBeLessThanOrEqual(buttonBox.x + EPSILON);
      } else {
        expect(glyphBox.x).toBeGreaterThanOrEqual(buttonBox.x + buttonBox.width - EPSILON);
      }
    });

    test('the chosen row draws its check at the inline end of a one-line row', async ({
      page,
    }) => {
      await page.getByTestId('picker-populated').locator('.tm-entity-picker__input').click();
      await expect(chosenRow(page)).toHaveCount(1);
      await appStable(page);
      await expectCheckGlyph(chosenRow(page), dir);
    });

    test('the chosen row draws its check at the inline end of a two-line row', async ({
      page,
    }) => {
      const input = page.getByTestId('picker-centers').locator('.tm-entity-picker__input');
      await input.fill('INV');
      await expect(page.locator('.tm-entity-picker__option')).toHaveCount(1);
      await page.keyboard.press('Enter');
      await expect(input).toHaveValue('INV-0011 - نقد');
      // Reopen on the committed value: its row is the chosen one.
      await input.press('Alt+ArrowDown');
      await expect(chosenRow(page)).toHaveCount(1);
      await appStable(page);
      await expectCheckGlyph(chosenRow(page), dir);
    });
  });

  test.describe(`entity picker glyph geometry in a grid cell (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await gotoGrid(page, 'grid-entity', { dir });
    });

    test("the cell's magnifier and its dropdown's check keep their boxes", async ({ page }) => {
      await activateCell(page, 0, 1); // row 0's agent is Bob Stone
      await page.keyboard.press('Alt+ArrowDown');
      await expect(chosenRow(page)).toHaveCount(1);
      await appStable(page);
      await expectCheckGlyph(chosenRow(page), dir);
      await expectMagnifierGlyph(page.locator('[data-tm-editor] .tm-entity-picker__magnifier'));
    });

    test("a two-line dropdown's check spans both lines at the inline end", async ({ page }) => {
      await activateCell(page, 0, 4); // row 0's Center is INV-0011
      await page.keyboard.press('Alt+ArrowDown');
      await expect(chosenRow(page)).toHaveCount(1);
      await appStable(page);
      await expectCheckGlyph(chosenRow(page), dir);
    });
  });
}
