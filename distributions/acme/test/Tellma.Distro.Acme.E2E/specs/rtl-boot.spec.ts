// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs, tmBootRecord, warmUp } from '@tellma/core-testing/e2e';

import {
  ACME_PRINCIPALS,
  expect,
  firstPaint,
  recordFirstPaint,
  removeWebLocks,
  test,
} from '../support/acme';

// RTL before the first paint (spec 0024 §14): in Arabic the first paint is
// already right to left, before any script but the boot script ran.

test.describe('rtl-boot', () => {
  test.use({ locale: 'ar' });

  test('the boot page paints right to left from its first frame', async ({ page }) => {
    await recordFirstPaint(page.context());
    await removeWebLocks(page.context());

    await page.goto('/1');

    expect(await firstPaint(page)).toMatchObject({ lang: 'ar', dir: 'rtl' });
    await expect(page.locator('main.tm-boot-page h1')).toHaveAttribute('dir', 'rtl');
  });

  test('the signed-out page paints right to left from its first frame', async ({ page }) => {
    await recordFirstPaint(page.context());

    await page.goto('/signed-out');

    expect(await firstPaint(page)).toMatchObject({ lang: 'ar', dir: 'rtl' });
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  });

  test('a warm launch in Arabic paints right to left from its first frame', async ({ page }) => {
    await recordFirstPaint(page.context());
    await signInAs(page, ACME_PRINCIPALS.sales);
    await warmUp(page, '/1');
    await page.evaluate(() => {
      const record = JSON.parse(localStorage.getItem('tm.boot') ?? '{}') as Record<string, unknown>;
      localStorage.setItem('tm.boot', JSON.stringify({ ...record, lang: 'ar', dir: 'rtl' }));
    });

    await page.reload();

    expect(await firstPaint(page)).toMatchObject({ lang: 'ar', dir: 'rtl' });
    expect((await tmBootRecord(page))?.lang).toBe('ar');
  });
});
