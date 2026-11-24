// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';
import { holdNetwork, signInAs, type TmNetworkHold, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeCast, type AcmeText, expect, test } from '../support/acme';

// Instant feedback (spec 0024 §4.4, §14): a click on an in-app link shows the
// route's skeleton at once, while the tenant's launch documents are still on
// their way. The pull-request tier checks the skeleton and its replacement;
// the frame itself is a timing gate, which parallel workers on one machine
// cannot hold, so it runs in the nightly tier's performance gates. That gate
// holds the skeleton to the first frame painted after the click; the report
// records the frame it showed in.

/** The frame the route skeleton first showed in, recorded by {@link watchSkeletonFrame}. */
interface AcmeSkeletonFrameWindow {
  /** The frame, counted from 1 for the first frame painted after the click; unset before it showed. */
  __acmeSkeletonFrame?: number;
}

/** The frames after the click the probe watches before it gives up. */
const WATCHED_FRAMES = 10;

/** The latest frame the skeleton may first show in: the first painted after the click. */
const SKELETON_FRAME = 1;

/**
 * Signs in, warms the app on the picker alone (the tenant's launch documents
 * are not on the device yet) and holds every server request.
 *
 * @param page - The page.
 * @param cast - The project's principals.
 * @param text - The project's texts.
 * @returns The hold.
 */
async function atThePicker(page: Page, cast: AcmeCast, text: AcmeText): Promise<TmNetworkHold> {
  await signInAs(page, cast.user);
  await warmUp(page, '/tenants');
  await page.goto('/tenants');
  await expect(page.getByRole('heading', { level: 1, name: text.chooseCompany })).toBeVisible();
  return holdNetwork(page);
}

/**
 * Records the first frame painted after the next click that shows the route
 * skeleton. Each frame's animation frame callback runs before that frame
 * paints; a message it posts runs after the paint, before any later frame,
 * so the DOM it reads is the one that frame painted.
 *
 * @param page - The page.
 */
async function watchSkeletonFrame(page: Page): Promise<void> {
  await page.evaluate((watched) => {
    let frame = 0;
    const next = (): void => {
      requestAnimationFrame(() => {
        frame++;
        const channel = new MessageChannel();
        channel.port1.onmessage = () => {
          const skeleton = document.querySelector('tm-page-skeleton');
          if (skeleton !== null && skeleton.checkVisibility()) {
            (window as AcmeSkeletonFrameWindow).__acmeSkeletonFrame = frame;
          } else if (frame < watched) {
            next();
          } else {
            (window as AcmeSkeletonFrameWindow).__acmeSkeletonFrame = Number.POSITIVE_INFINITY;
          }
        };
        channel.port2.postMessage(null);
      });
    };
    document.addEventListener('click', next, { capture: true, once: true });
  }, WATCHED_FRAMES);
}

test.describe('route feedback', () => {
  test('a link click shows the route skeleton until the documents arrive', async ({
    page,
    text,
    cast,
  }) => {
    const hold = await atThePicker(page, cast, text);

    await page.getByRole('link', { name: 'Acme', exact: true }).click();

    await expect(page.locator('tm-page-skeleton')).toBeVisible();
    await expect(page.locator('tm-route-host')).toHaveAttribute('aria-busy', 'true');

    // The documents arrive and the page replaces the skeleton.
    await hold.release();
    await expect(page.getByRole('heading', { level: 1, name: text.home })).toBeVisible();
    await expect(page.locator('tm-route-host')).not.toHaveAttribute('aria-busy', 'true');
  });

  test('a link click shows the route skeleton in the first frame after it @perf', async ({
    page,
    text,
    cast,
  }) => {
    const hold = await atThePicker(page, cast, text);
    await watchSkeletonFrame(page);

    await page.getByRole('link', { name: 'Acme', exact: true }).click();

    // The frame the skeleton first showed in, recorded in the report.
    const frame = (): Promise<number | undefined> =>
      page.evaluate(() => (window as AcmeSkeletonFrameWindow).__acmeSkeletonFrame);
    await expect.poll(frame).not.toBeUndefined();
    const shown = await frame();
    test.info().annotations.push({ type: 'skeleton frame', description: String(shown) });
    expect(shown).toBeLessThanOrEqual(SKELETON_FRAME);
    await hold.release();
  });
});
