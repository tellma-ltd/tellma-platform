import { expect, test } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * DoD 9 (token half): the brand preset renders in light and dark, a runtime
 * CSS-variable override restyles live, and the @layer tm.base/tm.theme
 * precedence holds regardless of stylesheet load order.
 */

const TEAL_600 = 'rgb(49, 110, 128)'; // #316E80 — --color-primary (light)
const DARK_FIELD = 'rgb(22, 37, 45)'; // #16252D — --white in the dark scheme

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('theming'));
});

test('brand preset paints the primary action from tokens', async ({ page }) => {
  const primary = page.getByTestId('swatch-primary');
  await expect(primary).toHaveCSS('background-color', TEAL_600);
});

test('dark scheme swaps the variable set ([data-theme=dark])', async ({ page }) => {
  const field = page.getByTestId('swatch-field');
  await expect(field).toHaveCSS('background-color', 'rgb(254, 254, 254)'); // --white light

  await page.getByTestId('toggle-theme').click();
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  await expect(field).toHaveCSS('background-color', DARK_FIELD);
});

test('runtime setProperty override restyles instantly, no rebuild', async ({ page }) => {
  const primary = page.getByTestId('swatch-primary');
  await expect(primary).toHaveCSS('background-color', TEAL_600);

  // The settings-screen path (§4): one variable write on a scope.
  await page.evaluate(() =>
    document.documentElement.style.setProperty('--color-primary', 'rgb(200, 16, 46)'),
  );
  await expect(primary).toHaveCSS('background-color', 'rgb(200, 16, 46)');
});

test('@layer precedence: a tm.theme delta wins over tm.base regardless of load order', async ({
  page,
}) => {
  const primary = page.getByTestId('swatch-primary');
  await expect(primary).toHaveCSS('background-color', TEAL_600);

  // Insert the distribution delta sheet BEFORE every other stylesheet in the
  // document — earlier in document order than the base sheet. If precedence
  // depended on load order this would lose; with the canonical layer
  // statement it must win.
  await page.evaluate(() => {
    const style = document.createElement('style');
    style.textContent =
      '@layer tm.base, tm.theme;\n' +
      '@layer tm.theme { :root { --color-primary: rgb(10, 120, 60); } }';
    document.head.insertBefore(style, document.head.firstChild);
  });
  await expect(primary).toHaveCSS('background-color', 'rgb(10, 120, 60)');
});
