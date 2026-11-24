// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';
import { expectNoAxeViolations, signInAs, tmExpireSession, warmUp } from '@tellma/core-testing/e2e';

import { ACME_PRINCIPALS, comeIntoViewUntil, expect, removeWebLocks, test } from '../support/acme';

// Responsive and RTL (spec 0024 §13): the boot page and the session-ended
// dialog at every breakpoint's width, in English and Arabic, in both themes,
// fit the viewport with no horizontal scroll and pass axe.

/** One width per breakpoint: a phone, a tablet, a small and a large desktop. */
const WIDTHS = [390, 768, 992, 1440] as const;

/** The languages, one of each direction. */
const LANGUAGES = [
  { lang: 'en', dir: 'ltr' },
  { lang: 'ar', dir: 'rtl' },
] as const;

/** The themes. */
const THEMES = ['light', 'dark'] as const;

/**
 * Checks the page at every width: nothing scrolls sideways, and axe finds no violation.
 *
 * @param page - The page, showing the surface.
 * @param selector - The surface.
 */
async function checkEveryWidth(page: Page, selector: string): Promise<void> {
  for (const width of WIDTHS) {
    await page.setViewportSize({ width, height: 800 });
    await expect(page.locator(selector)).toBeVisible();
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    );
    expect(overflow, `${width} px scrolls sideways`).toBeLessThanOrEqual(0);
    await expectNoAxeViolations(page, selector);
  }
}

test.describe('responsive and RTL', () => {
  for (const { lang, dir } of LANGUAGES) {
    for (const theme of THEMES) {
      test.describe(`${lang}, ${theme}`, () => {
        test.use({ locale: lang, colorScheme: theme });

        test('the boot page fits every width and passes axe', async ({ page }) => {
          await removeWebLocks(page.context());
          await page.goto('/1');

          await expect(page.locator('html')).toHaveAttribute('dir', dir);
          await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
          await checkEveryWidth(page, 'main.tm-boot-page');
        });

        test('the session-ended dialog fits every width and passes axe', async ({ page }) => {
          await signInAs(page, ACME_PRINCIPALS.sales);
          await warmUp(page, '/1');
          // The language is the boot record's on a warm launch.
          await page.evaluate(
            (language) => {
              const record = JSON.parse(localStorage.getItem('tm.boot') ?? '{}') as Record<
                string,
                unknown
              >;
              localStorage.setItem(
                'tm.boot',
                JSON.stringify({
                  ...record,
                  lang: language.lang,
                  dir: language.dir,
                }),
              );
            },
            { lang, dir },
          );
          await page.reload();
          await expect(page.locator('html')).toHaveAttribute('lang', lang);
          await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

          await tmExpireSession(page.context());
          await comeIntoViewUntil(page, page.getByRole('alertdialog'));

          await expect(page.locator('html')).toHaveAttribute('dir', dir);
          await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
          await checkEveryWidth(page, '[role="alertdialog"]');
        });
      });
    }
  }
});
