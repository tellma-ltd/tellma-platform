// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-kbd and the kbd helpers: the axe floor in both
 * themes and directions, the 18px caps keeping keyboard order in RTL, and
 * real keydowns (plus an Arabic-layout keydown) matched through the story's
 * key tester.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`kbd story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('kbd', { theme }));
      await expect(page.getByTestId('bindings')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }

  test('kbd story is axe-clean in RTL', async ({ page }) => {
    await page.goto(storyUrl('kbd', { dir: 'rtl' }));
    await expect(page.getByTestId('bindings')).toBeVisible();
    await expectNoAxeViolations(page);
  });
});

test.describe('caps', () => {
  test('are 18px tall, never narrower than tall, and labelled per platform', async ({ page }) => {
    await page.goto(storyUrl('kbd'));
    const row = page.locator('tr[data-keys="Mod+Shift+D"]');
    await expect(row.getByTestId('windows').locator('kbd')).toHaveText(['Ctrl', 'Shift', 'D']);
    await expect(row.getByTestId('apple').locator('kbd')).toHaveText(['⇧', '⌘', 'D']);

    const letter = (await row.getByTestId('windows').locator('kbd').nth(2).boundingBox())!;
    expect(letter.height).toBe(18);
    // A one-character cap is (about) square: the min width holds narrow
    // glyphs, and a wide one adds no more than its own overflow.
    expect(letter.width).toBeGreaterThanOrEqual(18);
    expect(letter.width).toBeLessThan(20);
    const name = (await row.getByTestId('windows').locator('kbd').nth(1).boundingBox())!;
    expect(name.height).toBe(18);
    expect(name.width).toBeGreaterThan(18);
  });

  test('keep keyboard order, left to right, inside right-to-left text', async ({ page }) => {
    await page.goto(storyUrl('kbd', { dir: 'rtl' }));
    const caps = page.getByTestId('sentence-kbd').locator('kbd');
    await expect(caps).toHaveCount(2);
    const first = (await caps.nth(0).boundingBox())!;
    const second = (await caps.nth(1).boundingBox())!;
    expect(first.x).toBeLessThan(second.x);
    await expect(page.getByTestId('sentence-kbd')).toHaveAttribute('dir', 'ltr');
  });
});

test.describe('matching real keydowns', () => {
  /** Whether the browser under test reports an Apple platform. */
  function isApple(page: Page): Promise<boolean> {
    return page.evaluate(() => /^(mac|iphone|ipad|ipod)/i.test(navigator.platform));
  }

  test('Mod+K, a shifted symbol and Mod+Alt+Plus from the keyboard', async ({ page }) => {
    await page.goto(storyUrl('kbd'));
    const tester = page.getByTestId('key-tester');
    await tester.focus();

    await page.keyboard.press('ControlOrMeta+KeyK');
    await expect(page.getByTestId('matched')).toHaveText('Mod+K');

    // '?' is Shift+/ on the US layout Playwright types with.
    await page.keyboard.press('Shift+Slash');
    await expect(page.getByTestId('last-key')).toHaveText('?');
    await expect(page.getByTestId('matched')).toHaveText('?');

    await page.keyboard.press('ControlOrMeta+Alt+NumpadAdd');
    await expect(page.getByTestId('matched')).toHaveText('Mod+Alt+Plus');

    await page.keyboard.press('F2');
    await expect(page.getByTestId('matched')).toHaveText('F2');
  });

  test('an Arabic-layout keydown matches by the physical key', async ({ page }) => {
    await page.goto(storyUrl('kbd'));
    const apple = await isApple(page);
    const tester = page.getByTestId('key-tester');
    await tester.focus();

    // What Ctrl+D sends on an Arabic layout: the letter ي from the D key.
    await tester.dispatchEvent('keydown', {
      key: 'ي',
      code: 'KeyD',
      ctrlKey: !apple,
      metaKey: apple,
      bubbles: true,
    });
    await expect(page.getByTestId('last-key')).toHaveText('ي');
    await expect(page.getByTestId('matched')).toHaveText('Mod+D');

    // And Ctrl+/ sends ظ from the Slash key.
    await tester.dispatchEvent('keydown', {
      key: 'ظ',
      code: 'Slash',
      ctrlKey: !apple,
      metaKey: apple,
      bubbles: true,
    });
    await expect(page.getByTestId('matched')).toHaveText('Mod+/');
  });
});
