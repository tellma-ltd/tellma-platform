// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tmNumber (DoD 4): axe floor, the physical right
 * alignment (RTL included), the live locale reformat, and the commit
 * round-trip through a real keyboard.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`number story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('number', { theme }));
      await expect(page.getByTestId('input-amount')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('typed entry commits the display-rounded value (DoD 4)', () => {
  test('typing then blurring reformats and rounds the model', async ({ page }) => {
    await page.goto(storyUrl('number'));
    const amount = page.getByTestId('input-amount');

    await amount.fill('1234.567');
    await amount.blur();
    await expect(amount).toHaveValue('1,234.57');
    await expect(page.getByTestId('model-json')).toContainText('"amount":1234.57');
  });

  test('percent entry: 15.5 → 15.5% displayed, 0.155 in the model', async ({ page }) => {
    await page.goto(storyUrl('number'));
    const discount = page.getByTestId('input-discount');
    await expect(discount).toHaveValue('15.5%');

    await discount.fill('20');
    await discount.blur();
    await expect(discount).toHaveValue('20%');
    await expect(page.getByTestId('model-json')).toContainText('"discount":0.2');
  });

  test('an over-precision entry is rejected with the envelope message', async ({ page }) => {
    await page.goto(storyUrl('number'));
    const free = page.getByTestId('input-free');
    await free.fill('12345678901234567');
    await free.blur();
    await expect(page.getByTestId('ff-free').locator('.tm-form-field__error')).toContainText(
      'at most 15 digits',
    );
    await expect(page.getByTestId('model-json')).toContainText('"free":null');
  });
});

test.describe('physical right alignment (§5, DoD 4)', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`numerals stay right-aligned in ${dir}`, async ({ page }) => {
      await page.goto(storyUrl('number', { dir }));
      const amount = page.getByTestId('input-amount');
      expect(await amount.evaluate((el) => getComputedStyle(el).textAlign)).toBe('right');
    });
  }
});

test.describe('live locale switch (DoD 4/18)', () => {
  test('the display reformats in place; the model never moves', async ({ page }) => {
    await page.goto(storyUrl('number'));
    const amount = page.getByTestId('input-amount');
    await expect(amount).toHaveValue('1,483.80');

    await page.getByTestId('lang-ar').click();
    // Arabic locale renders Arabic-Indic digits with the Arabic decimal mark.
    await expect(amount).not.toHaveValue('1,483.80');
    await expect(page.getByTestId('model-json')).toContainText('"amount":1483.8');

    await page.getByTestId('lang-en').click();
    await expect(amount).toHaveValue('1,483.80');
  });
});
