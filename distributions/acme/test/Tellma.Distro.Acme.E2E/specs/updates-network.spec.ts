// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { holdNetwork, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { ACME_PRINCIPALS, expect, test } from '../support/acme';

// The updates and network flows (spec 0024 §13). The update and offline
// banners are spec 0028's, so the flows that read them are pending; the
// hung request is the foundation's own.

test.describe('updates and network', () => {
  test('a hung request does not block navigation', async ({ page }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/tenants');
    await warmUp(page, '/1');
    await page.goto('/tenants');
    await expect(page.getByRole('heading', { level: 1, name: 'Choose a company' })).toBeVisible();
    const hold = await holdNetwork(page);

    // Every server request from here on hangs; the warm app still moves between pages.
    await page.getByRole('link', { name: 'Acme', exact: true }).click();

    await expect(page).toHaveURL('/1');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    await page.goBack();
    await expect(page.getByRole('heading', { level: 1, name: 'Choose a company' })).toBeVisible();
    await hold.release();
  });

  // --- Pending: a page read to answer with 412 (specs 0030, 0031) ---
  test.fixme('a page read answered 412 with a newer server build offers the update', async () => {});
  test.fixme('a page read answered 412 with an older server build reloads into the server build', async () => {});
  test.fixme('a 412 whose update download is blocked shows the download failure, then Reload anyway', async () => {});
  // --- Pending: the banners (spec 0028) ---
  test.fixme('the update banner shows when the worker finds a new version', async () => {});
  test.fixme('an old tab whose background client sync answers 412 after a deployment shows the update banner', async () => {});
  test.fixme('the offline banner shows while the device is offline', async () => {});
});
