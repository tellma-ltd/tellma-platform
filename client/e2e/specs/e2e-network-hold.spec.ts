// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';
import { holdNetwork } from '@tellma/core-testing/e2e';

/**
 * Self-checks of the network hold the apps' browser suites rely on: once it
 * lets go, every request the page made reaches the server and is answered,
 * the ones the browser intercepted while the hold let go included. A request
 * lost there leaves a page waiting for an answer that never comes.
 */

/**
 * How many holds a test lets go while requests keep coming. A hold that loses
 * requests loses one in about a quarter of its releases, so twenty rounds
 * pass by chance about once in five hundred runs.
 */
const ROUNDS = 20;

/** How many requests each round sees before and after the hold lets go. */
const BURST = 20;

/** The page's counters, which the test reads. */
interface TmRequestCounts {
  /** The requests the page has made. */
  readonly sent: number;
  /** The requests answered, with any status. */
  readonly answered: number;
}

/**
 * Starts a stream of requests to a server path, one every few milliseconds,
 * each counted when sent and again when answered, until the page is told to
 * stop.
 */
async function streamRequests(page: Page): Promise<void> {
  await page.evaluate(() => {
    const counts = { sent: 0, answered: 0, stop: false };
    (window as { tmCounts?: typeof counts }).tmCounts = counts;
    void (async () => {
      while (!counts.stop) {
        counts.sent++;
        // Any answer counts, a 404 included: only a request never answered is lost.
        void fetch(`/api/e2e-hold/${counts.sent}`).finally(() => counts.answered++);
        await new Promise((resolve) => setTimeout(resolve, 2));
      }
    })();
  });
}

/** Reads the page's counters. */
function counts(page: Page): Promise<TmRequestCounts> {
  return page.evaluate(() => {
    const { sent, answered } = (window as { tmCounts?: TmRequestCounts }).tmCounts!;
    return { sent, answered };
  });
}

test.describe('network hold', () => {
  test('a released hold answers every request, those made while it lets go included', async ({
    browser,
  }, testInfo) => {
    // Twenty contexts take about ten seconds here; a loaded runner gets six times that.
    test.setTimeout(60_000);
    for (let round = 0; round < ROUNDS; round++) {
      // --- A fresh context per round, so each hold is the only route in it ---
      const context = await browser.newContext({ baseURL: testInfo.project.use.baseURL });
      const page = await context.newPage();
      // Any document of the origin serves; a static file loads in a moment.
      await page.goto('/favicon.ico');
      const hold = await holdNetwork(page);
      await streamRequests(page);
      await expect.poll(async () => (await counts(page)).sent).toBeGreaterThan(BURST);

      // --- Let go while requests keep coming, then stop them ---
      await hold.release();
      const atRelease = (await counts(page)).sent;
      await expect.poll(async () => (await counts(page)).sent).toBeGreaterThan(atRelease + BURST);
      await page.evaluate(() => ((window as { tmCounts?: { stop: boolean } }).tmCounts!.stop = true));

      // --- Every request sent is answered ---
      const { sent } = await counts(page);
      await expect
        .poll(async () => (await counts(page)).answered, { message: `round ${round + 1}` })
        .toBe(sent);
      await context.close();
    }
  });
});
