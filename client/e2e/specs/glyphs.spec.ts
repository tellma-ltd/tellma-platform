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
  inlineEndGap,
  measure,
} from '../support/glyph-geometry';
import { checkAllBox, rowCheckbox } from '../support/grid';
import { animationsSettled, appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Geometry battery for the foundation's built-in glyphs: the checkbox's
 * check and dash, the select's caret and option check, and the form field's
 * invalid mark. Each glyph keeps its box (size, centring, inline-end inset)
 * and its stroke in both directions, and a checked or mixed tm-checkbox
 * paints the same pixels as the grid's row checkbox in the same state.
 *
 * Every assertion derives its expectation from the computed tokens and
 * paddings of the glyph's container, so a token change moves the
 * expectation with it; what must never happen is the glyph drifting away
 * from the box its container gives it.
 */

/** Opens the story's select and waits for its rows. */
async function openSelect(page: Page): Promise<Locator> {
  await page.getByTestId('select').locator('.tm-select__trigger').click();
  const panel = page.locator('.tm-select__panel');
  await expect(panel.getByRole('option')).toHaveCount(3);
  await appStable(page);
  return panel;
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`glyph geometry (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await page.goto(storyUrl('glyphs', { dir }));
      await expect(page.getByTestId('cb-checked')).toBeVisible();
    });

    test('the checkbox check and dash are centred in the box at the glyph size', async ({
      page,
    }) => {
      for (const [testid, geometry] of [
        ['cb-checked', ['d=M20 6 9 17l-5-5']],
        ['cb-mixed', ['d=M5 12h14']],
      ] as const) {
        const checkbox = page.getByTestId(testid);
        const measured = await measure(
          checkbox.locator('.tm-checkbox__glyph'),
          checkbox.locator('.tm-checkbox__box'),
          '--checkbox-glyph-size',
        );
        expectTokenSquare(measured);
        expectLucideStroke(measured, geometry);
        expect(measured.shapes.every((shape) => shape.painted)).toBe(true);
        expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
        const inlineCentre = Math.abs(
          measured.svg.x +
            measured.svg.width / 2 -
            (measured.container.x + measured.container.width / 2),
        );
        expect(inlineCentre).toBeLessThanOrEqual(EPSILON);
      }
    });

    test('the select caret sits at the trigger inline end, centred, and turns over on open', async ({
      page,
    }) => {
      const select = page.getByTestId('select');
      const caret = select.locator('.tm-select__caret');
      const measured = await measure(
        caret,
        select.locator('.tm-select__trigger'),
        '--select-check-size',
      );
      expectTokenSquare(measured);
      expectLucideStroke(measured, ['d=m6 9 6 6 6-6']);
      expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
      expect(
        Math.abs(inlineEndGap(measured, dir) - measured.containerEndInset),
      ).toBeLessThanOrEqual(EPSILON);
      expect(await caret.evaluate((el) => getComputedStyle(el).transform)).toBe('none');

      await openSelect(page);
      await animationsSettled(caret);
      // A half turn: the caret points up while the panel is open.
      expect(await caret.evaluate((el) => new DOMMatrix(getComputedStyle(el).transform).a)).toBe(
        -1,
      );
    });

    test('the option check sits at the row inline end, shown only on the selected row', async ({
      page,
    }) => {
      const panel = await openSelect(page);
      const rows = panel.getByRole('option');
      for (let index = 0; index < 3; index++) {
        const row = rows.nth(index);
        const measured = await measure(
          row.locator('.tm-option__check'),
          row,
          '--select-check-size',
        );
        // The box is reserved on every row, so selecting never shifts a label.
        expectTokenSquare(measured);
        expectLucideStroke(measured, ['d=M20 6 9 17l-5-5']);
        expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
        expect(
          Math.abs(inlineEndGap(measured, dir) - measured.containerEndInset),
        ).toBeLessThanOrEqual(EPSILON);
        const selected = (await row.getAttribute('aria-selected')) === 'true';
        expect(measured.shapes.every((shape) => shape.painted)).toBe(selected);
      }
    });

    test('the multi-select row checkbox sits at the row inline end, ticked only when selected', async ({
      page,
    }) => {
      await page.getByTestId('multi').locator('.tm-select__trigger').click();
      const rows = page.locator('.tm-select__panel').getByRole('option');
      await expect(rows).toHaveCount(3);
      await appStable(page);
      for (let index = 0; index < 3; index++) {
        const row = rows.nth(index);
        const box = row.locator('.tm-option__box');
        // The box is tm-checkbox's box, at the row's inline end.
        const boxMeasure = await measure(
          box.locator('.tm-option__box-check'),
          box,
          '--checkbox-glyph-size',
        );
        const boxSize = await box.evaluate((el) =>
          parseFloat(getComputedStyle(el).getPropertyValue('--checkbox-box-size')),
        );
        expect(Math.abs(boxMeasure.container.width - boxSize)).toBeLessThanOrEqual(EPSILON);
        expect(Math.abs(boxMeasure.container.height - boxSize)).toBeLessThanOrEqual(EPSILON);
        const rowMeasure = await measure(box, row, '--checkbox-box-size');
        expect(blockCentreOffset(rowMeasure)).toBeLessThanOrEqual(EPSILON);
        expect(
          Math.abs(inlineEndGap(rowMeasure, dir) - rowMeasure.containerEndInset),
        ).toBeLessThanOrEqual(EPSILON);
        // The tick: the checkbox glyph, centred in the box.
        expectTokenSquare(boxMeasure);
        expectLucideStroke(boxMeasure, ['d=M20 6 9 17l-5-5']);
        expect(blockCentreOffset(boxMeasure)).toBeLessThanOrEqual(EPSILON);
        const selected = (await row.getAttribute('aria-selected')) === 'true';
        expect(boxMeasure.shapes.every((shape) => shape.painted)).toBe(selected);
      }
    });

    test('the invalid mark sits at the field box inline end, centred', async ({ page }) => {
      const field = page.getByTestId('ff-error');
      const measured = await measure(
        field.locator('.tm-form-field__error-icon'),
        field.locator('.tm-form-field__box'),
        '--field-error-icon-size',
      );
      expectTokenSquare(measured);
      expectLucideStroke(measured, [
        'cx=12 cy=12 r=10',
        'x1=12 x2=12 y1=8 y2=12',
        'x1=12 x2=12.01 y1=16 y2=16',
      ]);
      expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
      expect(
        Math.abs(inlineEndGap(measured, dir) - measured.containerEndInset),
      ).toBeLessThanOrEqual(EPSILON);
    });

    test('a tm-checkbox paints the same check and dash as the grid row checkbox', async ({
      page,
    }) => {
      // Select one row: its checkbox draws the check, and the header's
      // select-all turns to the dash.
      await rowCheckbox(page, 0).click();
      await expect(rowCheckbox(page, 0)).toHaveClass(/tm-grid__check--on/);
      await expect(checkAllBox(page)).toHaveClass(/tm-grid__check--mixed/);
      // No hover or press state may tint either box.
      await page.mouse.move(0, 0);
      await appStable(page);

      for (const [testid, gridBox] of [
        ['cb-checked', rowCheckbox(page, 0)],
        ['cb-mixed', checkAllBox(page)],
      ] as const) {
        const difference = await comparePixels(
          page,
          page.getByTestId(testid).locator('.tm-checkbox__box'),
          gridBox,
        );
        expect(difference, `${testid} against the grid's checkbox`).toEqual({
          size: '14x14',
          differingPixels: 0,
        });
      }
    });
  });
}

/**
 * Screenshots the glyph square at the centre of two checkbox boxes (the
 * glyph-size square, clear of the rounded border, whose anti-aliasing
 * shows whatever lies behind each box) and compares them pixel by pixel in
 * the page, counting the pixels whose channels differ by more than one
 * step of rounding.
 */
async function comparePixels(
  page: Page,
  a: Locator,
  b: Locator,
): Promise<{ size: string; differingPixels: number }> {
  const glyph = await a.evaluate((el) =>
    parseFloat(getComputedStyle(el).getPropertyValue('--checkbox-glyph-size')),
  );
  const shoot = async (box: Locator) => {
    const r = (await box.boundingBox())!;
    // Whole device pixels: a fractional clip would be resampled, and the
    // two shots would then differ by the resampling rather than the paint.
    const clip = {
      x: Math.round(r.x + (r.width - glyph) / 2),
      y: Math.round(r.y + (r.height - glyph) / 2),
      width: glyph,
      height: glyph,
    };
    return (await page.screenshot({ clip, animations: 'disabled' })).toString('base64');
  };
  const [first, second] = [await shoot(a), await shoot(b)];
  return page.evaluate(
    async ([one, two]) => {
      const decode = async (base64: string) => {
        const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
        const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
        const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
        const context = canvas.getContext('2d')!;
        context.drawImage(bitmap, 0, 0);
        return context.getImageData(0, 0, bitmap.width, bitmap.height);
      };
      const [x, y] = [await decode(one), await decode(two)];
      if (x.width !== y.width || x.height !== y.height) {
        return {
          size: `${x.width}x${x.height} against ${y.width}x${y.height}`,
          differingPixels: -1,
        };
      }
      let differing = 0;
      for (let i = 0; i < x.data.length; i += 4) {
        const channels = [0, 1, 2, 3].map((c) => Math.abs(x.data[i + c] - y.data[i + c]));
        if (Math.max(...channels) > 1) {
          differing++;
        }
      }
      return { size: `${x.width}x${x.height}`, differingPixels: differing };
    },
    [first, second] as const,
  );
}
