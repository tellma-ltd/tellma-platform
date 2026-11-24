// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';
import { holdNetwork, signInAs, type TmNetworkHold, warmUp } from '@tellma/core-testing/e2e';

import { ACME_PRINCIPALS, expect, test } from '../support/acme';

// Instant feedback (spec 0024 §14): a click on an in-app link shows the
// route's skeleton in the frame that follows the click's own, while the
// tenant's launch documents are still on their way. The pull-request tier
// checks the skeleton and its replacement; the frame itself is a timing gate,
// which parallel workers on one machine cannot hold, so it runs in the
// nightly tier's performance gates.

/** What the next frame painted, recorded by {@link watchNextFrame}. */
interface AcmeNextFrameWindow {
  /** Whether the route skeleton showed in the frame after the click's; unset before it ran. */
  __acmeNextFrame?: boolean;
}

/**
 * Signs in, warms the app on the picker alone (the tenant's launch documents
 * are not on the device yet) and holds every server request.
 *
 * @param page - The page.
 * @returns The hold.
 */
async function atThePicker(page: Page): Promise<TmNetworkHold> {
  await signInAs(page, ACME_PRINCIPALS.admin);
  await warmUp(page, '/tenants');
  await page.goto('/tenants');
  await expect(page.getByRole('heading', { level: 1, name: 'Choose a company' })).toBeVisible();
  return holdNetwork(page);
}

/**
 * Records whether the next click's following frame shows the route skeleton:
 * the click's own frame runs the first animation frame callback the click
 * schedules, and the frame after it the second, before that frame's paint.
 *
 * @param page - The page.
 */
async function watchNextFrame(page: Page): Promise<void> {
  await page.evaluate(() => {
    const nextFrame = (): void => {
      const skeleton = document.querySelector('tm-page-skeleton');
      (window as AcmeNextFrameWindow).__acmeNextFrame =
        skeleton !== null && skeleton.checkVisibility();
    };
    document.addEventListener(
      'click',
      () => requestAnimationFrame(() => requestAnimationFrame(nextFrame)),
      { capture: true, once: true },
    );
  });
}

test.describe('route feedback', () => {
  test('a link click shows the route skeleton until the documents arrive', async ({ page }) => {
    const hold = await atThePicker(page);

    await page.getByRole('link', { name: 'Acme', exact: true }).click();

    await expect(page.locator('tm-page-skeleton')).toBeVisible();
    await expect(page.locator('tm-route-host')).toHaveAttribute('aria-busy', 'true');

    // The documents arrive and the page replaces the skeleton.
    await hold.release();
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    await expect(page.locator('tm-route-host')).not.toHaveAttribute('aria-busy', 'true');
  });

  test('the frame after a link click shows the route skeleton @perf', async ({ page }) => {
    const hold = await atThePicker(page);
    await watchNextFrame(page);

    await page.getByRole('link', { name: 'Acme', exact: true }).click();

    await expect
      .poll(() => page.evaluate(() => (window as AcmeNextFrameWindow).__acmeNextFrame))
      .toBe(true);
    await hold.release();
  });
});
