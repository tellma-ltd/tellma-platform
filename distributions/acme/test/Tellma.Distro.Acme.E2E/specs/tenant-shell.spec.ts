// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Page } from '@playwright/test';
import { goOffline, signInAs, tmIsServerPath, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';
import { SHELL_TEXT, openAbout, sessionButton } from '../support/shell';

// The tenant shell (spec 0027 §7, §10, §11, §13): the session menu opens
// from the session button, lists its rows, gives focus back on Escape and
// opens the About dialog, whose Diagnostics tab loads the Diagnostics panel,
// and Report a problem opens the support dialog (spec 0026 §8.4, §12.1).
// About's placeholder is as tall as its About tab, and the Diagnostics tab's
// place before its panel as tall as the panel, each within 8 px, on a phone
// and at md, in each language. About opens offline from the device; with a
// newer server it offers Reload, says when the update cannot be downloaded
// and then offers Reload anyway; with an older one it says nothing of it;
// and Acme, which has no Distribution_About text, shows no description.

/** The most an About height may move when the content replaces its stand-in, in px. */
const SHIFT_PX = 8;

/** The build a newer server reports: a major version no UI of this suite reaches. */
const NEWER_BUILD = '999.0.0+0000000';

/** The build an older server reports: below every released version. */
const OLDER_BUILD = '0.0.1+0000000';

/**
 * Makes every server response the context's pages read report a build of
 * the server's, as a deployment newer or older than the UI would.
 *
 * @param page - A page of the context.
 * @param build - Whether the server is newer or older than the UI.
 */
async function reportServerBuild(page: Page, build: 'newer' | 'older'): Promise<void> {
  const reported = build === 'newer' ? NEWER_BUILD : OLDER_BUILD;
  // The API and the BFF, which answer once; the hub's long-lived connection passes as it is.
  await page.context().route(
    (url) => tmIsServerPath(url.pathname) && !url.pathname.includes('/hub'),
    async (route) => {
      const response = await route.fetch();
      await route.fulfill({
        response,
        headers: { ...response.headers(), 'tellma-build': reported },
      });
    },
  );
}

test.describe('the tenant shell', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');
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
    await page
      .getByRole('menu', { name: text.menu })
      .getByRole('menuitem', { name: text.reportProblem })
      .click();
    await expect(page.getByRole('dialog', { name: text.reportProblem })).toBeVisible();
  });

  for (const [width, height] of [
    [390, 844],
    [1280, 800],
  ] as const) {
    test(`About's placeholder and the Diagnostics tab's place before its panel keep the loaded heights at ${width} px`, async ({
      page,
      lang,
    }) => {
      const text = SHELL_TEXT[lang];
      await page.setViewportSize({ width, height });
      await page.goto('/1');
      await page.getByRole('button', { name: text.account }).click();
      await page
        .getByRole('menu', { name: text.menu })
        .getByRole('menuitem', { name: new RegExp(`^${text.about}`) })
        .click();
      const about = page.getByRole('dialog', { name: text.about });
      await expect(about.locator('tm-about-dialog dl')).toBeVisible();

      // --- The placeholder's height, probed where the placeholder stood, against the About tab ---
      const placeholder = await about.locator('tm-shell-lazy-content').evaluate((content) => {
        // The placeholder's height is a custom property of the modal's pane.
        const probe = document.createElement('div');
        probe.style.blockSize = 'var(--tm-session-panels-about-height)';
        content.parentElement!.append(probe);
        const measured = probe.getBoundingClientRect().height;
        probe.remove();
        return { measured, content: content.getBoundingClientRect().height };
      });
      expect(Math.abs(placeholder.measured - placeholder.content)).toBeLessThanOrEqual(SHIFT_PX);

      // --- The pending state's rule, probed inside the dialog's view, against the panel ---
      await about.getByRole('tab', { name: text.diagnostics }).click();
      const panel = about.locator('tm-diagnostics');
      await expect(panel).toBeVisible();
      const pending = await panel.evaluate((loaded) => {
        const dialog = loaded.closest('tm-about-dialog')!;
        const host = [...dialog.attributes].find((attribute) =>
          attribute.name.startsWith('_nghost-'),
        )!.name;
        const probe = document.createElement('div');
        probe.className = 'tm-about-dialog__pending';
        probe.setAttribute(host.replace('_nghost-', '_ngcontent-'), '');
        loaded.parentElement!.append(probe);
        const measured = parseFloat(getComputedStyle(probe).minBlockSize);
        probe.remove();
        return { measured, panel: loaded.getBoundingClientRect().height };
      });
      expect(Math.abs(pending.measured - pending.panel)).toBeLessThanOrEqual(SHIFT_PX);
    });
  }

  test('About opens offline, with the versions from the device', async ({ page, lang }) => {
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    const online = await goOffline(page);

    const about = await openAbout(page, lang);
    // The server's row holds the build the last response reported, never the unknown dash.
    await expect(about.locator('tm-about-dialog dl dd').nth(1)).toHaveText(/^\d+\.\d+\.\d+/);
    await expect(about.locator('.tm-about-dialog__newer')).toHaveCount(0);
    await online();
  });

  test('About with a newer server offers Reload, says when the update is blocked, then offers Reload anyway', async ({
    page,
    lang,
  }) => {
    const text = SHELL_TEXT[lang];
    await reportServerBuild(page, 'newer');
    // The update's download fails: the worker cannot read its manifest.
    await page.context().route(
      (url) => url.pathname === '/ngsw.json',
      (route) => route.abort('internetdisconnected'),
    );
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();

    const about = await openAbout(page, lang);
    const alert = about.locator('.tm-about-dialog__newer');
    await expect(alert).toBeVisible();
    await expect(alert).toContainText(NEWER_BUILD);
    const reload = alert.getByRole('button', { name: text.reload, exact: true });
    const anyway = alert.getByRole('button', { name: text.reloadAnyway });
    await expect(anyway).toHaveCount(0);

    // The first failure says so; from the second, Reload anyway shows, and reloads the page.
    await reload.click();
    await expect(alert).not.toContainText(NEWER_BUILD);
    await expect(reload).toBeEnabled();
    await expect(anyway).toHaveCount(0);
    await reload.click();
    await expect(anyway).toBeVisible();
    const reloaded = page.waitForEvent('load');
    await anyway.click();
    await reloaded;
    await expect(sessionButton(page, lang)).toBeVisible();
    await page.context().unrouteAll({ behavior: 'ignoreErrors' });
  });

  test('About with an older server says nothing of it, and Acme shows no description', async ({
    page,
    lang,
  }) => {
    await reportServerBuild(page, 'older');
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();

    const about = await openAbout(page, lang);
    await expect(about.locator('tm-about-dialog dl dd').nth(1)).toHaveText(OLDER_BUILD);
    await expect(about.locator('.tm-about-dialog__newer')).toHaveCount(0);
    // Acme has no Distribution_About text, so the dialog has no description.
    await expect(about.locator('.tm-about-dialog__description')).toHaveCount(0);
    await page.context().unrouteAll({ behavior: 'ignoreErrors' });
  });
});
