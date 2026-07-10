import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

test.describe('showcase shell', () => {
  test('index lists the registered stories and is axe-clean', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Tellma UI showcase' })).toBeVisible();
    // Every story appears twice: in the persistent header menu and the index.
    const nav = page.getByRole('navigation', { name: 'Stories' });
    await expect(nav.getByRole('link', { name: 'Welcome' })).toBeVisible();
    await expect(page.locator('main').getByRole('link', { name: 'Welcome' })).toBeVisible();
    // The persistent toggles are visible on the index too.
    await expect(page.getByTestId('toggle-theme')).toBeVisible();
    await expect(page.getByTestId('lang-ar')).toBeVisible();
    await expectNoAxeViolations(page);
  });

  test('the header menu and toggles persist across pages', async ({ page }) => {
    await page.goto(storyUrl('checkbox'));
    const nav = page.getByRole('navigation', { name: 'Stories' });
    await expect(nav.getByRole('link', { name: 'Text input (tmInput + tm-form-field)' })).toBeVisible();
    await expect(page.getByTestId('toggle-theme')).toBeVisible();

    // Theme toggles from ANY page, via the URL (?theme=dark), and back.
    await page.getByTestId('toggle-theme').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    await page.getByTestId('toggle-theme').click();
    await expect(page.locator('html')).not.toHaveAttribute('data-theme');

    // Language toggles from ANY page.
    await page.getByTestId('lang-ar').click();
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.getByTestId('lang-en').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  });

  test('story page applies dir and theme from query params', async ({ page }) => {
    await page.goto(storyUrl('welcome', { dir: 'rtl', theme: 'dark' }));
    await expect(page.getByRole('heading', { name: 'Welcome' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
  });

  test('story page defaults to ltr/light', async ({ page }) => {
    await page.goto(storyUrl('welcome'));
    await expect(page.getByRole('heading', { name: 'Welcome' })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.locator('html')).not.toHaveAttribute('data-theme');
  });
});
