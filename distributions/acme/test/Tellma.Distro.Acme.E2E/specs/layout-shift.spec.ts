// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

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

  // An error state inside the content box needs a page whose read fails (specs 0030, 0031).
  test.fixme('an error state replaces the skeleton with zero shift', async () => {});
});
