// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';
import { goOffline, holdNetwork, signInAs, tmIsServerPath, warmUp } from '@tellma/core-testing/e2e';

import {
  ACME_PRINCIPALS,
  expect,
  headingShownAt,
  issuedRequests,
  recordRequests,
  test,
} from '../support/acme';

// The instant warm launch (spec 0024 §14, §15): once the app is warm, every
// route family shows its chrome within a second from the device alone, with
// every server request held and with the device offline; and the app's first
// server request starts only after its first render.

/** One page of each route family, and the heading its chrome shows. */
const FAMILIES = [
  { family: 'the tenant home', path: '/1', heading: 'Home' },
  { family: 'a search page', path: '/1/customers', heading: 'Customers' },
  { family: 'a drill-through list', path: '/1/list/customers', heading: 'Customers' },
  { family: 'the tenant picker', path: '/tenants', heading: 'Choose a company' },
] as const;

/** The chrome's budget on a warm launch. */
const WARM_CHROME_MS = 1_000;

/**
 * Launches a family's page warm and checks its chrome's budget and its
 * requests: every server request the document's app issued came after the
 * app's first render. The device's own files (the shell, the metadata) may
 * come first.
 *
 * @param page - The page, warm, with every server request held.
 * @param family - The route family.
 */
async function launchWarm(page: Page, family: (typeof FAMILIES)[number]): Promise<void> {
  await page.goto(family.path, { waitUntil: 'commit' });

  expect(await headingShownAt(page, family.heading), family.family).toBeLessThan(WARM_CHROME_MS);

  const requests = (await issuedRequests(page)).filter((request) =>
    tmIsServerPath(new URL(request.url).pathname),
  );
  for (const request of requests) {
    expect(request.afterShell, `${family.family}: ${request.url}`).toBe(true);
  }
}

test.describe('warm launch', () => {
  test.beforeEach(async ({ page }) => {
    await recordRequests(page.context());
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/tenants');
    await warmUp(page, '/1');
  });

  test('every tenant route family shows its chrome within a second, its first server request after its first render', async ({
    page,
  }) => {
    const hold = await holdNetwork(page);
    for (const family of FAMILIES.filter((each) => each.path !== '/tenants')) {
      await launchWarm(page, family);
    }
    await hold.release();
  });

  test('the tenant picker shows its chrome within a second, its first server request after its first render', async ({
    page,
  }) => {
    // The launch cache reads the user's tenants again as soon as a tenantless
    // route is routed (spec 0025 §2.3), before the app's first render, where
    // spec 0024 §15 starts the first request after it. The render never waits
    // on that read; only the order is out of line, so this test is expected
    // to fail until the read moves after the first render.
    test.fail();
    const hold = await holdNetwork(page);
    await launchWarm(
      page,
      FAMILIES.find((each) => each.path === '/tenants')!,
    );
    await hold.release();
  });

  test('every route family shows its chrome within a second offline', async ({ page }) => {
    const online = await goOffline(page);
    for (const { family, path, heading } of FAMILIES) {
      await page.goto(path, { waitUntil: 'commit' });

      expect(await headingShownAt(page, heading), family).toBeLessThan(WARM_CHROME_MS);
    }
    await online();
  });
});
