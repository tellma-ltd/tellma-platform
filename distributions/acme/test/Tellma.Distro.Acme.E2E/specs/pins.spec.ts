// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Page, Response } from '@playwright/test';
import { signInAs } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';
import {
  SHELL_TEXT,
  activeOption,
  launcherField,
  openSessionMenu,
  pinChips,
  pinnedBar,
  resetPins,
  sessionButton,
  storeLanguage,
  storePinnedBarVisible,
  storePins,
} from '../support/shell';

// The pins and the account's own settings against the real server (spec
// 0027 §8, §10, §13): a filtered search pinned into a new folder survives a
// reload; a pin to a page the user's role does not grant shows locked and
// unpins; with the bar hidden, the launcher's pinned band pins the page; a
// keyboard reorder in Manage pins is saved; and a language switch writes
// the locale once and reads no sync. These flows write the user's own
// preferences, so they sign in as the project's pin keeper, whom no other
// flow signs in as, run one at a time, and each starts from no pins, the bar
// shown and the project's language.

/** A customers search filtered to the names holding "acme", as its fragment carries it. */
const FILTERED_CUSTOMERS = '/1/customers#d=eyJmaWx0ZXIiOiJjb250YWlucyhOYW1lLCAnYWNtZScpIn0';

/** The folder the filtered search is pinned into. */
const FOLDER = 'Desk';

/** Each language's own name, as the language submenu shows it. */
const NATIVE_NAMES: Readonly<Record<AcmeLanguage, string>> = { en: 'English', ar: 'العربية' };

/**
 * Waits for the next accepted write of the user's preferences.
 *
 * @param page - The page.
 * @returns The response, once it arrived.
 */
function preferencesWritten(page: Page): Promise<Response> {
  return page.waitForResponse(
    (response) => response.url().includes('/users/me/preferences/') && response.ok(),
  );
}

/**
 * The hrefs of the pinned bar's link chips, in the bar's order.
 *
 * @param page - The page.
 * @returns The hrefs.
 */
function chipHrefs(page: Page): Promise<string[]> {
  return pinnedBar(page)
    .locator('a.tm-pinned-bar__chip')
    .evaluateAll((chips) => chips.map((chip) => chip.getAttribute('href')!));
}

test.describe('pins and the account', () => {
  test.describe.configure({ mode: 'serial' });

  test.beforeEach(async ({ page, cast, lang }) => {
    await signInAs(page, cast.pins);
    await resetPins(page);
    await storeLanguage(page, lang);
  });

  test('a filtered search pinned into a new folder is in that folder after a reload', async ({
    page,
    lang,
    text,
  }) => {
    const shell = SHELL_TEXT[lang];
    await page.goto(FILTERED_CUSTOMERS);
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    await expect(pinnedBar(page).locator('.tm-pinned-bar__empty')).toBeVisible();

    // --- The pin control pins the page and opens the popover on it ---
    await pinnedBar(page).getByRole('button', { name: shell.pinThisPage }).click();
    const popover = page.locator('.tm-pin-overlays__popover');
    await expect(popover.getByRole('heading', { name: shell.pinned })).toBeVisible();

    // --- A new folder, chosen with the keyboard, named, then Done ---
    await popover.getByRole('combobox', { name: shell.folder }).focus();
    await page.keyboard.press('ArrowDown');
    await expect(page.getByRole('option', { name: shell.newFolder })).toBeVisible();
    await page.keyboard.press('End');
    await page.keyboard.press('Enter');
    await popover.getByRole('textbox', { name: shell.folderName }).fill(FOLDER);
    const written = preferencesWritten(page);
    await popover.getByRole('button', { name: shell.done }).click();
    await written;
    const folder = pinnedBar(page).getByRole('button', { name: FOLDER });
    await expect(folder).toHaveAttribute('aria-haspopup', 'menu');

    // --- After a reload the folder holds the pin, which opens the filtered search ---
    await page.reload();
    await folder.click();
    const menu = page.getByRole('menu', { name: FOLDER });
    await menu.getByRole('menuitem', { name: new RegExp(text.customers as string) }).click();
    await expect(page).toHaveURL(FILTERED_CUSTOMERS);
    await expect(pinnedBar(page).getByRole('button', { name: shell.editPin })).toBeVisible();
  });

  test('a click on a folder in the pin popover keeps the popover open', async ({
    page,
    lang,
    text,
  }) => {
    // A known gap: a click on the Folder select's option closes the pin popover with the select's
    // list, as an outside click would, so the Folder name field never shows; the keyboard works.
    test.fail();
    const shell = SHELL_TEXT[lang];
    await page.goto('/1/customers');
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    await pinnedBar(page).getByRole('button', { name: shell.pinThisPage }).click();
    const popover = page.locator('.tm-pin-overlays__popover');
    await popover.getByRole('combobox', { name: shell.folder }).click();
    await page.getByRole('option', { name: shell.newFolder }).click();
    await expect(popover.getByRole('textbox', { name: shell.folderName })).toBeVisible({
      timeout: 2_000,
    });
  });

  test("a pin to a page the user's role does not grant is locked, says so and unpins", async ({
    page,
    lang,
  }) => {
    const shell = SHELL_TEXT[lang];
    // A pin the user made while a role granted the settings, which the sales role does not.
    await storePins(page, [{ id: 'settings', url: 'settings', title: 'Settings' }]);
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();

    const revoked = pinChips(page).first();
    await expect(revoked).toHaveClass(/tm-pinned-bar__chip--revoked/);
    await revoked.click();
    const menu = page.locator('.tm-pin-overlays__menu');
    await expect(menu).toContainText(shell.noAccess);
    const written = preferencesWritten(page);
    await menu.getByRole('menuitem', { name: shell.unpin }).click();
    await written;
    await expect(pinChips(page)).toHaveCount(0);
    await expect(pinnedBar(page).locator('.tm-pinned-bar__empty')).toBeVisible();
  });

  test("with the pinned bar hidden, the launcher's band pins the page and lists the pin", async ({
    page,
    lang,
    text,
  }) => {
    const shell = SHELL_TEXT[lang];
    await storePinnedBarVisible(page, false);
    await page.goto('/1/customers');
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    await expect(pinnedBar(page)).toHaveCount(0);

    // --- The band holds the pin command, which pins the page ---
    await page.keyboard.press('ControlOrMeta+K');
    await expect(launcherField(page)).toBeFocused();
    const band = page.getByRole('listbox').getByRole('group', { name: shell.launcherPinned });
    const written = preferencesWritten(page);
    await band.getByRole('option', { name: shell.pinThisPage }).click();
    await written;
    await expect(pinnedBar(page)).toHaveCount(0);

    // --- The next open lists the page in the band, and offers to edit its pin ---
    await page.keyboard.press('ControlOrMeta+K');
    await expect(band.locator('[role="option"][href="/1/customers"]')).toHaveCount(1);
    await expect(band.getByRole('option', { name: shell.editPin })).toHaveCount(1);
    await page.keyboard.press('Escape');
    await expect(activeOption(page)).toHaveCount(0);
  });

  test('a keyboard reorder in Manage pins is saved', async ({ page, lang }) => {
    const shell = SHELL_TEXT[lang];
    await storePins(page, [
      { id: 'customer', url: 'customers', title: 'Customers' },
      { id: 'centersx', url: 'centers', title: 'Centers' },
    ]);
    await page.goto('/1');
    await expect(pinChips(page)).toHaveCount(2);
    expect(await chipHrefs(page)).toEqual(['/1/customers', '/1/centers']);

    // --- The handle takes the row with Space, the arrow moves it, and Space drops it ---
    await pinnedBar(page).getByRole('button', { name: shell.managePins }).click();
    const dialog = page.getByRole('dialog', { name: shell.managePins });
    const handle = dialog.locator('.tm-reorder-list__handle').first();
    await handle.focus();
    await page.keyboard.press('Space');
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Space');
    const written = preferencesWritten(page);
    await dialog.getByRole('button', { name: shell.save }).click();
    await written;
    await expect(dialog).toHaveCount(0);
    expect(await chipHrefs(page)).toEqual(['/1/centers', '/1/customers']);

    // --- The order is the server's too: the reload's sync answers it ---
    await page.reload();
    await expect(pinChips(page)).toHaveCount(2);
    await expect.poll(() => chipHrefs(page)).toEqual(['/1/centers', '/1/customers']);
  });

  test('a language switch writes the locale once and reads no sync', async ({ page, lang }) => {
    const other: AcmeLanguage = lang === 'en' ? 'ar' : 'en';
    // The launch's own sync, and whatever else the launch reads, end before the switch.
    const launched = page.waitForResponse((response) =>
      new URL(response.url()).pathname.endsWith('/client/sync'),
    );
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    await launched;
    await page.waitForLoadState('networkidle');

    // Every request from here on: the switch's own, and anything it sets off.
    const paths: string[] = [];
    // The service worker's own fetch of a request the page sent is the same request
    // again, so only the page's requests count.
    page.context().on('request', (request) => {
      if (request.serviceWorker() === null) {
        paths.push(new URL(request.url()).pathname);
      }
    });

    const menu = await openSessionMenu(page, lang);
    await menu.getByRole('menuitem', { name: new RegExp(`^${SHELL_TEXT[lang].language}`) }).click();
    const written = page.waitForResponse(
      (response) => response.url().endsWith('/users/me/locale') && response.ok(),
    );
    await page.getByRole('menuitemradio', { name: NATIVE_NAMES[other] }).click();
    await written;

    // The page reads the other language at once, from the device.
    await expect(page.locator('html')).toHaveAttribute('lang', other);
    await expect(sessionButton(page, other)).toBeVisible();
    expect(paths.filter((path) => path.endsWith('/users/me/locale'))).toHaveLength(1);
    expect(paths.filter((path) => path.includes('client/sync'))).toEqual([]);
  });
});
