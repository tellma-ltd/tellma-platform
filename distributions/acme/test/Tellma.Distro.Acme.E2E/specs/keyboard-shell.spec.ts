// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Page } from '@playwright/test';
import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import { type AcmeLanguage, expect, test } from '../support/acme';
import {
  SHELL_TEXT,
  closeHeld,
  pinChips,
  pinnedBar,
  servePins,
  sessionButton,
} from '../support/shell';

// The shell by keyboard (spec 0027 §4, §8.7, §10, §14 WCAG AA): the skip
// link is the first tab stop and moves the focus past the chrome; the pinned
// bar's chips are one tab stop, whose arrows rove in the reading direction;
// the session menu opens with Enter, moves with the arrows and gives the
// focus back on Escape; Mod+/ opens the shortcuts dialog and Alt+Shift+P
// pins the page; and no character key alone does anything. The pins are
// served in the sync's answer and their writes held, so the user's stored
// pins never change.

/** The skip link's text in each language. */
const SKIP: Readonly<Record<AcmeLanguage, string>> = {
  en: 'Skip to content',
  ar: 'الانتقال إلى المحتوى',
};

/**
 * The element that holds the focus, by its tag, its classes and its name.
 *
 * @param page - The page.
 * @returns A short description.
 */
function focused(page: Page): Promise<string> {
  return page.evaluate(() => {
    const element = document.activeElement;
    return element === null
      ? ''
      : `${element.localName}.${[...element.classList].join('.')} ${element.getAttribute('aria-label') ?? element.textContent?.trim() ?? ''}`;
  });
}

test.describe('the shell by keyboard', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await servePins(page, [
      { id: 'customer', url: 'customers', title: 'Customers' },
      { id: 'centersx', url: 'centers', title: 'Centers' },
      { id: 'invoices', url: 'sales-invoices', title: 'Sales invoices' },
    ]);
    await warmUp(page, '/1');
  });

  // The page closes while its writes are still held, so the writes it flushes
  // as it goes never reach the user's stored preferences.
  test.afterEach(async ({ page }) => {
    await closeHeld(page);
  });

  test('the skip link comes first and moves the focus past the chrome', async ({ page, lang }) => {
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    await page.keyboard.press('Tab');
    const skip = page.getByRole('link', { name: SKIP[lang] });
    await expect(skip).toBeFocused();
    await expect(skip).toBeVisible();

    await page.keyboard.press('Enter');
    // The focus is in the page now, past the app bar and the pinned bar.
    await expect
      .poll(() =>
        page.evaluate(() => document.activeElement?.closest('main, [role="main"]') !== null),
      )
      .toBe(true);
  });

  test("the pinned bar's chips are one tab stop, whose arrows follow the reading direction", async ({
    page,
    lang,
  }) => {
    await page.goto('/1');
    await expect(pinChips(page)).toHaveCount(3);
    // The bar's chips take one stop: Tab from the first chip leaves the list.
    const chips = pinChips(page);
    await chips.first().focus();
    await expect(chips.first()).toHaveAttribute('tabindex', '0');
    await expect(chips.nth(1)).toHaveAttribute('tabindex', '-1');

    const rtl = lang === 'ar';
    await page.keyboard.press(rtl ? 'ArrowLeft' : 'ArrowRight');
    await expect(chips.nth(1)).toBeFocused();
    await page.keyboard.press(rtl ? 'ArrowRight' : 'ArrowLeft');
    await expect(chips.first()).toBeFocused();
    await page.keyboard.press('End');
    await expect(chips.last()).toBeFocused();

    await page.keyboard.press('Tab');
    expect(
      await chips.evaluateAll((all) => all.some((chip) => chip === document.activeElement)),
      'Tab leaves the chips',
    ).toBe(false);
  });

  test('the session menu opens with Enter, moves with the arrows and gives the focus back on Escape', async ({
    page,
    lang,
  }) => {
    const text = SHELL_TEXT[lang];
    await page.goto('/1');
    const session = sessionButton(page, lang);
    await session.focus();
    await page.keyboard.press('Enter');
    const menu = page.getByRole('menu', { name: text.menu });
    await expect(menu).toBeVisible();
    const first = await focused(page);
    await page.keyboard.press('ArrowDown');
    expect(await focused(page)).not.toBe(first);
    await page.keyboard.press('Escape');
    await expect(menu).toBeHidden();
    await expect(session).toBeFocused();
  });

  test('Mod+/ opens the shortcuts, Alt+Shift+P pins and unpins the page, and a character key alone does nothing', async ({
    page,
    lang,
  }) => {
    const shell = SHELL_TEXT[lang];
    await page.goto('/1/credit-review');
    await expect(page.getByRole('heading', { level: 1, name: shell.creditReview })).toBeVisible();

    // --- No character key alone opens anything ---
    await page.locator('main h1').focus();
    for (const key of ['k', 'p', '/', '?', 'g']) {
      await page.keyboard.press(key);
    }
    await expect(page.getByRole('dialog')).toHaveCount(0);
    await expect(page.getByRole('listbox')).toHaveCount(0);

    // --- Mod+/ opens the shortcuts dialog, which Escape closes ---
    await page.keyboard.press('ControlOrMeta+/');
    const shortcuts = page.getByRole('dialog', { name: shell.shortcuts });
    await expect(shortcuts).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(shortcuts).toHaveCount(0);

    // --- Alt+Shift+P pins the routed page, which has no pin, then unpins it ---
    const current = pinnedBar(page).locator('a.tm-pinned-bar__chip--current');
    await expect(current).toHaveCount(0);
    await page.keyboard.press('Alt+Shift+P');
    await expect(current).toHaveCount(1);
    await expect(pinnedBar(page).getByRole('button', { name: shell.editPin })).toBeVisible();
    // The new pin's "Pinned" popover holds the focus in its name field; Escape keeps the pin
    // as created and gives the focus back to the control, where the shortcut unpins at once.
    const pinned = page.locator('.tm-pin-overlays__popover').getByRole('heading', {
      name: shell.pinned,
    });
    await expect(pinned).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(pinned).toBeHidden();
    await page.keyboard.press('Alt+Shift+P');
    await expect(current).toHaveCount(0);
    await expect(pinnedBar(page).getByRole('button', { name: shell.pinThisPage })).toBeVisible();
  });
});
