// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import {
  activateCell,
  cell,
  gotoGrid,
  liveRegion,
  statusChip,
} from '../support/grid';
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
  await expect(page.getByTestId('select-status')).toContainText('حدد خيارا');

  // And back — no reload anywhere; the :lang(en) rule restores body leading.
  await page.getByTestId('lang-en').click();
  await expect(error).toHaveText('This field is required');
  expect(await page.evaluate(() => getComputedStyle(document.body).lineHeight)).toBe('25.6px');
});

/**
 * DoD 13 on the GRID's built-in strings: a runtime locale switch re-renders
 * the already-visible status-bar tally in Arabic, the context menu's
 * built-ins come out Arabic, and the live region announces in Arabic from
 * then on.
 */
test('a live locale switch re-renders visible grid strings and announcements in Arabic', async ({
  page,
}) => {
  await gotoGrid(page, 'grid-editable');

  // Surface a built-in grid string: the tally chip after one field error.
  await activateCell(page, 3, 0);
  await page.keyboard.press('Delete');
  await expect(statusChip(page)).toContainText('1 error');

  // Switch the locale at runtime: the SAME visible tally re-renders Arabic
  // ('خطأ واحد' — the ar plural `one` branch from the locale pack).
  await page.getByTestId('lang-ar').click();
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
  await expect(statusChip(page)).toContainText('خطأ واحد');

  // The context menu's built-in items are Arabic too ('انسخ' = Copy).
  await cell(page, 1, 0).click({ button: 'right' });
  await expect(page.getByRole('menuitem', { name: 'انسخ', exact: true })).toBeVisible();
  await page.keyboard.press('Escape');

  // And announcements now come out of the live region in Arabic.
  await activateCell(page, 1, 0);
  await cell(page, 2, 1).click({ modifiers: ['Shift'] });
  await expect(liveRegion(page)).toContainText('تم تحديد');
});

test('leading islands are real BELOW the root: marked subtrees re-lead both ways', async ({
  page,
}) => {
  await page.goto(storyUrl('i18n')); // <html lang="en">: body leading 1.6
  const leadings = await page.evaluate(() => {
    const probe = (lang: string, parent: Element) => {
      const el = document.createElement('p');
      el.lang = lang;
      el.textContent = 'قياس / probe';
      parent.append(el);
      return el;
    };
    // An Arabic island inside the English page, and an English island
    // nested back inside it.
    const arabicIsland = probe('ar', document.body);
    const englishIsland = probe('en', arabicIsland);

    // The standard app override pattern: line-height set on ONE element must
    // flow to unmarked descendants by inheritance — the emitted rules apply
    // only at [lang]-marked roots, so they must not pin descendants.
    const overridden = document.createElement('div');
    overridden.style.lineHeight = '2';
    const unmarkedChild = document.createElement('p');
    unmarkedChild.textContent = 'inherits the override';
    overridden.append(unmarkedChild);
    document.body.append(overridden);

    return {
      body: getComputedStyle(document.body).lineHeight,
      arabicIsland: getComputedStyle(arabicIsland).lineHeight,
      englishInsideArabic: getComputedStyle(englishIsland).lineHeight,
      childOfOverride: getComputedStyle(unmarkedChild).lineHeight,
    };
  });

  expect(leadings.body).toBe('25.6px'); // 1.6 × 16px
  expect(leadings.arabicIsland).toBe('30.4px'); // 1.9 × 16px — not inherited 1.6
  expect(leadings.englishInsideArabic).toBe('25.6px'); // snaps back
  expect(leadings.childOfOverride).toBe('32px'); // 2 × 16px — inheritance intact
});
