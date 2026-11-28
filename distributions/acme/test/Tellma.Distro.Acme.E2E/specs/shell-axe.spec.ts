// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Locator, Page } from '@playwright/test';
import { expectNoAxeViolations, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';
import {
  SHELL_TEXT,
  closeHeld,
  launcherField,
  openSessionMenu,
  pinnedBar,
  servePins,
  sessionButton,
} from '../support/shell';

// The shell's overlays under axe (spec 0027 §13, §14 WCAG AA): in the light
// and the dark theme, and in each project's direction, the page passes with
// the launcher open, the session menu, the sandbox marker's popover, the pin
// popover, and each of the shell's dialogs: About, the keyboard shortcuts,
// Report a problem and Manage pins. The sync serves no pins, and the pin
// popover pins the page on the device; its write to the server is held
// unanswered, so the user's stored pins never change.

/** The themes. */
const THEMES = ['light', 'dark'] as const;

/**
 * Scans the page with an overlay open, then closes it with Escape.
 *
 * @param page - The page.
 * @param overlay - The open overlay, to wait for before the scan and after Escape.
 */
async function scanThenClose(page: Page, overlay: Locator): Promise<void> {
  await expect(overlay).toBeVisible();
  // The overlay's entrance has finished: mid-fade, its text would read as faded as it is drawn.
  await page.evaluate(() =>
    Promise.all(
      document.getAnimations().map((animation) => animation.finished.catch(() => undefined)),
    ),
  );
  await expectNoAxeViolations(page);
  await page.keyboard.press('Escape');
  await expect(overlay).toBeHidden();
}

/**
 * Opens one of the session menu's dialogs.
 *
 * @param page - The page.
 * @param lang - The project's language.
 * @param row - The row's name, which is the dialog's too.
 * @returns The open dialog.
 */
async function openDialog(page: Page, lang: AcmeLanguage, row: string): Promise<Locator> {
  const menu = await openSessionMenu(page, lang);
  await menu.getByRole('menuitem', { name: new RegExp(`^${row}`) }).click();
  return page.getByRole('dialog', { name: row });
}

test.describe('the shell under axe', () => {
  for (const theme of THEMES) {
    test.describe(theme, () => {
      test.use({ colorScheme: theme });

      test.beforeEach(async ({ page, cast }) => {
        await signInAs(page, cast.user);
        // No pins, on the device and from the server, so the customers' page pins anew.
        await servePins(page, []);
        await warmUp(page, '/2');
        await warmUp(page, '/1');
      });

      // The page closes while its writes are still held, so the writes it
      // flushes as it goes never reach the user's stored preferences.
      test.afterEach(async ({ page }) => {
        await closeHeld(page);
      });

      test('the launcher, the session menu, the marker and the pin popovers and every dialog pass axe', async ({
        page,
        lang,
      }) => {
        const text = SHELL_TEXT[lang];
        // --- The tenant's overlays ---
        await page.goto('/1/customers');
        await expect(sessionButton(page, lang)).toBeVisible();
        await page.keyboard.press('ControlOrMeta+K');
        await expect(launcherField(page)).toBeFocused();
        await scanThenClose(page, page.getByRole('listbox'));

        await openSessionMenu(page, lang);
        await scanThenClose(page, page.getByRole('menu', { name: text.menu }));

        for (const row of [text.about, text.shortcuts, text.reportProblem]) {
          await scanThenClose(page, await openDialog(page, lang, row));
        }

        await pinnedBar(page).getByRole('button', { name: text.pinThisPage }).click();
        await scanThenClose(page, page.locator('.tm-pin-overlays__popover'));

        await pinnedBar(page).getByRole('button', { name: text.managePins }).click();
        await scanThenClose(page, page.getByRole('dialog', { name: text.managePins }));

        // --- The sandbox's marker ---
        await page.goto('/2');
        const marker = page.locator('tm-app-bar .tm-app-bar__marker[data-tm-marker="sandbox"]');
        await marker.click();
        await expect(marker).toHaveAttribute('aria-expanded', 'true');
        await scanThenClose(page, page.getByRole('dialog'));
      });
    });
  }
});
