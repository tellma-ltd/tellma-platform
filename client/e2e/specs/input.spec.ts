// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tmInput + tm-form-field (DoD 4/15): axe floor,
 * live-region mechanism, focus-ring, forced-colors, reduced-motion, and the
 * bidi dir="auto" behavior under both directions.
 */

test.describe('axe floor (DoD 4)', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`input story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('input', { theme }));
      await expect(page.getByTestId('input-email')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('error display + live region mechanism (§6)', () => {
  test('the error region is persistent and live; the hint stays put beside it', async ({
    page,
  }) => {
    await page.goto(storyUrl('input'));
    const field = page.getByTestId('ff-email');
    const input = page.getByTestId('input-email');
    const error = field.locator('.tm-form-field__error');
    const hint = field.locator('.tm-form-field__hint');

    // The live region exists BEFORE it holds text (persistent element).
    await expect(error).toHaveAttribute('aria-live', 'polite');
    await expect(error).toHaveAttribute('aria-atomic', 'true');
    await expect(error).toHaveText('');
    await expect(hint).toBeVisible();

    await input.click();
    await page.keyboard.press('Tab');

    await expect(error).toHaveText('This field is required');
    // The hint is the field's standing instruction and does NOT give way to
    // the error: it is most useful exactly when the value is wrong, and the
    // error is a popover that needs no room from it.
    await expect(hint).toBeVisible();
    await expect(input).toHaveAttribute('aria-invalid', 'true');

    // Both ids are described, hint first, and the last one carries the message.
    const describedBy = (await input.getAttribute('aria-describedby'))!.split(' ');
    await expect(page.locator(`[id="${describedBy.at(-1)}"]`)).toHaveText(
      'This field is required',
    );
  });

  test('the bubble follows focus; the message never leaves the accessibility tree', async ({
    page,
  }) => {
    await page.goto(storyUrl('form-field'));
    const field = page.getByTestId('ff-error-blur');
    const input = field.locator('input');
    const bubble = page.locator('tm-error-popover');

    // Invalid from the start, and blurred: the border and the in-field glyph
    // are what say so, and the words are still described.
    await expect(field).toHaveClass(/tm-form-field--invalid/);
    await expect(field.locator('.tm-form-field__error-icon')).toBeAttached();
    await expect(field.locator('.tm-form-field__error')).toHaveText('Select an account.');
    await expect(bubble).toHaveCount(0);

    await input.focus();
    await expect(bubble).toHaveCount(1);
    // Decoration only: the live region above already carries these words.
    await expect(bubble).toHaveAttribute('aria-hidden', 'true');

    // Move to a VALID field, not just anywhere: Tab would land on the next
    // invalid tile and open its bubble, and the locator matches any of them.
    await page.getByTestId('ff-default').locator('input').focus();
    await expect(bubble).toHaveCount(0);
    await expect(field.locator('.tm-form-field__error')).toHaveText('Select an account.');
  });

  test('pressing the in-field glyph focuses the control and brings the bubble back', async ({
    page,
  }) => {
    await page.goto(storyUrl('form-field'));
    const field = page.getByTestId('ff-error-blur');
    const glyph = field.locator('.tm-form-field__error-icon');
    await expect(page.locator('tm-error-popover')).toHaveCount(0);

    // The glyph is pointer-transparent, so Playwright's own click refuses it;
    // a press at its coordinates is what a reader actually does, and it has
    // to land on the field beneath rather than on nothing.
    const box = (await glyph.boundingBox())!;
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);

    await expect(field.locator('input')).toBeFocused();
    await expect(page.locator('tm-error-popover')).toHaveCount(1);
  });

  test('a page-shift gate: going invalid moves nothing below the field', async ({ page }) => {
    await page.goto(storyUrl('form-field'));
    const later = page.getByTestId('ff-nolabel');
    const before = (await later.boundingBox())!.y;

    await page.getByTestId('ff-required').locator('input').click();
    await page.keyboard.press('Tab');
    await expect(page.getByTestId('ff-required')).toHaveClass(/tm-form-field--invalid/);

    // The whole reason the message is a popover.
    expect((await later.boundingBox())!.y).toBe(before);
  });
});

test.describe('focus ring (§6)', () => {
  test('keyboard focus draws the box ring; the inner input has no double outline', async ({
    page,
  }) => {
    await page.goto(storyUrl('input'));
    const input = page.getByTestId('input-email');
    const box = page.getByTestId('ff-email').locator('.tm-form-field__box');

    await input.focus();
    // Auto-retrying reads: the border-color transitions over --duration-fast,
    // so an instant getComputedStyle can catch an interpolated color on slow CI.
    await expect
      .poll(() => box.evaluate((el) => getComputedStyle(el).boxShadow))
      .not.toBe('none');
    await expect(box).toHaveCSS('border-color', 'rgb(62, 137, 157)'); // --teal-500 focus border
  });
});

test.describe('forced-colors + reduced-motion gates (DoD 15)', () => {
  test('forced-colors keeps the field boundary visible', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('input'));
    const box = page.getByTestId('ff-email').locator('.tm-form-field__box');
    const borderStyle = await box.evaluate((el) => getComputedStyle(el).borderStyle);
    expect(borderStyle).toBe('solid');
  });

  test('reduced-motion disables the pending spinner animation', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('input'));
    const username = page.getByTestId('input-username');
    await username.fill('valid-name');
    // The spinner is TRANSIENT — it lives only while the story's mock
    // async validator is pending (~800ms) — so the style must be read the
    // moment the element appears, in ONE page-side call anchored on the
    // stable field root: a visible-check followed by a separate
    // locator.evaluate can lose the element between the two calls on a
    // loaded machine and stall until the test times out.
    const animation = await page.getByTestId('ff-username').evaluate(
      (field) =>
        new Promise<string | null>((resolve) => {
          const deadline = performance.now() + 15_000;
          const read = (): void => {
            const spinner = field.querySelector('.tm-form-field__spinner');
            if (spinner !== null) {
              resolve(getComputedStyle(spinner).animationName);
            } else if (performance.now() > deadline) {
              resolve(null); // never appeared — fail with a value, not a stall
            } else {
              requestAnimationFrame(read);
            }
          };
          read();
        }),
    );
    expect(animation).toBe('none');
  });
});

test.describe('bidi dir="auto" (§7, DoD 15)', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`field base direction follows its own content in a ${dir} root`, async ({ page }) => {
      await page.goto(storyUrl('input', { dir }));

      const arabicFirst = page.getByTestId('input-bidi-ar');
      const englishFirst = page.getByTestId('input-bidi-en');

      await expect(arabicFirst).toHaveAttribute('dir', 'auto');
      // Computed direction resolves from the CONTENT's first strong
      // character — independent of the page direction.
      expect(await arabicFirst.evaluate((el) => getComputedStyle(el).direction)).toBe('rtl');
      expect(await englishFirst.evaluate((el) => getComputedStyle(el).direction)).toBe('ltr');
    });
  }
});

test.describe('textarea host (DoD 3)', () => {
  test('grows the field box, pins resize: none, and wires label/hint/focus ring', async ({
    page,
  }) => {
    await page.goto(storyUrl('input'));
    const field = page.getByTestId('ff-notes');
    const textarea = page.getByTestId('textarea-notes');
    const box = field.locator('.tm-form-field__box');

    // Fixed size: no user resize handle; height driven by the authored rows.
    await expect(textarea).toHaveCSS('resize', 'none');
    await expect(textarea).toHaveJSProperty('rows', 4);

    // The box grew past the single-line field height to hold four rows.
    const singleLineBox = await page
      .getByTestId('ff-email')
      .locator('.tm-form-field__box')
      .boundingBox();
    const grownBox = await box.boundingBox();
    expect(grownBox!.height).toBeGreaterThan(singleLineBox!.height * 2);

    // Label click focuses the textarea (label[for] association).
    await field.locator('label').click();
    await expect(textarea).toBeFocused();

    // The focus ring wraps the grown box.
    await expect
      .poll(() => box.evaluate((el) => getComputedStyle(el).boxShadow))
      .not.toBe('none');
  });
});
