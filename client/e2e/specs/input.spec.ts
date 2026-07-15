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
  test('hint swaps to error after blur; the error element is a persistent polite region', async ({
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
    await expect(hint).toBeHidden();
    await expect(input).toHaveAttribute('aria-invalid', 'true');

    // describedby resolves to the element carrying the message.
    const describedBy = await input.getAttribute('aria-describedby');
    await expect(page.locator(`[id="${describedBy}"]`)).toHaveText('This field is required');
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
    const spinner = page.getByTestId('ff-username').locator('.tm-form-field__spinner');
    await expect(spinner).toBeVisible();
    const animation = await spinner.evaluate((el) => getComputedStyle(el).animationName);
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
