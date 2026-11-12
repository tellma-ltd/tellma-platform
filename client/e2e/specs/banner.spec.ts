// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-banner-bar: the axe floor in both themes and
 * directions, exactly 32 px tall whatever it holds, a long message cut to
 * one line, the pager's position and wrap-around, the chevrons mirrored in
 * RTL, and the action and dismiss outputs.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`banner story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('banner', { theme, dir }));
        await expect(page.getByTestId('banner-paged')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('geometry', () => {
  test('every bar is exactly 32 px tall, and a long message stays on one line', async ({ page }) => {
    await page.goto(storyUrl('banner'));
    for (const id of [
      'banner-info',
      'banner-warning',
      'banner-error',
      'banner-long',
      'banner-paged',
      'banner-arabic',
    ]) {
      expect((await page.getByTestId(id).boundingBox())!.height, id).toBe(32);
    }
    const message = page.getByTestId('banner-long').locator('.tm-banner-bar__message');
    const cut = await message.evaluate((element) => element.scrollWidth > element.clientWidth);
    expect(cut).toBe(true);
  });

  test('the buttons take the pointer 24 px square', async ({ page }) => {
    await page.goto(storyUrl('banner'));
    const buttons = page.getByTestId('banner-paged').locator('.tm-banner-bar__button');
    await expect(buttons).toHaveCount(3);
    for (const box of await buttons.evaluateAll((all) =>
      all.map((button) => button.getBoundingClientRect().toJSON() as DOMRect),
    )) {
      expect(box.width).toBeGreaterThanOrEqual(24);
      expect(box.height).toBeGreaterThanOrEqual(24);
    }
  });
});

test.describe('pager', () => {
  test('shows its place, and previous and next wrap around', async ({ page }) => {
    await page.goto(storyUrl('banner'));
    const bar = page.getByTestId('banner-paged');
    const position = bar.locator('.tm-banner-bar__position');
    await expect(position).toHaveText('1 of 3');
    await expect(bar).toHaveAttribute('data-tone', 'info');
    await bar.getByRole('button', { name: 'Previous message' }).click();
    await expect(position).toHaveText('3 of 3');
    await expect(bar).toHaveAttribute('data-tone', 'error');
    await expect(bar.locator('.tm-banner-bar__message')).toContainText('You are offline.');
    await bar.getByRole('button', { name: 'Next message' }).click();
    await expect(position).toHaveText('1 of 3');
    await bar.getByRole('button', { name: 'Next message' }).click();
    await expect(position).toHaveText('2 of 3');
  });

  test('the chevrons point along the reading direction (rtl)', async ({ page }) => {
    await page.goto(storyUrl('banner', { dir: 'rtl' }));
    const bar = page.getByTestId('banner-paged');
    const previous = (await bar.locator('.tm-banner-bar__previous').boundingBox())!;
    const next = (await bar.locator('.tm-banner-bar__next').boundingBox())!;
    expect(next.x).toBeLessThan(previous.x);
    const mirrored = await bar
      .locator('.tm-banner-bar__next svg')
      .evaluate((svg) => getComputedStyle(svg).transform);
    expect(mirrored).not.toBe('none');
  });
});

test.describe('outputs', () => {
  test('the action and dismiss buttons emit', async ({ page }) => {
    await page.goto(storyUrl('banner'));
    const bar = page.getByTestId('banner-long');
    await bar.getByRole('button', { name: 'Learn more' }).click();
    await bar.getByRole('button', { name: 'Dismiss' }).click();
    await expect(page.getByTestId('banner-log')).toHaveText('action, dismiss');
  });

  test('a screen reader hears the tone before the message', async ({ page }) => {
    await page.goto(storyUrl('banner'));
    const message = page.getByTestId('banner-error').locator('.tm-banner-bar__message');
    await expect(message).toHaveText(/^\S.*Sync failed\.$/);
    const prefix = await message.locator('.tm-banner-bar__tone').textContent();
    expect(prefix?.trim().length).toBeGreaterThan(0);
  });
});
