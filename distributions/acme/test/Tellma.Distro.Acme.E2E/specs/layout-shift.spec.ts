// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page, type Route } from '@playwright/test';
import { holdNetwork, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, holdSelectChunks, test } from '../support/acme';
import { describeShifts, recordShifts, settledShifts } from '../support/layout';

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
    await recordShifts(page);
    const hold = await holdNetwork(page);

    await page.goto('/1/customers');
    await expect(page.locator('tm-page-skeleton')).toBeVisible();
    await hold.release();
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();

    const shifts = await settledShifts(page);
    // The scores alone decide; the message names what moved, and from where to where.
    expect(
      shifts.map((shift) => shift.value),
      describeShifts(shifts),
    ).toEqual([]);
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

  test.describe('with the worker blocked', () => {
    // The worker claims the page once it activates and answers the lazy files
    // from its own cache, past the page's routes: the hold needs it blocked.
    test.use({ serviceWorkers: 'block' });
    // The hold's route may still be fetching a file when the flow ends; its
    // callback must not outlive the test, or the error it then throws fails
    // the worker and the next test with it.
    test.afterEach(async ({ page }) => {
      await page.unrouteAll({ behavior: 'ignoreErrors' });
    });

    // The pager's page-size select loads on demand; until it lands, a stand-in
    // of its geometry holds its place, so the swap moves nothing.
    test("the pager's size select replaces its stand-in with zero shift", async ({
      page,
      text,
      cast,
    }) => {
      await signInAs(page, cast.user);
      await recordShifts(page);
      const select = await holdSelectChunks(page);

      await page.goto('/1/customers');
      await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
      const standIn = page.locator('tm-pager .tm-pager__proxy');
      const next = page.locator('tm-pager .tm-pager__next');
      // The page settles first: its rows, the pager's status and the fonts.
      await expect(
        page.getByRole('grid').locator('[role="rowgroup"] [role="row"]').first(),
      ).toBeVisible();
      await expect(page.locator('tm-pager .tm-pager__fraction')).toBeVisible();
      await page.evaluate(() => document.fonts.ready);
      await expect(standIn).toBeVisible();
      await expect(page.locator('tm-pager tm-select')).toHaveCount(0);
      const before = [await standIn.boundingBox(), await next.boundingBox()];
      const earlier = (await settledShifts(page)).length;

      await select.release();
      await expect(standIn).toHaveCount(0);
      expect([
        await page.locator('tm-pager tm-select').boundingBox(),
        await next.boundingBox(),
      ]).toEqual(before);
      // Only the swap is judged: the shifts reported after the release.
      const shifts = (await settledShifts(page)).slice(earlier);
      expect(
        shifts.map((shift) => shift.value),
        describeShifts(shifts),
      ).toEqual([]);
    });
  });

  // An error state inside the content box needs a page whose read fails (specs 0030, 0031).
  test.fixme('an error state replaces the skeleton with zero shift', async () => {});
});
