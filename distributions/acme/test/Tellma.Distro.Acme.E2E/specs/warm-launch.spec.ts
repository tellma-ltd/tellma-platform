// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';
import { goOffline, holdNetwork, signInAs, tmIsServerPath, warmUp } from '@tellma/core-testing/e2e';

import {
  ACME_PERF_MULTIPLIER,
  ACME_TIMING_SAMPLES,
  type AcmeText,
  expect,
  headingShownAt,
  issuedRequests,
  median,
  recordRequests,
  test,
} from '../support/acme';

// The instant warm launch (spec 0024 §14): once the app is warm, every route
// family shows its chrome within a second from the device alone, with every
// server request held and with the device offline; and the app's first
// server request starts only after its first render.

/** One page of each route family, and the heading its chrome shows. */
const FAMILIES = [
  { family: 'the tenant home', path: '/1', heading: 'home' },
  { family: 'a search page', path: '/1/customers', heading: 'customers' },
  { family: 'a drill-through list', path: '/1/list/customers', heading: 'customers' },
  { family: 'the tenant picker', path: '/tenants', heading: 'chooseCompany' },
] as const satisfies readonly { family: string; path: string; heading: keyof AcmeText }[];

/** The chrome's budget on a warm launch, on a quiet machine. */
const WARM_CHROME_MS = 1_000;

/**
 * Launches a family's page warm a few times and checks the median time to
 * its chrome against the budget. With `requests` set, it also checks every
 * launch's requests: every server request the document's app issued came
 * after the app's first render. The device's own files (the shell, the
 * metadata) may come first.
 *
 * @param page - The page, warm.
 * @param family - The route family.
 * @param text - The project's texts.
 * @param requests - Whether to check the requests' order.
 */
async function launchWarm(
  page: Page,
  family: (typeof FAMILIES)[number],
  text: AcmeText,
  requests: boolean,
): Promise<void> {
  const shown: number[] = [];
  for (let sample = 0; sample < ACME_TIMING_SAMPLES; sample++) {
    await page.goto(family.path, { waitUntil: 'commit' });
    shown.push(await headingShownAt(page, text[family.heading]));

    if (requests) {
      const issued = (await issuedRequests(page)).filter((request) =>
        tmIsServerPath(new URL(request.url).pathname),
      );
      for (const request of issued) {
        expect(request.afterShell, `${family.family}: ${request.url}`).toBe(true);
      }
    }
  }
  expect(median(shown), `${family.family}: ${shown.map(Math.round).join(', ')} ms`).toBeLessThan(
    WARM_CHROME_MS * ACME_PERF_MULTIPLIER,
  );
}

test.describe('warm launch', () => {
  test.beforeEach(async ({ page, cast }) => {
    await recordRequests(page.context());
    await signInAs(page, cast.user);
    await warmUp(page, '/tenants');
    await warmUp(page, '/1');
  });

  test('every route family shows its chrome within a second with every server request held, its first server request after its first render', async ({
    page,
    text,
  }) => {
    const hold = await holdNetwork(page);
    for (const family of FAMILIES) {
      await launchWarm(page, family, text, true);
    }
    await hold.release();
  });

  test('every route family shows its chrome within a second offline', async ({ page, text }) => {
    const online = await goOffline(page);
    for (const family of FAMILIES) {
      await launchWarm(page, family, text, false);
    }
    await online();
  });
});
