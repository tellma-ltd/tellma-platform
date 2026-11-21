// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

// The showcase formats in en-US, whose short wording abbreviates to "min.".

/** 2026-10-20 10:40:20 UTC: twenty seconds into a minute. */
const OPENED = Date.UTC(2026, 9, 20, 10, 40, 20);

/**
 * Browser battery for tm-relative-time: the axe floor in both themes and
 * directions, the wordings, the texts ageing once a minute on the minute
 * boundary under a controlled clock, and no tick while the page is hidden.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`relative-time story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('relative-time', { theme, dir }));
        await expect(page.getByTestId('minutes')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('text', () => {
  test('a time element per age, in each wording', async ({ page }) => {
    await page.clock.install({ time: OPENED });
    await page.goto(storyUrl('relative-time'));
    const minutes = page.getByTestId('minutes').locator('time');
    await expect(minutes).toHaveText('2 min. ago');
    // The story stamps its instants when it opens, a moment after the clock
    // starts, so only the minute is fixed.
    await expect(minutes).toHaveAttribute('datetime', /^2026-10-20T10:38:\d{2}\.\d{3}Z$/);
    await expect(page.getByTestId('long')).toHaveText('2 minutes ago');
    await expect(page.getByTestId('short')).toHaveText('2 min. ago');
  });

  test('ages once a minute, on the minute boundary', async ({ page }) => {
    await page.clock.install({ time: OPENED });
    await page.goto(storyUrl('relative-time'));
    const minutes = page.getByTestId('minutes');
    await expect(minutes).toHaveText('2 min. ago');

    // The first boundary is 40 s away; the instant is then 2 min 40 s old.
    await page.clock.runFor(40_000);
    await page.clock.runFor(60_000);
    await expect(minutes).toHaveText('3 min. ago');
    await page.clock.runFor(60_000);
    await expect(minutes).toHaveText('4 min. ago');
  });

  test('does not tick while the page is hidden, and catches up when shown', async ({ page }) => {
    await page.clock.install({ time: OPENED });
    await page.goto(storyUrl('relative-time'));
    const minutes = page.getByTestId('minutes');
    await expect(minutes).toHaveText('2 min. ago');

    // Hide the page: the ticker stops scheduling.
    const setVisibility = (state: 'hidden' | 'visible') =>
      page.evaluate((value) => {
        Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => value });
        document.dispatchEvent(new Event('visibilitychange'));
      }, state);
    await setVisibility('hidden');
    await page.clock.runFor(10 * 60_000);
    await expect(minutes).toHaveText('2 min. ago');

    await setVisibility('visible');
    await expect(minutes).toHaveText('12 min. ago');
  });
});
