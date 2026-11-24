// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  openTabs,
  signInAs,
  tmBootFacts,
  tmBootRecord,
  tmExpireSession,
  tmSignInToIdentityAs,
  tmStoredDocs,
  warmUp,
} from '@tellma/core-testing/e2e';

import {
  ACME_PRINCIPALS,
  comeIntoViewUntil,
  expect,
  expireInApp,
  sessionEndedDialog,
  stopAtSignIn,
  test,
} from '../support/acme';

// The session flows (spec 0024 §13): what an expiry deletes, a changed user,
// the other tabs on a new session, and another user signing in from the
// session-ended dialog. The flows that need a later spec's surfaces are
// listed at the end as pending.

test.describe('session', () => {
  test("an expired session deletes the user's stored data before it leaves to sign in", async ({
    page,
  }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');
    const { sub } = await tmBootFacts(page);
    expect(await tmStoredDocs(page, `u:${sub}/`)).not.toEqual([]);
    await tmExpireSession(page.context());
    await stopAtSignIn(page.context());

    await page.goto('/1');

    await expect(page).toHaveTitle('Sign-in stopped');
    expect(new URL(page.url()).pathname).toBe('/bff/login');
    expect(await tmStoredDocs(page, `u:${sub}/`)).toEqual([]);
    expect(await page.evaluate(() => caches.has('tm-images-v1'))).toBe(false);
    const record = await tmBootRecord(page);
    expect(record?.sub ?? null).toBeNull();
    expect(record?.lastTenantId ?? null).toBeNull();
  });

  test('a changed user reaches a tab that never came into view', async ({ page, context }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await page.goto('/1');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    const [hidden] = await openTabs(context, 1, '/1');

    // Another user signs in on the device; only the first tab comes into view.
    await signInAs(page, ACME_PRINCIPALS.sales);
    await expect(async () => {
      await page.evaluate(() => document.dispatchEvent(new Event('visibilitychange')));
      expect((await tmBootFacts(page)).email).toBe(ACME_PRINCIPALS.sales);
    }).toPass({ timeout: 30_000 });

    // The other tab learns of the new user from the first, and restarts as that user.
    await expect
      .poll(async () => (await tmBootFacts(hidden!)).email, { timeout: 30_000 })
      .toBe(ACME_PRINCIPALS.sales);
  });

  test("the user's other tabs reload when the session starts again", async ({ page, context }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');
    const [other] = await openTabs(context, 1, '/1/customers');
    await expireInApp(page);
    // The other tab hears of the expiry and shows the dialog too.
    await expect(sessionEndedDialog(other!)).toBeVisible();

    const reloaded = other!.waitForEvent('load');
    await sessionEndedDialog(page).getByRole('button', { name: 'Sign in' }).click();
    await expect(page).toHaveURL('/1');
    await reloaded;

    await expect(other!.getByRole('heading', { level: 1, name: 'Customers' })).toBeVisible();
    await expect(sessionEndedDialog(other!)).toHaveCount(0);
    expect((await tmBootFacts(other!)).state).toBe('run');
  });

  test("another user signing in from the session-ended dialog finds none of the previous user's drafts", async ({
    page,
  }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');
    const { sub } = await tmBootFacts(page);
    await page.evaluate((owner) => {
      sessionStorage.setItem(
        `tm.draft:${owner}:t1:customers:new`,
        JSON.stringify({ v: 1, name: 'Unsaved' }),
      );
    }, sub);
    await expireInApp(page);
    // The administrator's identity sign-in is gone too: the sales representative signs in here.
    await tmSignInToIdentityAs(page.context(), ACME_PRINCIPALS.sales);

    await sessionEndedDialog(page).getByRole('button', { name: 'Sign in' }).click();

    await expect
      .poll(async () => (await tmBootFacts(page)).email, { timeout: 30_000 })
      .toBe(ACME_PRINCIPALS.sales);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    const keys = await page.evaluate(() => Object.keys(sessionStorage));
    expect(keys.filter((key) => key.startsWith(`tm.draft:${sub}:`))).toEqual([]);
  });

  test('a tab whose session ended re-checks when it comes into view', async ({ page }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await page.goto('/1');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    await tmExpireSession(page.context());

    await comeIntoViewUntil(page, sessionEndedDialog(page));

    await expect(sessionEndedDialog(page)).toContainText('Sign in to continue. Your work is kept.');
  });

  // --- Pending: signed out elsewhere needs the hub's sign-out message (spec 0025) ---
  test.fixme('signed out elsewhere: the sign-out reaches every tab', async () => {});
  // --- Pending: a step-up save (spec 0031) ---
  test.fixme('a step-up save asks for the stronger sign-in and saves on return', async () => {});
  // --- Pending: sign-out across three tabs needs the account menu (spec 0027) and saves (spec 0031) ---
  test.fixme('sign-out across three tabs: a running save that succeeds signs out with no question', async () => {});
  test.fixme('sign-out across three tabs: a save that fails shows the confirm', async () => {});
  test.fixme('sign-out across three tabs: a dirty idle tab shows the confirm at once', async () => {});
  test.fixme("sign-out across three tabs: every tab shows the overlay in the click's frame", async () => {});
  test.fixme("sign-out across three tabs: the telemetry batch leaves before the BFF's logout", async () => {});
});
