// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Page, Route } from '@playwright/test';
import { goOffline, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';
import { describeShifts, recordShifts, settledShifts } from '../support/layout';
import {
  SHELL_TEXT,
  type StoredPin,
  boxes,
  closeHeld,
  launcherField,
  pinChips,
  pinnedBar,
  servePins,
  sessionButton,
} from '../support/shell';

// The shell's stable layout (spec 0027 §14): a warm launch shifts nothing;
// the app bar's boxes are the same with the bell's count at 9 and at 10,
// and hold while a banner opens; the pinned bar's boxes hold while a pin goes into
// More and while the pointer moves along the bar; and the launcher's rows
// hold while a provider's bucket arrives below them. The pins are served in
// the sync's answer and their writes held, so the user's stored pins never
// change.

/** The app bar's parts whose boxes the flows compare. */
const APP_BAR_PARTS =
  'tm-app-bar .tm-app-bar__wordmark, tm-app-bar .tm-app-bar__group, tm-app-bar [data-tm-slot], tm-app-bar .tm-app-bar__marker, tm-app-bar .tm-app-bar__session';

/** The pinned bar's parts whose boxes the flows compare. */
const PINNED_BAR_PARTS =
  'nav.tm-pinned-bar .tm-pinned-bar__list > .tm-pinned-bar__item, nav.tm-pinned-bar .tm-pinned-bar__control';

/** Enough pins with long labels that the bar at the flows' width puts some in More. */
const MANY_PINS: readonly StoredPin[] = Array.from({ length: 12 }, (_, index) => ({
  id: `pinmany${'abcdefghijkl'[index]}`,
  url: `customers#d=${Buffer.from(JSON.stringify({ filter: `Id = ${index + 1}` })).toString('base64url')}`,
  title: `Customers of the northern region, desk ${index + 1}`,
}));

/** A launcher query that finds Customers in each language. */
const CUSTOMERS_QUERY: Readonly<Record<AcmeLanguage, string>> = { en: 'cust', ar: 'عمل' };

/** The bell's counter at 9 and at 10, in either digit set the tenant's region writes. */
const NINE = /^(?:9|٩)$/;
const TEN = /^(?:10|١٠)$/;

/**
 * A summary of the inbox, as the inbox's read answers it: a count of unseen
 * notifications, none of them listed, and no running job.
 *
 * @param unseen - The unseen notifications' count.
 * @returns The summary.
 */
function inboxSummary(unseen: number): unknown {
  return {
    unseen,
    unread: unseen,
    latest: [],
    running: [],
    related: {},
    version: String(unseen),
  };
}

/**
 * The pinned bar's items' inline places and sizes, without their block
 * places, which a banner above the bar moves.
 *
 * @param page - The page.
 * @returns Each item's inline start, width and height, rounded to the pixel.
 */
function pinsInline(page: Page): Promise<{ x: number; width: number; height: number }[]> {
  return pinnedBar(page)
    .locator('.tm-pinned-bar__list > .tm-pinned-bar__item')
    .evaluateAll((items) =>
      items.map((item) => {
        const box = item.getBoundingClientRect();
        return {
          x: Math.round(box.x),
          width: Math.round(box.width),
          height: Math.round(box.height),
        };
      }),
    );
}

test.describe('the stable shell', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
  });

  // The page closes while its writes are still held, so the writes it flushes
  // as it goes never reach the user's stored preferences.
  test.afterEach(async ({ page }) => {
    await closeHeld(page);
  });

  test('a warm launch shifts nothing', async ({ page, lang }) => {
    await servePins(page, MANY_PINS.slice(0, 3));
    await recordShifts(page);
    await warmUp(page, '/1');

    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    await expect(pinChips(page)).toHaveCount(3);
    const shifts = await settledShifts(page);
    expect(shifts, describeShifts(shifts)).toEqual([]);
  });

  test("the app bar's boxes are the same with the bell's count at 9 and at 10", async ({
    page,
    lang,
  }) => {
    // The inbox reads 9 on the first launch and 10 on the next.
    let unseen = 9;
    await page.context().route(
      (url) => url.pathname === '/1/api/web/inbox/summary',
      (route: Route) => route.fulfill({ json: inboxSummary(unseen) }),
    );
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    const counter = page.locator('tm-app-bar [data-tm-slot="bell"] tm-counter');
    await expect(counter).toHaveText(NINE);
    const before = await boxes(page, APP_BAR_PARTS);

    // The next launch reads the inbox again once its tab leads, which may take a while.
    unseen = 10;
    const read = page.waitForResponse(
      (response) => new URL(response.url()).pathname === '/1/api/web/inbox/summary',
    );
    await page.reload();
    await read;
    await expect(counter).toHaveText(TEN);
    expect(await boxes(page, APP_BAR_PARTS)).toEqual(before);
  });

  test("the app bar's and the pins' boxes hold while a banner opens", async ({ page }) => {
    await servePins(page, MANY_PINS.slice(0, 3));
    await warmUp(page, '/1');
    await page.goto('/1');
    await expect(pinChips(page)).toHaveCount(3);
    const appBar = await boxes(page, APP_BAR_PARTS);
    const pins = await pinsInline(page);

    // Offline, a navigation's read fails, and the offline banner opens 5 s later.
    const online = await goOffline(page);
    await page.keyboard.press('ControlOrMeta+K');
    await page.keyboard.type('cust');
    await page.keyboard.press('Enter');
    await expect(page.locator('tm-banner-host tm-banner-bar')).toBeVisible({ timeout: 15_000 });

    expect(await boxes(page, APP_BAR_PARTS)).toEqual(appBar);
    // The banner's row comes between the two bars: the pins move down, and nothing else changes.
    expect(await pinsInline(page)).toEqual(pins);
    await online();
  });

  test("the pinned bar's boxes hold while a pin goes into More and the pointer moves along the bar", async ({
    page,
    text,
  }) => {
    await servePins(page, MANY_PINS);
    await warmUp(page, '/1');
    await page.goto('/1/customers');
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    const more = pinnedBar(page).locator('[data-tm-pin="more"]');
    await expect(more).toBeVisible();
    const before = await boxes(page, PINNED_BAR_PARTS);

    // --- A hover along the bar, chip by chip, then away ---
    for (const chip of await pinChips(page).all()) {
      await chip.hover();
      expect(await boxes(page, PINNED_BAR_PARTS)).toEqual(before);
    }
    await page.mouse.move(0, 400);

    // --- A pin of the routed page, which goes into More: the shown chips stay as they were ---
    await page.keyboard.press('Alt+Shift+P');
    await expect(pinnedBar(page).getByRole('button', { name: /./ }).first()).toBeVisible();
    await page.keyboard.press('Escape');
    expect(await boxes(page, PINNED_BAR_PARTS)).toEqual(before);
    await more.click();
    await expect(
      page.getByRole('menu').getByRole('menuitem', { name: text.customers, exact: true }),
    ).toHaveCount(1);
  });

  test("the launcher's rows hold while a provider's bucket arrives below them", async ({
    page,
    lang,
  }) => {
    await warmUp(page, '/1');
    // The invoices' answers wait until the flow lets them go.
    const held: Route[] = [];
    await page.context().route(
      (url) => url.pathname === '/1/api/web/sales-invoices/query',
      (route) => {
        held.push(route);
      },
    );
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    await page.keyboard.press('ControlOrMeta+K');
    await expect(launcherField(page)).toBeFocused();
    await page.keyboard.type(CUSTOMERS_QUERY[lang]);
    const pages = page.getByRole('listbox').locator('.tm-launcher__group--pages');
    await expect(pages.getByRole('option').first()).toBeVisible();
    await expect.poll(() => held.length).toBeGreaterThan(0);
    const before = await boxes(
      page,
      '.tm-launcher__group--pages, .tm-launcher__group--pages [role="option"]',
    );
    const active = await page
      .getByRole('listbox')
      .locator('[aria-selected="true"]')
      .getAttribute('id');

    // The last query's answer arrives: its bucket comes below, and the rows and the active one stay.
    // The answer is the server's to a query every invoice matches, so the bucket has rows; the
    // reads the page superseded were aborted already, and passing them on does nothing.
    for (const route of held.splice(0)) {
      const body = route.request().postDataJSON() as Record<string, unknown>;
      await route
        .continue({ postData: JSON.stringify({ ...body, filters: ['SerialNumber > 0'] }) })
        .catch(() => undefined);
    }
    const bucket = page
      .getByRole('listbox')
      .getByRole('group', { name: SHELL_TEXT[lang].salesInvoices, exact: true });
    await expect(bucket.getByRole('option').first()).toBeVisible();
    expect(
      await boxes(page, '.tm-launcher__group--pages, .tm-launcher__group--pages [role="option"]'),
    ).toEqual(before);
    await expect(page.getByRole('listbox').locator('[aria-selected="true"]')).toHaveAttribute(
      'id',
      active!,
    );
  });
});
