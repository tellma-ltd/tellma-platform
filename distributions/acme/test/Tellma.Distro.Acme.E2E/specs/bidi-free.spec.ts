// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs } from '@tellma/core-testing/e2e';

import { expect, removeWebLocks, test } from '../support/acme';

// Bidi-free text (spec 0024 §14): no text the app writes holds a
// bidirectional control character; direction comes from markup. Both
// projects run it, so the Arabic one checks the texts in Arabic.

/** The bidirectional control characters no text may hold: ALM, LRM, RLM, the embeddings, overrides and isolates. */
const BIDI_CONTROLS = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/u;

test.describe('bidi-free', () => {
  test('the boot page and its title hold no bidirectional control character', async ({ page }) => {
    await removeWebLocks(page.context());

    await page.goto('/1');

    const main = page.locator('main.tm-boot-page');
    await expect(main).toBeVisible();
    expect(await main.innerText()).not.toMatch(BIDI_CONTROLS);
    expect(await page.title()).not.toMatch(BIDI_CONTROLS);
  });

  test('the signed-out page and its title hold no bidirectional control character', async ({
    page,
  }) => {
    await page.goto('/signed-out');

    const main = page.locator('main.tm-signed-out-page');
    await expect(main).toBeVisible();
    expect(await main.innerText()).not.toMatch(BIDI_CONTROLS);
    expect(await page.title()).not.toMatch(BIDI_CONTROLS);
  });

  test('a tenant page title holds no bidirectional control character', async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await page.goto('/1/customers');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

    await expect.poll(() => page.title()).toContain('·');
    expect(await page.title()).not.toMatch(BIDI_CONTROLS);
  });

  // A copied display name, date and grid range, and an exported cell, need the grids and exports (spec 0030).
  test.fixme('a copied display name, date and grid range and an exported cell hold no bidirectional control character', async () => {});
});
