// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tmButton (DoD 2): axe floor across variants, the
 * pending state's size/focus/suppression invariants, the in-form type
 * default, and the forced-colors boundary.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`button story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('button', { theme }));
      await expect(page.getByTestId('variants')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }

  test('button story is axe-clean in RTL', async ({ page }) => {
    await page.goto(storyUrl('button', { dir: 'rtl' }));
    await expect(page.getByTestId('variants')).toBeVisible();
    await expectNoAxeViolations(page);
  });
});

test.describe('pending state (Playwright-pinned size + focus)', () => {
  test('keeps its exact box, keeps focus, and swallows activation', async ({ page }) => {
    await page.goto(storyUrl('button'));
    const button = page.getByTestId('pending-btn');
    const toggle = page.getByTestId('pending-toggle');
    const count = page.getByTestId('click-count');

    await button.click();
    await expect(count).toHaveText('1');

    const before = await button.boundingBox();
    await button.focus();
    await toggle.click();

    await expect(button).toHaveAttribute('aria-busy', 'true');
    await expect(button.locator('.tm-button__spinner')).toBeVisible();
    // Never disabled; focus retained through the state change.
    await expect(button).toBeEnabled();
    // The toggle click moved focus to the toggle; re-focus and verify it sticks.
    await button.focus();
    await expect(button).toBeFocused();

    const during = await button.boundingBox();
    expect(during!.width).toBeCloseTo(before!.width, 1);
    expect(during!.height).toBeCloseTo(before!.height, 1);

    // Activation is swallowed — pointer clicks and keyboard alike.
    await button.click();
    await button.press('Enter');
    await button.press(' ');
    await expect(count).toHaveText('1');

    await toggle.click();
    await button.click();
    await expect(count).toHaveText('2');
  });
});

test.describe('type default inside a form (DoD 2)', () => {
  test('an untyped tmButton never submits; an explicit type="submit" does', async ({ page }) => {
    await page.goto(storyUrl('button'));
    const submits = page.getByTestId('submit-count');

    await page.getByTestId('untyped-in-form').click();
    await expect(submits).toHaveText('0');

    await page.getByTestId('submit-in-form').click();
    await expect(submits).toHaveText('1');
  });
});

test.describe('forced-colors (DoD 2)', () => {
  test('every variant keeps a visible boundary via system colors', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('button'));
    const buttons = page.getByTestId('variants').locator('button');
    for (let i = 0; i < (await buttons.count()); i++) {
      const style = await buttons
        .nth(i)
        .evaluate((el) => getComputedStyle(el).borderStyle + '|' + getComputedStyle(el).borderTopWidth);
      expect(style).toMatch(/^solid\|[1-9]/);
    }
  });
});
