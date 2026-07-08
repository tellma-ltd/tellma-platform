import { expect, test } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * DoD 13 in the browser: the installed pack renders Arabic strings on a
 * runtime locale switch (re-rendering ALREADY-VISIBLE errors), and the
 * Arabic face loads ON DEMAND — only once Arabic glyphs render.
 */
test('runtime locale switch re-renders visible errors; Arabic font loads on demand', async ({
  page,
}) => {
  const fontRequests: string[] = [];
  page.on('request', (request) => {
    if (/\.woff2(\?|$)/.test(request.url())) {
      fontRequests.push(request.url());
    }
  });

  await page.goto(storyUrl('i18n'));
  const input = page.getByTestId('input-email');
  const error = page.getByTestId('ff-email').locator('.tm-form-field__error');

  // Make an ENGLISH error visible.
  await input.click();
  await page.keyboard.press('Tab');
  await expect(error).toHaveText('This field is required');

  // Latin-only paint so far (the Arabic option labels sit in the UNOPENED
  // panel): unicode-range must not have fetched the Arabic face yet.
  await page.waitForLoadState('networkidle');
  expect(fontRequests.some((url) => url.includes('arabic'))).toBe(false);

  // Switch the locale at runtime: the SAME visible error re-renders Arabic…
  await page.getByTestId('lang-ar').click();
  await expect(error).toHaveText('هذا الحقل مطلوب');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');

  // :lang(ar) re-points --leading-ui (§7): body leading 1.6 → 1.9 at 16px.
  expect(await page.evaluate(() => getComputedStyle(document.body).lineHeight)).toBe('30.4px');

  // …and NOW Arabic glyphs are painted, so the face loads on demand — from
  // the app origin, no CDN.
  await expect
    .poll(() => fontRequests.filter((url) => url.includes('arabic')).length)
    .toBeGreaterThan(0);
  const origin = new URL(page.url()).origin;
  expect(
    fontRequests.filter((url) => url.includes('arabic')).every((url) => url.startsWith(origin)),
  ).toBe(true);

  // The library's Select placeholder is localized too.
  await expect(page.getByTestId('select-status')).toContainText('اختر خيارًا');

  // And back — no reload anywhere; the :lang(en) rule restores body leading.
  await page.getByTestId('lang-en').click();
  await expect(error).toHaveText('This field is required');
  expect(await page.evaluate(() => getComputedStyle(document.body).lineHeight)).toBe('25.6px');
});
