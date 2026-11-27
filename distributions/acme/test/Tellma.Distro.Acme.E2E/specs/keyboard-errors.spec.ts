// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expectNoAxeViolations, signInAs, tmBootFacts } from '@tellma/core-testing/e2e';

import { expect, expireInApp, removeWebLocks, sessionEndedDialog, test } from '../support/acme';

// Keyboard use of the error surfaces (spec 0024 §14): the session-ended
// alert dialog takes the focus, keeps it, ignores Escape and leads on with
// Enter; the boot page is a landmark whose heading takes the focus; and an
// in-app navigation moves the focus to the new page's title.

test.describe('keyboard and errors', () => {
  // On a localhost scope the Angular worker prefetches its whole shell group before it answers
  // any fetch, so a worker registered on the first page would hold the next lazy chunk a test
  // waits for (the home page's after the picker, the session flows' after an expiry) behind that
  // prefetch, which a production scope runs at idle instead. None of these tests is about the
  // worker.
  test.use({ serviceWorkers: 'block' });

  test('the session-ended dialog takes and keeps the focus, ignores Escape and signs in with Enter', async ({
    page,
    text,
    cast,
  }) => {
    await signInAs(page, cast.user);
    await page.goto('/1/customers');
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    await expireInApp(page);

    const dialog = sessionEndedDialog(page);
    const signIn = dialog.getByRole('button', { name: text.signIn });
    await expect(signIn).toBeFocused();
    await expectNoAxeViolations(page);

    // Escape and Tab keep the dialog open and the focus inside it.
    await page.keyboard.press('Escape');
    await expect(dialog).toBeVisible();
    for (let step = 0; step < 3; step++) {
      await page.keyboard.press('Tab');
      expect(await dialog.evaluate((element) => element.contains(document.activeElement))).toBe(
        true,
      );
    }

    await signIn.focus();
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL('/1/customers');
    await expect(dialog).toHaveCount(0);
    await expect.poll(async () => (await tmBootFacts(page)).state).toBe('run');
  });

  test('the boot page is a landmark whose heading takes the focus, and Try again reloads', async ({
    page,
  }) => {
    await removeWebLocks(page.context());
    await page.goto('/1');

    const main = page.getByRole('main');
    await expect(main).toHaveClass(/tm-boot-page/);
    await expect(main.getByRole('heading', { level: 1 })).toBeFocused();

    await page.keyboard.press('Tab');
    const tryAgain = main.getByRole('button');
    await expect(tryAgain).toBeFocused();
    const reloaded = page.waitForEvent('load');
    await page.keyboard.press('Enter');
    await reloaded;
    await expect(page.locator('main.tm-boot-page h1')).toBeFocused();
  });

  test("an in-app navigation moves the focus to the new page's title", async ({
    page,
    text,
    cast,
  }) => {
    await signInAs(page, cast.user);
    await page.goto('/tenants');
    await expect(page.getByRole('heading', { level: 1, name: text.chooseTenant })).toBeVisible();

    await page.getByRole('link', { name: 'Acme', exact: true }).focus();
    await page.keyboard.press('Enter');

    await expect(page.getByRole('heading', { level: 1, name: text.home })).toBeFocused();
  });

  // The error dialogs of a page's failed read need those pages (specs 0030, 0031).
  test.fixme('an error dialog takes the focus, closes with Escape and returns the focus', async () => {});
});
