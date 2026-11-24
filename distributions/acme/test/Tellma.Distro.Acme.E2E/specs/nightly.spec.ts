// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs, tmSignInToIdentityAs, warmUp } from '@tellma/core-testing/e2e';

import {
  ACME_PERF_MULTIPLIER,
  ACME_TIMING_SAMPLES,
  expect,
  headingShownAt,
  median,
  test,
} from '../support/acme';

// The nightly gates (spec 0024 §12), all through Chromium's DevTools
// protocol: the app is installable; a cold launch on Slow 4G paints its
// largest content within four seconds, a warning only; and a warm launch
// shows its chrome within 300 ms with the CPU slowed four times, the median
// of a few launches. Each budget is a quiet machine's, times the CI
// multiplier.

/** Slow 4G as Lighthouse's DevTools throttling applies it: 562.5 ms of latency, 1.6 Mbps down and 750 kbps up, less its overhead. */
const SLOW_4G = {
  offline: false,
  latency: 562.5,
  downloadThroughput: (1.6 * 1024 * 1024 * 0.9) / 8,
  uploadThroughput: (750 * 1024 * 0.9) / 8,
};

/** The cold launch's largest-contentful-paint budget on a quiet machine, which only warns. */
const COLD_LCP_MS = 4_000;

/** The warm chrome's budget with the CPU slowed four times, on a quiet machine. */
const WARM_CHROME_MS = 300;

test.describe('nightly gates', () => {
  test('the app is installable @nightly', async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');

    const cdp = await page.context().newCDPSession(page);
    const { installabilityErrors } = (await cdp.send('Page.getInstallabilityErrors')) as {
      installabilityErrors: { errorId: string }[];
    };

    expect(installabilityErrors.map((error) => error.errorId)).toEqual([]);
  });

  test('a cold launch on Slow 4G paints its largest content within four seconds @perf', async ({
    page,
    text,
    cast,
  }) => {
    await tmSignInToIdentityAs(page.context(), cast.user);
    await page.context().addInitScript(() => {
      new PerformanceObserver((list) => {
        const last = list.getEntries().at(-1);
        if (last) {
          (window as { __acmeLcp?: number }).__acmeLcp = last.startTime;
        }
      }).observe({ type: 'largest-contentful-paint', buffered: true });
    });
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Network.enable');
    await cdp.send('Network.emulateNetworkConditions', SLOW_4G);

    await page.goto('/');
    await expect(page.getByRole('heading', { level: 1, name: text.home })).toBeVisible({
      timeout: 60_000,
    });
    // The largest paint is final once the user acts; a click on the page's title ends it.
    await page.getByRole('heading', { level: 1 }).click();
    const lcp = await page.evaluate(() => (window as { __acmeLcp?: number }).__acmeLcp ?? null);

    expect(lcp).not.toBeNull();
    test.info().annotations.push({ type: 'lcp', description: `${Math.round(lcp!)} ms` });
    const budget = COLD_LCP_MS * ACME_PERF_MULTIPLIER;
    if (lcp! > budget) {
      const warning = `The cold launch's largest paint took ${Math.round(lcp!)} ms, over ${budget} ms.`;
      test.info().annotations.push({ type: 'warning', description: warning });
      // A GitHub workflow command, so the warning shows on the run's summary.
      if (process.env['CI']) {
        console.log(`::warning title=Cold launch LCP::${warning}`);
      }
    }
  });

  test('a warm launch shows its chrome within 300 ms with the CPU slowed four times @perf', async ({
    page,
    text,
    cast,
  }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');
    const cdp = await page.context().newCDPSession(page);
    await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });

    const shown: number[] = [];
    for (let sample = 0; sample < ACME_TIMING_SAMPLES; sample++) {
      await page.goto('/1', { waitUntil: 'commit' });
      shown.push(await headingShownAt(page, text.home));
    }

    test.info().annotations.push({
      type: 'warm chrome',
      description: `${shown.map(Math.round).join(', ')} ms`,
    });
    expect(median(shown)).toBeLessThanOrEqual(WARM_CHROME_MS * ACME_PERF_MULTIPLIER);
  });
});
