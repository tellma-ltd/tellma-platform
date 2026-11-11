// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-skeleton: the axe floor in both themes and
 * directions, the shapes' geometry (8 px bars, the short last line, a
 * circle, a table at the grid's row height), and no motion.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`skeleton story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('skeleton', { theme, dir }));
        await expect(page.getByTestId('table')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('geometry', () => {
  test('lines are 8 px bars, the last at 60 % of the width', async ({ page }) => {
    await page.goto(storyUrl('skeleton'));
    const bars = page.getByTestId('lines').locator('.tm-skeleton__bar');
    await expect(bars).toHaveCount(3);
    const first = (await bars.nth(0).boundingBox())!;
    const last = (await bars.nth(2).boundingBox())!;
    expect(first.height).toBe(8);
    expect(first.width).toBe(320);
    expect(last.width).toBe(192);
    const lone = (await page.getByTestId('line').locator('.tm-skeleton__bar').boundingBox())!;
    expect(lone.width).toBe(160);
  });

  test('a circle, a block and a table at the grid row height', async ({ page }) => {
    await page.goto(storyUrl('skeleton'));
    const circle = (await page.getByTestId('circle').boundingBox())!;
    expect(circle.width).toBe(24);
    expect(circle.height).toBe(24);
    const block = (await page.getByTestId('block').boundingBox())!;
    expect(block.width).toBe(320);
    expect(block.height).toBe(120);

    // The default size step is sm: 30 px rows, a header row plus five.
    const rows = page.getByTestId('table').locator('.tm-skeleton__row');
    await expect(rows).toHaveCount(6);
    for (let index = 0; index < 6; index++) {
      expect((await rows.nth(index).boundingBox())!.height).toBe(30);
    }
    await expect(rows.nth(1).locator('.tm-skeleton__bar')).toHaveCount(4);
  });

  test('is hidden from assistive technology and does not move', async ({ page }) => {
    await page.goto(storyUrl('skeleton'));
    await expect(page.getByTestId('table')).toHaveAttribute('aria-hidden', 'true');
    const motion = await page.getByTestId('lines').evaluate((el) => {
      const bar = el.querySelector('.tm-skeleton__bar')!;
      return getComputedStyle(bar).animationName;
    });
    expect(motion).toBe('none');
  });
});
