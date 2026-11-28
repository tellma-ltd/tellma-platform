// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { type Page, chromium } from '@playwright/test';
import { signInAs } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';
import { SHELL_TEXT, activeOption, sessionButton } from '../support/shell';

// Installed mode (spec 0027 §7.1, §7.3, §13): run as an installed app, the
// app bar shows Back and Forward from md, which move through the app's own
// history. The first flow runs the app in Chromium's app window (`--app`),
// whose display mode is standalone; an app window is headed, so the flow
// needs a display and skips on a Linux machine without one. The second runs
// in the suite's own headless browser with the flag iOS sets on a
// home-screen app, the other signal the shell follows.

/**
 * Moves from the tenant home to the customers through the launcher, then
 * back and forward with the app bar's buttons, each enabled only when it
 * can move.
 *
 * @param page - The page, on the tenant home in installed mode.
 * @param lang - The project's language.
 */
async function backAndForward(page: Page, lang: AcmeLanguage): Promise<void> {
  const text = SHELL_TEXT[lang];
  const history = page.locator('tm-app-bar .tm-app-bar__history');
  const back = history.getByRole('button', { name: text.back });
  const forward = history.getByRole('button', { name: text.forward });
  await expect(back).toBeVisible();
  await expect(forward).toBeVisible();

  await page.keyboard.press('ControlOrMeta+K');
  await page.keyboard.type(lang === 'ar' ? 'عمل' : 'cust');
  await expect(activeOption(page)).toHaveAttribute('href', '/1/customers');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(/\/1\/customers$/);
  await expect(back).toBeEnabled();

  await back.click();
  await expect(page).toHaveURL(/\/1$/);
  await expect(forward).toBeEnabled();
  await forward.click();
  await expect(page).toHaveURL(/\/1\/customers$/);
}

test.describe('installed mode', () => {
  test("back and forward in Chromium's app window", async ({
    page,
    lang,
    locale,
    baseURL,
    cast,
  }) => {
    test.skip(
      process.platform === 'linux' && process.env['DISPLAY'] === undefined,
      'an app window needs a display',
    );
    // The session, signed in through the suite's own context and carried into the app window.
    await signInAs(page, cast.user);
    const cookies = await page.context().cookies();
    const profile = mkdtempSync(join(tmpdir(), 'acme-installed-'));
    const app = await chromium.launchPersistentContext(profile, {
      headless: false,
      ignoreHTTPSErrors: true,
      locale,
      viewport: { width: 1280, height: 800 },
      // The app window opens on the signed-out page, which asks the server nothing.
      args: [`--app=${baseURL}/signed-out`, '--ignore-certificate-errors'],
    });
    try {
      await app.addCookies(cookies);
      const window = app.pages()[0] ?? (await app.waitForEvent('page'));
      await window.waitForLoadState();
      await window.goto(`${baseURL}/1`);
      expect(
        await window.evaluate(() => matchMedia('(display-mode: standalone)').matches),
        'the app window runs standalone',
      ).toBe(true);
      await expect(sessionButton(window, lang)).toBeVisible();
      await backAndForward(window, lang);
    } finally {
      await app.close();
      rmSync(profile, { recursive: true, force: true });
    }
  });

  test("back and forward with iOS's home-screen flag", async ({ page, lang, cast }) => {
    await page.context().addInitScript(() => {
      Object.defineProperty(navigator, 'standalone', { value: true, configurable: true });
    });
    await signInAs(page, cast.user);
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    await backAndForward(page, lang);
  });
});
