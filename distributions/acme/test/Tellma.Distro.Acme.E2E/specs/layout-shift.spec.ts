// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Route } from '@playwright/test';
import { holdNetwork, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';

// Stable layout (spec 0024 §14): the skeleton a cold entry holds has the
// page's final geometry, so the page replaces it with no layout shift.

test.describe('layout shift', () => {
  test('a cold entry moves from its skeleton to its chrome with zero shift', async ({
    page,
    text,
    cast,
  }) => {
    await signInAs(page, cast.user);
    // The shell and its fonts are on the device; the tenant's launch documents are not.
    await warmUp(page, '/tenants');
    // Every layout shift of the next document, from its first frame on.
    await page.context().addInitScript(() => {
      const shifts: number[] = ((window as { __acmeShifts?: number[] }).__acmeShifts = []);
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries() as (PerformanceEntry & {
          value: number;
          hadRecentInput: boolean;
        })[]) {
          if (!entry.hadRecentInput) {
            shifts.push(entry.value);
          }
        }
      }).observe({ type: 'layout-shift', buffered: true });
    });
    const hold = await holdNetwork(page);

    await page.goto('/1/customers');
    await expect(page.locator('tm-page-skeleton')).toBeVisible();
    await hold.release();
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();

    // A shift is reported in the frame after it happens: two frames settle the last one.
    const shifts = await page.evaluate(
      () =>
        new Promise<number[]>((settle) =>
          requestAnimationFrame(() =>
            requestAnimationFrame(() =>
              settle((window as { __acmeShifts?: number[] }).__acmeShifts ?? []),
            ),
          ),
        ),
    );
    expect(shifts).toEqual([]);
  });

  // The wordmark's file loads in its own time, after the bar's first frame on
  // a device that has never loaded it: its image keeps the box its file's
  // ratio gives it, so nothing after it in the bar moves when the file lands.
  // The image, and the item after it in its bar.
  for (const [url, wordmark, next] of [
    ['/1/customers', '.tm-app-bar__wordmark img', '.tm-app-bar__group'],
    ['/tenants', '.tm-tenantless-shell__wordmark img', '.tm-tenantless-shell__spacer'],
  ] as const) {
    test(`the wordmark keeps its box while its file loads (${url})`, async ({
      page,
      text,
      cast,
    }) => {
      await signInAs(page, cast.user);
      // The brand files, held: a first visit, so no service worker answers them.
      const held: Route[] = [];
      let holding = true;
      await page.route('**/brand/*.svg', (route) =>
        holding ? void held.push(route) : route.fallback(),
      );

      await page.goto(url);
      await expect(
        page.getByRole('heading', {
          level: 1,
          name: url === '/tenants' ? text.chooseTenant : text.customers,
        }),
      ).toBeVisible();
      const image = page.locator(wordmark);
      const after = page.locator(next);
      expect(await image.evaluate((element: HTMLImageElement) => element.complete)).toBe(false);
      const before = [await image.boundingBox(), await after.boundingBox()];
      expect(before[0]?.width).toBeGreaterThan(0);

      holding = false;
      await Promise.all(held.map((route) => route.fallback()));
      await expect
        .poll(() =>
          image.evaluate(
            (element: HTMLImageElement) => element.complete && element.naturalWidth > 0,
          ),
        )
        .toBe(true);
      expect([await image.boundingBox(), await after.boundingBox()]).toEqual(before);
    });
  }

  // An error state inside the content box needs a page whose read fails (specs 0030, 0031).
  test.fixme('an error state replaces the skeleton with zero shift', async () => {});
});
