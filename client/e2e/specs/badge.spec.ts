// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-badge: the axe floor in both themes and directions,
 * and labels whose descenders and Arabic dots are not clipped.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`badge story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('badge', { theme, dir }));
        await expect(page.getByTestId('badge-error')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('badge label', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`keeps descenders and the dots under Arabic letters inside its box (${dir})`, async ({
      page,
    }) => {
      await page.goto(storyUrl('badge', { dir }));
      for (const testId of ['badge-descenders', 'badge-arabic']) {
        const label = page.getByTestId(testId).locator('.tm-badge__label');
        // The label clips (for its ellipsis), so its content must fit its
        // box: nothing it paints may reach past the clip.
        const fit = await label.evaluate((el) => ({
          content: el.scrollHeight,
          box: el.clientHeight,
        }));
        expect(fit.content, testId).toBeLessThanOrEqual(fit.box);
        // And the pill keeps its 20 px height around the taller line box.
        expect((await page.getByTestId(testId).boundingBox())!.height, testId).toBe(20);
      }
    });
  }
});
