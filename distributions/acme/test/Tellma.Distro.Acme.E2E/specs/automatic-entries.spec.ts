// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  signInAs,
  tmBootFacts,
  tmExpireSession,
  tmSignInToIdentityAs,
  warmUp,
} from '@tellma/core-testing/e2e';

import { ACME_PRINCIPALS, expect, expireInApp, recordSeen, test, wasSeen } from '../support/acme';

// The automatic entries (spec 0024 §13): the tenant the platform picks for a
// launch at the root after an expiry, and what an entry the user did not
// choose shows when the tenant is not the user's.

/** The tenantless skeleton: the picker's geometry while the memberships are read. */
const TENANTLESS_SKELETON = 'main.tm-launch-redirect';

test.describe('automatic entries', () => {
  test('an expired session at the root returns to the one live tenant, with no tenantless skeleton', async ({
    page,
  }) => {
    await recordSeen(page.context(), TENANTLESS_SKELETON);
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');
    await tmExpireSession(page.context());

    await page.goto('/');

    await expect(page).toHaveURL('/1');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    expect(await wasSeen(page, TENANTLESS_SKELETON)).toBe(false);
    expect((await tmBootFacts(page)).auto).toBe(1);
  });

  test('an expired session at the root returns to the last of two tenants', async ({ page }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');
    await page.goto('/2');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    await expect
      .poll(() =>
        page.evaluate(
          () =>
            (
              JSON.parse(localStorage.getItem('tm.boot') ?? '{}') as {
                lastTenantId?: number;
              }
            ).lastTenantId,
        ),
      )
      .toBe(2);
    await tmExpireSession(page.context());

    await page.goto('/');

    await expect(page).toHaveURL('/2');
    expect((await tmBootFacts(page)).auto).toBe(2);
  });

  test('a tab closed at the session-ended dialog leaves a launch at the root to the last tenant', async ({
    page,
    context,
  }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');
    await page.goto('/2');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    await expireInApp(page);
    await page.close();

    const next = await context.newPage();
    await next.goto('/');

    await expect(next).toHaveURL('/2');
    await expect(next.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
  });

  test('a shared device whose second user is not in the last tenant shows the picker without a notice', async ({
    page,
  }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');
    // The administrator's session ends, and the sandbox tester signs in on the same device.
    await tmSignInToIdentityAs(page.context(), ACME_PRINCIPALS.sandbox);

    await page.goto('/');

    await expect(page).toHaveURL('/tenants');
    await expect(page.getByRole('link', { name: 'Acme Sandbox' })).toBeVisible();
    expect((await tmBootFacts(page)).email).toBe(ACME_PRINCIPALS.sandbox);
  });

  test("a typed tenant that is not the user's shows the picker with the notice", async ({
    page,
  }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);

    await page.goto('/99');

    await expect(page).toHaveURL('/tenants?notice=tenant-not-found&from=99');
    await expect(page.getByRole('heading', { level: 1, name: 'Choose a company' })).toBeVisible();
  });
});
