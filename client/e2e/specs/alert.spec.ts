// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-alert (DoD 17): axe floor across kinds and themes,
 * the dynamic-insertion announcement mechanism, the hidden kind prefix, and
 * the forced-colors kind boundary.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`alert story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('alert', { theme }));
      await expect(page.getByTestId('kinds')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }

  test('alert story is axe-clean in RTL', async ({ page }) => {
    await page.goto(storyUrl('alert', { dir: 'rtl' }));
    await expect(page.getByTestId('kinds')).toBeVisible();
    await expectNoAxeViolations(page);
  });
});

test.describe('kind is never color-only', () => {
  test('each alert carries a visually-hidden localized kind prefix', async ({ page }) => {
    await page.goto(storyUrl('alert'));
    const alerts = page.getByTestId('kinds').locator('tm-alert');
    const prefixes = ['Info:', 'Success:', 'Warning:', 'Error:'];
    for (let i = 0; i < prefixes.length; i++) {
      const prefix = alerts.nth(i).locator('.tm-alert__sr-kind');
      await expect(prefix).toHaveText(prefixes[i]);
      // Visually hidden, present in the accessibility tree.
      const box = await prefix.boundingBox();
      expect(box!.width).toBeLessThanOrEqual(1);
    }
  });
});

test.describe('dynamic insertion announces (DoD 17)', () => {
  test('a fresh live alert renders role="alert" whose text arrives as a region mutation', async ({
    page,
  }) => {
    await page.goto(storyUrl('alert'));
    await expect(page.getByTestId('dynamic-alert')).toHaveCount(0);

    await page.getByTestId('insert-alert').click();
    const region = page.getByTestId('dynamic-alert').locator('.tm-alert__content');
    await expect(region).toHaveAttribute('role', 'alert');
    // Populated content (post-microtask) including the hidden prefix.
    await expect(region).toContainText('Error:');
    await expect(region).toContainText('Saving failed — try again.');
  });
});

test.describe('forced-colors (DoD 17)', () => {
  test('kind distinction survives via border + prefix, not tint alone', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('alert'));
    const alerts = page.getByTestId('kinds').locator('tm-alert');
    for (let i = 0; i < (await alerts.count()); i++) {
      const borderStyle = await alerts.nth(i).evaluate((el) => getComputedStyle(el).borderStyle);
      expect(borderStyle).toBe('solid');
    }
  });
});
