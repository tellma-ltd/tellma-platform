// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  expectNoAxeViolations,
  signInAs,
  tmBootFacts,
  tmBootRecord,
  tmSignInToIdentityAs,
  warmUp,
} from '@tellma/core-testing/e2e';

import {
  ACME_PRINCIPALS,
  expect,
  expireInApp,
  firstPaint,
  recordFirstPaint,
  removeWebLocks,
  sessionEndedDialog,
  test,
} from '../support/acme';

// The launch flows (spec 0024 §13): where a cold launch lands, deep links and
// their fragments across a sign-in, the warm launch, the theme before the
// first paint, the boot page, and the signed-out page with no request.

test.describe('launch', () => {
  // --- Cold launches ---
  test('a cold launch of a user with one live tenant lands in that tenant', async ({ page }) => {
    await tmSignInToIdentityAs(page.context(), ACME_PRINCIPALS.admin);

    await page.goto('/');

    await expect(page).toHaveURL('/1');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    expect((await tmBootFacts(page)).cold).toBe(true);
  });

  test('a cold launch of a user with two live tenants lands on the picker', async ({ page }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    // The user's memberships, with the sandbox made a second live tenant.
    await page.context().route(
      (url) => url.pathname === '/bff/user',
      async (route) => {
        const response = await route.fetch();
        const user = (await response.json()) as {
          tenants: { category: string; liveTenantId: number | null }[];
        };
        const tenants = user.tenants.map((tenant) => ({
          ...tenant,
          category: 'Live',
          liveTenantId: null,
        }));
        await route.fulfill({ response, json: { ...user, tenants } });
      },
    );

    await page.goto('/');

    await expect(page).toHaveURL('/tenants');
    await expect(page.getByRole('heading', { level: 1, name: 'Choose a company' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Acme', exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Acme Sandbox' })).toBeVisible();
  });

  test('a cold launch of a user with no live tenant lands on the picker', async ({ page }) => {
    await tmSignInToIdentityAs(page.context(), ACME_PRINCIPALS.sandbox);

    await page.goto('/');

    await expect(page).toHaveURL('/tenants');
    await expect(page.getByRole('link', { name: 'Acme Sandbox' })).toBeVisible();
  });

  test('a cold deep link opens its page', async ({ page }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);

    await page.goto('/1/customers');

    await expect(page.getByRole('heading', { level: 1, name: 'Customers' })).toBeVisible();
    await expect(page).toHaveURL('/1/customers');
    expect((await tmBootFacts(page)).cold).toBe(true);
  });

  // --- Fragments across a sign-in ---
  test('a signed-out drill-through link keeps its fragment across the sign-in', async ({
    page,
  }) => {
    await tmSignInToIdentityAs(page.context(), ACME_PRINCIPALS.admin);

    await page.goto('/1/list/customers#d=eyJmIjoiQ29kZSBlcSAnQzEnIn0');

    await expect(page.getByRole('heading', { level: 1, name: 'Customers' })).toBeVisible();
    await expect(page).toHaveURL('/1/list/customers#d=eyJmIjoiQ29kZSBlcSAnQzEnIn0');
    expect((await tmBootFacts(page)).email).toBe(ACME_PRINCIPALS.admin);
  });

  test('a search page keeps its filter fragment across a sign-in at a cold launch', async ({
    page,
  }) => {
    await tmSignInToIdentityAs(page.context(), ACME_PRINCIPALS.admin);

    await page.goto('/1/customers#d=eyJxIjoiYWNtZSJ9');

    await expect(page.getByRole('heading', { level: 1, name: 'Customers' })).toBeVisible();
    await expect(page).toHaveURL('/1/customers#d=eyJxIjoiYWNtZSJ9');
  });

  test('a search page keeps its filter fragment across the sign-in after an in-app expiry', async ({
    page,
  }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await page.goto('/1/customers#d=eyJxIjoiYWNtZSJ9');
    await expect(page.getByRole('heading', { level: 1, name: 'Customers' })).toBeVisible();

    await expireInApp(page);
    await sessionEndedDialog(page).getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL('/1/customers#d=eyJxIjoiYWNtZSJ9');
    await expect(sessionEndedDialog(page)).toHaveCount(0);
    expect((await tmBootFacts(page)).state).toBe('run');
  });

  // --- Warm launches ---
  test('a warm launch runs from the device and returns to the last tenant', async ({ page }) => {
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1/customers');

    await page.goto('/');

    await expect(page).toHaveURL('/1');
    await expect(page.getByRole('heading', { level: 1, name: 'Home' })).toBeVisible();
    const facts = await tmBootFacts(page);
    expect(facts.cold).toBe(false);
    expect(facts.auto).toBe(1);
  });

  test('the theme is in place before the first paint', async ({ page }) => {
    await recordFirstPaint(page.context());
    await signInAs(page, ACME_PRINCIPALS.admin);
    await warmUp(page, '/1');

    // A dark device on a cold boot record, then the user's own light choice on a warm one.
    await page.emulateMedia({ colorScheme: 'dark' });
    await page.evaluate(() => localStorage.removeItem('tm.boot'));
    await page.reload();
    expect((await firstPaint(page)).theme).toBe('dark');

    await expect.poll(async () => (await tmBootRecord(page))?.sub ?? null).not.toBeNull();
    await page.evaluate(() => {
      const record = JSON.parse(localStorage.getItem('tm.boot') ?? '{}') as Record<string, unknown>;
      localStorage.setItem('tm.boot', JSON.stringify({ ...record, theme: 'light' }));
    });
    await page.reload();
    expect((await firstPaint(page)).theme).toBe('light');
  });

  // --- The boot page and the signed-out page ---
  test('a browser without Web Locks gets the boot page, which is accessible', async ({ page }) => {
    await removeWebLocks(page.context());

    await page.goto('/1');

    const main = page.locator('main.tm-boot-page');
    await expect(main).toBeVisible();
    await expect(main.getByRole('heading', { level: 1 })).toBeFocused();
    await expect(main.getByRole('button')).toBeVisible();
    expect((await tmBootFacts(page)).state).toBe('page');
    await expectNoAxeViolations(page);
  });

  test('the signed-out page renders with no API or BFF request', async ({ page }) => {
    const requests: string[] = [];
    page.context().on('request', (request) => {
      const { pathname } = new URL(request.url());
      if (/^\/(?:api|bff|mcp)(?:\/|$)|^\/\d+\/(?:api|hub)(?:\/|$)/.test(pathname)) {
        requests.push(pathname);
      }
    });

    await page.goto('/signed-out');

    await expect(page.getByRole('heading', { level: 1, name: "You're signed out" })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await expectNoAxeViolations(page);
    expect(requests).toEqual([]);
  });
});
