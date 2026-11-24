// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import {
  ACME_PERF_MULTIPLIER,
  ACME_TIMING_SAMPLES,
  expect,
  headingShownAt,
  median,
  test,
} from '../support/acme';

// The nightly gates (spec 0024 §12), all through Chromium's DevTools
// protocol: the app is installable; a signed-in cold launch on Slow 4G
// with the CPU slowed four times paints its largest content within four
// seconds, a warning only; and a warm launch shows its chrome within 300 ms
// with the CPU slowed four times, the median of a few launches. The warm
// budget is a quiet machine's, times the CI multiplier; the cold budget,
// bound by the emulated network, takes none.

/** Slow 4G as Lighthouse's DevTools throttling applies it: 562.5 ms of latency, 1.6 Mbps down and 750 kbps up, less its overhead. */
const SLOW_4G = {
  offline: false,
  latency: 562.5,
  downloadThroughput: (1.6 * 1024 * 1024 * 0.9) / 8,
  uploadThroughput: (750 * 1024 * 0.9) / 8,
};

/** The cold launch's largest-contentful-paint budget, which only warns. */
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
    browser,
    text,
    cast,
  }) => {
    // A signed-in user's cold launch: a fresh context that carries the app's
    // session cookies and nothing the app stored, so the launch makes no
    // sign-in round trip and finds neither the worker nor the launch documents.
    await signInAs(page, cast.user);
    const { cookies } = await page.context().storageState();
    const context = await browser.newContext({
      baseURL: test.info().project.use.baseURL,
      ignoreHTTPSErrors: true,
      locale: test.info().project.use.locale,
      storageState: { cookies, origins: [] },
    });
    try {
      await context.addInitScript(() => {
        new PerformanceObserver((list) => {
          const last = list.getEntries().at(-1);
          if (last) {
            (window as { __acmeLcp?: number }).__acmeLcp = last.startTime;
          }
        }).observe({ type: 'largest-contentful-paint', buffered: true });
      });
      const cold = await context.newPage();
      const cdp = await context.newCDPSession(cold);
      await cdp.send('Network.enable');
      await cdp.send('Network.emulateNetworkConditions', SLOW_4G);
      await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 });

      await cold.goto('/');
      await expect(cold.getByRole('heading', { level: 1, name: text.home })).toBeVisible({
        timeout: 60_000,
      });
      // The largest paint is final once the user acts; a click on the page's title ends it.
      await cold.getByRole('heading', { level: 1 }).click();
      const { lcp, redirects } = await cold.evaluate(() => ({
        lcp: (window as { __acmeLcp?: number }).__acmeLcp ?? null,
        redirects: (performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming)
          .redirectCount,
      }));

      expect(lcp).not.toBeNull();
      const measure = `The cold launch's largest paint took ${Math.round(lcp!)} ms after ${redirects} redirects.`;
      test.info().annotations.push({ type: 'lcp', description: measure });
      // GitHub workflow commands, so the run's summary shows the measure, and a
      // warning over the budget. Network emulation runs no slower on a slow
      // runner, so the budget takes no CI multiplier.
      if (process.env['CI']) {
        console.log(`::notice title=Cold launch LCP::${measure}`);
      }
      if (lcp! > COLD_LCP_MS) {
        const warning = `${measure} The budget is ${COLD_LCP_MS} ms.`;
        test.info().annotations.push({ type: 'warning', description: warning });
        if (process.env['CI']) {
          console.log(`::warning title=Cold launch LCP::${warning}`);
        }
      }
    } finally {
      await context.close();
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
