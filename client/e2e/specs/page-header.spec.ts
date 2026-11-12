// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-page-header: the axe floor in both themes and
 * directions, the band wrapping its parameters without clipping any, the
 * end slot packed so hiding its first item moves nothing, the absent
 * rows, and the narrow padding.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`page header story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('page-header', { theme, dir }));
        await expect(page.getByTestId('header-details')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the band wraps its parameters onto several lines without clipping any', async ({ page }) => {
  await page.goto(storyUrl('page-header'));
  const band = page.getByTestId('header-report').locator('.tm-page-header__band');
  const chips = band.locator('tm-chip');
  await expect(chips).toHaveCount(6);
  const bandBox = (await band.boundingBox())!;
  const tops = new Set<number>();
  for (const chip of await chips.all()) {
    const box = (await chip.boundingBox())!;
    tops.add(Math.round(box.y));
    expect(box.x).toBeGreaterThanOrEqual(bandBox.x - 0.5);
    expect(box.x + box.width).toBeLessThanOrEqual(bandBox.x + bandBox.width + 0.5);
    expect(box.y + box.height).toBeLessThanOrEqual(bandBox.y + bandBox.height + 0.5);
  }
  expect(tops.size).toBeGreaterThan(1);
  expect(await band.evaluate((element) => element.scrollWidth <= element.clientWidth)).toBe(true);
});

for (const dir of ['ltr', 'rtl'] as const) {
  test(`hiding the end slot's first item moves nothing after it (${dir})`, async ({ page }) => {
    await page.goto(storyUrl('page-header', { dir }));
    const more = page.getByTestId('end-more');
    const before = (await more.boundingBox())!;
    await page.getByTestId('toggle-share').click();
    await expect(page.getByTestId('end-share')).toHaveCount(0);
    const after = (await more.boundingBox())!;
    expect(after.x).toBe(before.x);
    expect(after.y).toBe(before.y);
  });
}

test('the toolbar row and the band are absent while empty', async ({ page }) => {
  await page.goto(storyUrl('page-header'));
  const plain = page.getByTestId('header-plain');
  await expect(plain.locator('.tm-page-header__toolbar')).toBeHidden();
  await expect(plain.locator('.tm-page-header__band')).toBeHidden();
  await expect(plain.locator('.tm-page-header__back')).toBeHidden();
  await expect(
    page.getByTestId('header-details').locator('.tm-page-header__toolbar'),
  ).toBeVisible();
});

test('pads 12x14 px, and 10 px in a container narrower than 576 px', async ({ page }) => {
  await page.goto(storyUrl('page-header'));
  const padding = (id: string) =>
    page
      .getByTestId(id)
      .locator('.tm-page-header__inner')
      .evaluate((element) => {
        const style = getComputedStyle(element);
        return [style.paddingTop, style.paddingLeft];
      });
  expect(await padding('header-details')).toEqual(['12px', '14px']);
  expect(await padding('header-report')).toEqual(['10px', '10px']);
});
