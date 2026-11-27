// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmSignInToIdentityAs } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';

// The tenant home (spec 0035 §15) against the real distribution: a sign-in
// lands on /1, titled by the greeting with the user's name over today's
// date, and its grid holds the recently visited pages, the one home widget
// Acme declares today. A page the user opens from the launcher and stays on
// for its dwell then shows in that list as a link back to it.

test.describe('the tenant home', () => {
  // On a localhost scope the Angular worker prefetches its whole shell group before it answers
  // any fetch, so a worker registered on the landing would hold the launcher's and the Centers
  // page's lazy chunks behind that prefetch, which a production scope runs at idle instead.
  // This test is not about the worker.
  test.use({ serviceWorkers: 'block' });

  test('a sign-in lands on the home, its greeting over the recently visited pages, which list a page once visited', async ({
    page,
    text,
    cast,
  }) => {
    await tmSignInToIdentityAs(page.context(), cast.user);

    // --- The landing: the greeting is the page's title, over today's date ---
    await page.goto('/');
    await expect(page).toHaveURL('/1');
    const greeting = page.getByRole('heading', { level: 1, name: text.home });
    await expect(greeting).toBeVisible();
    await expect(greeting.locator('bdi')).not.toBeEmpty();
    await expect(page.locator('.tm-home-greeting__date')).not.toBeEmpty();

    // --- The grid: one region, its one tile the recently visited pages ---
    const grid = page.getByRole('region', { name: text.yourWork });
    await expect(grid).toBeVisible();
    const recents = grid.getByRole('region', { name: text.recents });
    await expect(recents).toBeVisible();
    await expect(recents.getByRole('heading', { level: 2, name: text.recents })).toBeVisible();
    await expect(grid.locator('tm-dashboard-tile')).toHaveCount(1);

    // --- A page visited past its dwell is listed, and its link leads back to it ---
    // The visit and the way back are in-app navigations, so the list the
    // home shows is this tab's own, whatever the preferences' write.
    // In Arabic too, "cent" finds Centers by its English label.
    await page.keyboard.press('ControlOrMeta+K');
    await expect(page.getByRole('listbox')).toBeVisible();
    await page.keyboard.type('cent');
    await expect(
      page.getByRole('listbox').locator('[role="option"][aria-selected="true"]'),
    ).toHaveAttribute('href', '/1/centers');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/1\/centers$/);
    // A visit counts once the page has shown for two seconds: the launcher's
    // recent pages, which read the same list, show it then. The launcher
    // opens on its last query, which the spec clears to show its lists.
    await page.keyboard.press('ControlOrMeta+K');
    await expect(page.getByRole('listbox')).toBeVisible();
    const query = page.getByRole('banner').getByRole('combobox');
    await query.fill('');
    await expect(query).toHaveValue('');
    await expect(
      page.locator(
        '[role="group"][aria-labelledby$="-recent-h"] [role="option"][href="/1/centers"]',
      ),
    ).toBeVisible({ timeout: 10_000 });
    await page.keyboard.press('Escape');
    await expect(page.getByRole('listbox')).toBeHidden();
    await page.goBack();
    await expect(page).toHaveURL('/1');
    const link = recents.locator('a[href="/1/centers"]');
    await expect(link).toBeVisible();
    await link.click();
    await expect(page).toHaveURL(/\/1\/centers$/);
  });
});
