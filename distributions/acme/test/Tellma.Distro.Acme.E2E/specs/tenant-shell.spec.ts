// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { holdNetwork, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';

// The tenant shell (spec 0027 §7, §10, §11): a warm launch of the live
// tenant draws the whole app bar from the device with every server request
// held, and the session menu opens from the session button, lists its rows,
// gives focus back on Escape and opens the About dialog, whose Diagnostics
// tab loads the Diagnostics panel, and Report a problem opens the support
// dialog (spec 0026 §8.4, §12.1).

/** The shell's texts the flows match, in each project's language. */
const SHELL_TEXT: Readonly<
  Record<
    AcmeLanguage,
    {
      readonly account: RegExp;
      readonly menu: string;
      readonly mySettings: string;
      readonly about: string;
      readonly diagnostics: string;
      readonly reportProblem: string;
      readonly signOut: string;
    }
  >
> = {
  en: {
    account: /^Account: .+, .+$/,
    menu: 'Account',
    mySettings: 'My settings',
    about: 'About',
    diagnostics: 'Diagnostics',
    reportProblem: 'Report a problem',
    signOut: 'Sign out',
  },
  ar: {
    account: /^الحساب: .+، .+$/,
    menu: 'الحساب',
    mySettings: 'إعداداتي',
    about: 'حول',
    diagnostics: 'التشخيص',
    reportProblem: 'الإبلاغ عن مشكلة',
    signOut: 'تسجيل الخروج',
  },
};

test.describe('the tenant shell', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');
  });

  test('a warm launch draws the whole app bar with every server request held', async ({
    page,
    lang,
  }) => {
    const hold = await holdNetwork(page);
    await page.goto('/1', { waitUntil: 'commit' });
    const session = page.getByRole('button', { name: SHELL_TEXT[lang].account });
    await expect(session).toBeVisible();
    // The tenant's name, never its skeleton, and the reserved boxes of the bell and the tray.
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toHaveCount(1);
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant-skeleton')).toHaveCount(0);
    await expect(page.locator('tm-app-bar span[data-tm-slot="bell"]')).toHaveCSS('width', '30px');
    await expect(page.locator('tm-app-bar span[data-tm-slot="tray"]')).toHaveCSS('width', '30px');
    await hold.release();
  });

  test('the session menu opens from the session button, gives focus back on Escape and opens About', async ({
    page,
    lang,
  }) => {
    const text = SHELL_TEXT[lang];
    await page.goto('/1');
    const session = page.getByRole('button', { name: text.account });
    await session.click();
    const menu = page.getByRole('menu', { name: text.menu });
    await expect(menu).toBeVisible();
    await expect(session).toHaveAttribute('aria-expanded', 'true');
    await expect(menu.getByRole('menuitem', { name: text.mySettings })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: text.reportProblem })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: text.signOut })).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(menu).toBeHidden();
    await expect(session).toBeFocused();

    await session.click();
    await menu.getByRole('menuitem', { name: new RegExp(`^${text.about}`) }).click();
    const about = page.getByRole('dialog', { name: text.about });
    await expect(about).toBeVisible();
    await expect(about.locator('tm-about-dialog dl')).toBeVisible();

    // The Diagnostics tab loads the panel from the support entry point.
    await about.getByRole('tab', { name: text.diagnostics }).click();
    await expect(about.locator('tm-diagnostics')).toBeVisible();
  });

  test('Report a problem opens the support dialog', async ({ page, lang }) => {
    const text = SHELL_TEXT[lang];
    await page.goto('/1');
    await page.getByRole('button', { name: text.account }).click();
    await page.getByRole('menu', { name: text.menu }).getByRole('menuitem', { name: text.reportProblem }).click();
    await expect(page.getByRole('dialog', { name: text.reportProblem })).toBeVisible();
  });
});
