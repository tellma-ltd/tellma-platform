// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';

// The launcher (spec 0027 §9) against the real distribution: Mod+K opens it
// on the app bar's field, typing "cent" lists Centers first (in Arabic, by
// its English label), and Enter opens it. The launcher's panel is a chunk
// of its own, which the shell loads once the browser is idle, so the first
// open finds it in memory.

test.describe('the launcher', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');
  });

  test('Mod+K, then "cent" and Enter, opens Centers', async ({ page }) => {
    await page.goto('/1');
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toBeVisible();
    const field = page.locator('tm-app-bar .tm-app-bar__field input');

    await page.keyboard.press('ControlOrMeta+K');
    await expect(field).toBeFocused();
    await expect(field).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('listbox')).toBeVisible();

    await page.keyboard.type('cent');
    // The first row is active, and the input points at it.
    const active = page.getByRole('listbox').locator('[role="option"][aria-selected="true"]');
    await expect(active).toHaveAttribute('href', '/1/centers');
    await expect(field).toHaveAttribute(
      'aria-activedescendant',
      (await active.getAttribute('id'))!,
    );

    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/1\/centers$/);
    await expect(page.getByRole('listbox')).toHaveCount(0);
    // The text stays for the next open.
    await expect(field).toHaveValue('cent');
  });
});
