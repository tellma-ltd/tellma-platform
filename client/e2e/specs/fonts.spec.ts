import { expect, test } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * DoD 12: self-hosted woff2 + @font-face/unicode-range wiring — a Latin-only
 * page loads the Latin face from the app origin and fetches NOTHING for
 * scripts the content doesn't use, and nothing from any CDN.
 */
test('Latin page fetches only Latin subsets, self-hosted, no CDN', async ({ page }) => {
  const fontRequests: string[] = [];
  page.on('request', (request) => {
    const url = request.url();
    if (/\.(woff2?|ttf|otf)(\?|$)/.test(url)) {
      fontRequests.push(url);
    }
  });

  await page.goto(storyUrl('welcome'));
  await page.waitForLoadState('networkidle');

  // The Latin body face loads (fonts.css + unicode-range + swap wiring works)…
  expect(fontRequests.some((url) => url.includes('noto-sans-latin'))).toBe(true);
  // …every font comes from the app origin (self-hosted, intranet-safe)…
  const origin = new URL(page.url()).origin;
  expect(fontRequests.every((url) => url.startsWith(origin))).toBe(true);
  // …and no other script's face was eagerly downloaded.
  expect(fontRequests.some((url) => /arabic|ethiopic|cyrillic|greek|hebrew/.test(url))).toBe(
    false,
  );
});
