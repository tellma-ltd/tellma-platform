// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * The System / Light / Dark theme through TmTheme: a toggle repaints in the
 * click's own task, System follows the OS preference live, and
 * `<html data-theme>` only ever holds light or dark.
 */

const LIGHT_FIELD = 'rgb(254, 254, 254)'; // --white, the light field surface
const DARK_FIELD = 'rgb(22, 37, 45)'; // #16252D, the dark field surface

/** Records every value `<html data-theme>` takes from now on. */
async function recordThemeWrites(page: Page): Promise<void> {
  await page.evaluate(() => {
    const writes: (string | null)[] = [];
    (window as unknown as { themeWrites: (string | null)[] }).themeWrites = writes;
    new MutationObserver(() =>
      writes.push(document.documentElement.getAttribute('data-theme')),
    ).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
  });
}

/** The values recorded by {@link recordThemeWrites}. */
function themeWrites(page: Page): Promise<(string | null)[]> {
  return page.evaluate(() => (window as unknown as { themeWrites: (string | null)[] }).themeWrites);
}

test('the shell toggle repaints in the click and never writes data-theme=system', async ({
  page,
}) => {
  await page.goto(storyUrl('theming'));
  const html = page.locator('html');
  const field = page.getByTestId('swatch-field');
  await expect(html).toHaveAttribute('data-theme', 'light');
  await expect(field).toHaveCSS('background-color', LIGHT_FIELD);
  await recordThemeWrites(page);

  // A document-level listener runs after the button's own handler in the
  // same dispatch: the attribute must already have changed by then.
  await page.evaluate(() => {
    document.addEventListener('click', (event) => {
      if ((event.target as Element).closest('[data-testid="toggle-theme"]')) {
        (window as unknown as { themeInClick: string | null }).themeInClick =
          document.documentElement.getAttribute('data-theme');
      }
    });
  });
  await page.getByTestId('toggle-theme').click();
  expect(
    await page.evaluate(() => (window as unknown as { themeInClick: string }).themeInClick),
  ).toBe('dark');
  await expect(html).toHaveAttribute('data-theme', 'dark');
  await expect(field).toHaveCSS('background-color', DARK_FIELD);
  await expect(page).toHaveURL(/theme=dark/);

  await page.getByTestId('toggle-theme').click();
  await expect(html).toHaveAttribute('data-theme', 'light');
  await expect(field).toHaveCSS('background-color', LIGHT_FIELD);
  await expect(page).not.toHaveURL(/theme=/);

  expect(await themeWrites(page)).toEqual(['dark', 'light']);
});

test('System, Light and Dark each paint a two-state attribute', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto(storyUrl('theming'));
  const html = page.locator('html');
  const readout = page.getByTestId('theme-readout');
  await recordThemeWrites(page);

  await page.getByTestId('theme-system').click();
  await expect(readout).toHaveText('system → dark');
  await expect(html).toHaveAttribute('data-theme', 'dark');

  await page.getByTestId('theme-light').click();
  await expect(readout).toHaveText('light → light');
  await expect(html).toHaveAttribute('data-theme', 'light');

  await page.getByTestId('theme-dark').click();
  await expect(readout).toHaveText('dark → dark');

  const writes = await themeWrites(page);
  expect(writes.length).toBeGreaterThan(0);
  expect(writes.every((value) => value === 'light' || value === 'dark')).toBe(true);
});

test('System follows the OS preference live, and stops when a scheme is pinned', async ({
  page,
}) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto(storyUrl('theming', { theme: 'system' }));
  const html = page.locator('html');
  const field = page.getByTestId('swatch-field');
  await expect(html).toHaveAttribute('data-theme', 'dark');
  await expect(field).toHaveCSS('background-color', DARK_FIELD);

  await page.emulateMedia({ colorScheme: 'light' });
  await expect(html).toHaveAttribute('data-theme', 'light');
  await expect(field).toHaveCSS('background-color', LIGHT_FIELD);

  await page.getByTestId('theme-dark').click();
  await expect(html).toHaveAttribute('data-theme', 'dark');
  // Pinned: an OS change no longer reaches the page.
  await page.emulateMedia({ colorScheme: 'light' });
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.emulateMedia({ colorScheme: 'light' });
  await expect(page.getByTestId('theme-readout')).toHaveText('dark → dark');
  await expect(html).toHaveAttribute('data-theme', 'dark');
});

for (const theme of ['light', 'dark'] as const) {
  test(`the theming story is axe-clean (${theme})`, async ({ page }) => {
    await page.goto(storyUrl('theming', { theme }));
    await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
    await expectNoAxeViolations(page);
  });
}
