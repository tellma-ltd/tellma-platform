// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

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

  // Content hashing (§7.1): every fetched font URL carries its hash, so the
  // files can be cached immutable.
  expect(fontRequests.every((url) => /\.[0-9a-f]{10}\.woff2$/.test(url))).toBe(true);

  // The preload manifest and @font-face resolve to the SAME URLs: every
  // injected preload href was actually fetched, and exactly once — a
  // mismatch would 404 or double-download.
  const preloadHrefs = await page.$$eval('link[rel="preload"][as="font"]', (links) =>
    links.map((l) => (l as HTMLLinkElement).href),
  );
  expect(preloadHrefs.length).toBeGreaterThan(0);
  for (const href of preloadHrefs) {
    expect(fontRequests.filter((url) => url === href)).toHaveLength(1);
  }
});

/**
 * Per-glyph face selection (§7): --font-ui is one multi-script stack, so
 * Arabic content renders in the brand Arabic face even in the English/LTR
 * root — the glyphs, not the page language or direction, select the face
 * (and trigger its on-demand download).
 */
test('Arabic content in the LTR root loads the Arabic face per glyph', async ({ page }) => {
  const fontRequests: string[] = [];
  page.on('request', (request) => {
    if (/\.woff2(\?|$)/.test(request.url())) {
      fontRequests.push(request.url());
    }
  });

  // The input story's bidi demo carries Arabic sample text; the page itself
  // stays English and LTR.
  await page.goto(storyUrl('input'));
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');

  await expect
    .poll(() => fontRequests.filter((url) => url.includes('arabic')).length)
    .toBeGreaterThan(0);
  const origin = new URL(page.url()).origin;
  expect(
    fontRequests.filter((url) => url.includes('arabic')).every((url) => url.startsWith(origin)),
  ).toBe(true);
});
