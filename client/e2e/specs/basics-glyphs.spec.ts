// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';
import {
  Calendar,
  Camera,
  Check,
  ChevronLeft,
  ChevronRight,
  CircleAlert,
  CircleCheckBig,
  CircleX,
  Crop,
  FileText,
  Image as ImageGlyph,
  ImageOff,
  Info,
  Trash2,
  TriangleAlert,
  Upload,
  X,
} from 'lucide';

import {
  blockCentreOffset,
  EPSILON,
  expectLucideStroke,
  expectTokenSquare,
  inlineCentreOffset,
  inlineEndGap,
  inlineStartGap,
  isMirrored,
  lucideGeometry,
  measure,
  type GlyphMeasure,
} from '../support/glyph-geometry';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Geometry battery for the basic components' built-in glyphs: the alert
 * tones, the date picker's invalid mark, calendar button and popup
 * chevrons, the dropzone, the image's error, empty, edit-chrome and
 * edit-pane glyphs, the modal's close X, the file preview's cards and the
 * validation bubble's mark. Each glyph keeps its box (the size token its
 * container gives it, and its place in that container) and its stroke, in
 * both directions; the popup chevrons point the way their buttons page,
 * across a live direction flip too.
 *
 * Every expectation derives from the computed tokens and paddings of the
 * glyph's container and from the Lucide data the glyph draws, so a token
 * change moves the expectation with it.
 */

/** The glyph a container holds: its tm-icon (or a bare inline svg). */
function glyphIn(container: Locator): Locator {
  return container.locator(':scope > :is(tm-icon, svg)');
}

/** Asserts a glyph is a centred square of its token, drawing `node`. */
function expectCentredGlyph(
  measured: GlyphMeasure,
  node: Parameters<typeof lucideGeometry>[0],
  strokeWidth?: string,
): void {
  expectTokenSquare(measured);
  expectLucideStroke(measured, lucideGeometry(node), strokeWidth);
  expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
  expect(inlineCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
}

/** Opens the date picker's popup and waits for its header. */
async function openDatePopup(page: Page): Promise<Locator> {
  await page.getByTestId('date').locator('.tm-date-picker__toggle').click();
  const popup = page.locator('.tm-date-popup');
  await expect(popup.locator('.tm-date-popup__nav')).toHaveCount(2);
  await appStable(page);
  return popup;
}

/** Flips the document direction the way an app's language switch does. */
async function flipDocumentTo(page: Page, dir: 'ltr' | 'rtl'): Promise<void> {
  await page.evaluate((value) => {
    document.documentElement.dir = value;
  }, dir);
}

/**
 * Whether the popup's previous and next chevrons point outward, toward the
 * edges their buttons sit at: "previous" at the inline start points to it.
 * A chevron points to the side its tip is on: the tip's side in the drawing,
 * flipped when the glyph is drawn mirrored.
 */
async function chevronsPointOutward(popup: Locator): Promise<boolean> {
  const navs = popup.locator('.tm-date-popup__nav');
  const pointsLeft = async (nav: Locator): Promise<boolean> => {
    const tipOnLeft = await nav.locator('svg path').evaluate((path: SVGPathElement) => {
      // A chevron's tip is the middle point of its two equal strokes.
      const tip = path.getPointAtLength(path.getTotalLength() / 2);
      return tip.x < 12;
    });
    return tipOnLeft !== (await isMirrored(glyphIn(nav)));
  };
  const first = (await navs.nth(0).boundingBox())!;
  const second = (await navs.nth(1).boundingBox())!;
  const previousIsLeft = first.x < second.x;
  const previousPointsLeft = await pointsLeft(navs.nth(0));
  const nextPointsLeft = await pointsLeft(navs.nth(1));
  return previousPointsLeft === previousIsLeft && nextPointsLeft === !previousIsLeft;
}

for (const dir of ['ltr', 'rtl'] as const) {
  test.describe(`basics glyph geometry (${dir})`, () => {
    test.beforeEach(async ({ page }) => {
      await page.goto(storyUrl('basics-glyphs', { dir }));
      await expect(page.getByTestId('alert-info')).toBeVisible();
    });

    test('each alert tone draws its glyph at the inline start, centred on the line', async ({
      page,
    }) => {
      for (const [kind, node] of [
        ['info', Info],
        ['success', CircleCheckBig],
        ['warning', TriangleAlert],
        ['error', CircleX],
      ] as const) {
        const alert = page.getByTestId(`alert-${kind}`);
        const measured = await measure(alert.locator('.tm-alert__icon'), alert, '--alert-icon-size');
        expectTokenSquare(measured);
        expectLucideStroke(measured, lucideGeometry(node));
        expect(blockCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
        expect(
          Math.abs(inlineStartGap(measured, dir) - measured.containerStartInset),
        ).toBeLessThanOrEqual(EPSILON);
      }
    });

    test('the date picker draws its invalid mark and calendar glyph in place', async ({ page }) => {
      const field = page.getByTestId('ff-date');
      const mark = await measure(
        field.locator('.tm-form-field__error-icon'),
        field.locator('.tm-form-field__box'),
        '--field-error-icon-size',
      );
      expectTokenSquare(mark);
      expectLucideStroke(mark, lucideGeometry(CircleAlert));
      expect(blockCentreOffset(mark)).toBeLessThanOrEqual(EPSILON);

      const toggle = field.locator('.tm-date-picker__toggle');
      expectCentredGlyph(await measure(glyphIn(toggle), toggle, '--space-4'), Calendar);
      // The mark sits before the button, toward the inline start.
      const toggleBox = (await toggle.boundingBox())!;
      expect(dir === 'rtl' ? mark.svg.x > toggleBox.x : mark.svg.x < toggleBox.x).toBe(true);
      // And the button closes the field box at its inline end.
      const box = await measure(toggle, field.locator('.tm-form-field__box'), '--space-4');
      expect(
        Math.abs(inlineEndGap(box, dir) - box.containerEndInset),
      ).toBeLessThanOrEqual(EPSILON);
    });

    test('the popup chevrons are centred in their buttons and point the way they page', async ({
      page,
    }) => {
      const popup = await openDatePopup(page);
      const navs = popup.locator('.tm-date-popup__nav');
      for (const [index, node] of [
        [0, ChevronLeft],
        [1, ChevronRight],
      ] as const) {
        const nav = navs.nth(index);
        const measured = await measure(glyphIn(nav), nav, '--date-picker-nav-glyph-size');
        expectCentredGlyph(measured, node);
        expect(await isMirrored(glyphIn(nav))).toBe(dir === 'rtl');
      }
      expect(await chevronsPointOutward(popup)).toBe(true);
    });

    test('the dropzone draws its upload glyph centred above the hint', async ({ page }) => {
      const zone = page.getByTestId('dropzone');
      const measured = await measure(
        zone.locator('.tm-dropzone__icon'),
        zone,
        '--files-dropzone-icon-size',
      );
      expectTokenSquare(measured);
      expectLucideStroke(measured, lucideGeometry(Upload));
      expect(inlineCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
      const hint = (await zone.locator('.tm-dropzone__hint').boundingBox())!;
      expect(measured.svg.y + measured.svg.height).toBeLessThanOrEqual(hint.y + EPSILON);
    });

    test('the image error and empty glyphs are centred in their boxes', async ({ page }) => {
      const error = page.getByTestId('image-error').locator('.tm-image__glyph--error');
      await expect(error).toBeVisible();
      expectCentredGlyph(await measure(glyphIn(error), error, '--image-glyph-size'), ImageOff);

      const empty = page.getByTestId('image-empty').locator('.tm-image__glyph');
      expectCentredGlyph(await measure(glyphIn(empty), empty, '--image-glyph-size'), ImageGlyph);
    });

    test('the image edit chrome and the edit pane check keep their boxes and strokes', async ({
      page,
    }) => {
      const image = page.getByTestId('image-edit');
      await expect(image.locator('.tm-image__img')).toBeVisible();
      for (const [action, node] of [
        ['replace', Camera],
        ['adjust', Crop],
        ['remove', Trash2],
      ] as const) {
        const button = image.locator(`[data-tm-image-action="${action}"]`);
        expectCentredGlyph(
          await measure(glyphIn(button), button, '--image-chrome-icon-size'),
          node,
        );
      }

      await image.locator('[data-tm-image-action="adjust"]').click();
      const done = image.locator('.tm-image__done');
      await expect(done).toBeVisible();
      // The pane's check is heavier than every other glyph.
      expectCentredGlyph(
        await measure(glyphIn(done), done, '--image-crop-done-glyph-size'),
        Check,
        '2.25px',
      );
    });

    test('the modal close X is centred in its button', async ({ page }) => {
      await page.getByTestId('open-modal').click();
      const close = page.locator('.tm-modal__close');
      await expect(close).toBeVisible();
      expectCentredGlyph(await measure(glyphIn(close), close, '--modal-icon-size'), X);
    });

    test('the file preview cards draw their glyphs centred above the message', async ({
      page,
    }) => {
      for (const [testid, node] of [
        ['preview-unsupported', FileText],
        ['preview-error', CircleX],
      ] as const) {
        await page.getByTestId(testid).click();
        const card = page.locator('.tm-preview__card');
        await expect(card).toBeVisible();
        const measured = await measure(
          card.locator('.tm-preview__card-icon'),
          card,
          '--preview-card-icon-size',
        );
        expectTokenSquare(measured);
        expectLucideStroke(measured, lucideGeometry(node));
        expect(inlineCentreOffset(measured)).toBeLessThanOrEqual(EPSILON);
        await page.keyboard.press('Escape');
        await expect(card).toHaveCount(0);
      }
    });

    test('the validation bubble bullets its message at the inline start', async ({ page }) => {
      await page.getByTestId('ff-error-input').focus();
      const message = page.locator('.tm-error-popover__message');
      await expect(message).toBeVisible();
      await appStable(page);
      const measured = await measure(
        message.locator('.tm-error-popover__icon'),
        message,
        '--error-popover-icon-size',
      );
      expectTokenSquare(measured);
      expectLucideStroke(measured, lucideGeometry(CircleAlert));
      expect(
        Math.abs(inlineStartGap(measured, dir) - measured.containerStartInset),
      ).toBeLessThanOrEqual(EPSILON);
      // Centred on the message's first line.
      const lineHeight = await message
        .locator('.tm-error-popover__text')
        .evaluate((el) => parseFloat(getComputedStyle(el).lineHeight));
      const firstLineCentre = measured.container.y + lineHeight / 2;
      expect(
        Math.abs(measured.svg.y + measured.svg.height / 2 - firstLineCentre),
      ).toBeLessThanOrEqual(EPSILON);
    });
  });
}

test('the popup chevrons follow a live direction flip of the open popup', async ({ page }) => {
  await page.goto(storyUrl('basics-glyphs'));
  const popup = await openDatePopup(page);
  const navs = popup.locator('.tm-date-popup__nav');
  expect(await isMirrored(glyphIn(navs.nth(0)))).toBe(false);
  expect(await chevronsPointOutward(popup)).toBe(true);

  await flipDocumentTo(page, 'rtl');
  await expect.poll(() => isMirrored(glyphIn(navs.nth(0)))).toBe(true);
  expect(await chevronsPointOutward(popup)).toBe(true);

  await flipDocumentTo(page, 'ltr');
  await expect.poll(() => isMirrored(glyphIn(navs.nth(0)))).toBe(false);
  expect(await chevronsPointOutward(popup)).toBe(true);
});
