// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BrowserContext, chromium } from '@playwright/test';
import { tmE2eHost, tmSignInToIdentityAs } from '@tellma/core-testing/e2e';

import { ACME_ARABIC_READERS } from './acme';

// The run's one-time setup, after the e2e host is up: the Arabic project's
// principals read Arabic. The development seed creates every account in
// English, and an account's language is what a signed-in launch shows, so the
// Arabic project's flows would otherwise render in English. Each account's
// own profile page sets it, as its user would; setting it again is harmless.

/** The identity engine's profile page, where an account's language is set. */
const PROFILE_PAGE = '/id/Identity/Manage';

/**
 * Sets the Arabic project's principals' account language to Arabic.
 *
 * @returns A promise that settles once every one reads Arabic.
 */
export default async function globalSetup(): Promise<void> {
  const host = tmE2eHost();
  const browser = await chromium.launch();
  try {
    for (const principal of ACME_ARABIC_READERS) {
      const context = await browser.newContext({ baseURL: host.origin, ignoreHTTPSErrors: true });
      try {
        await readArabic(context, principal);
      } finally {
        await context.close();
      }
    }
  } finally {
    await browser.close();
  }
}

/**
 * Sets one account's language to Arabic through its profile page, unless it
 * reads Arabic already, and checks the saved value.
 *
 * @param context - A fresh browser context.
 * @param principal - The account's email address.
 * @returns A promise that settles once the account reads Arabic.
 */
async function readArabic(context: BrowserContext, principal: string): Promise<void> {
  await tmSignInToIdentityAs(context, principal);
  const page = await context.newPage();
  await page.goto(PROFILE_PAGE);

  // --- The language: the first offered Arabic culture, saved once ---
  const locale = page.locator('#locale');
  const arabic = await locale
    .locator('option')
    .evaluateAll((options) =>
      options
        .map((option) => (option as HTMLOptionElement).value)
        .find((value) => /^ar\b/.test(value)),
    );
  if (arabic === undefined) {
    throw new Error(`The identity engine's profile page at ${PROFILE_PAGE} offers no Arabic.`);
  }
  if ((await locale.inputValue()) !== arabic) {
    await locale.selectOption(arabic);
    await Promise.all([
      page.waitForResponse(
        (response) => response.request().method() === 'POST' && response.status() < 400,
      ),
      page.locator('form:has(#locale) button[type="submit"]').click(),
    ]);
  }
  await page.goto(PROFILE_PAGE);
  if ((await locale.inputValue()) !== arabic) {
    throw new Error(`The account ${principal} did not save Arabic as its language.`);
  }
}
