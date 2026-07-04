import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

test.describe('showcase shell', () => {
  test('index lists the registered stories and is axe-clean', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Tellma UI showcase' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Welcome' })).toBeVisible();
    await expectNoAxeViolations(page);
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
