// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Live direction: the app flips `<html dir>` at run time and every library
 * surface follows — an open modal mirrors in place, and a menu opened after
 * the flip opens right-to-left. The showcase has no `[dir]` wrapper, so
 * these pass only through the library's root Directionality.
 */

/** Flips the document direction the way an app's language switch does. */
async function flipDocumentTo(page: Page, dir: 'ltr' | 'rtl'): Promise<void> {
  await page.evaluate((value) => {
    document.documentElement.dir = value;
  }, dir);
}

/** The computed CSS direction of an element. */
function directionOf(locator: Locator): Promise<string> {
  return locator.evaluate((element) => getComputedStyle(element).direction);
}

test('an open modal mirrors when the document flips to right-to-left', async ({ page }) => {
  await page.goto(storyUrl('modal'));
  await page.getByTestId('open-basic').click();
  const shell = page.locator('tm-modal-shell');
  await expect(shell).toBeVisible();
  expect(await directionOf(shell)).toBe('ltr');

  await flipDocumentTo(page, 'rtl');
  await expect.poll(() => directionOf(shell)).toBe('rtl');
  // The header's close button moves to the inline end, now the left edge.
  const close = shell.locator('.tm-modal__close');
  const title = shell.locator('.tm-modal__title');
  await expect
    .poll(async () => (await close.boundingBox())!.x < (await title.boundingBox())!.x)
    .toBe(true);

  await flipDocumentTo(page, 'ltr');
  await expect.poll(() => directionOf(shell)).toBe('ltr');
});

test('a menu closed during the flip opens right-to-left', async ({ page }) => {
  await page.goto(storyUrl('menu'));
  await flipDocumentTo(page, 'rtl');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  await page.getByTestId('menu-trigger-button').click();
  const panel = page.locator('.tm-menu__panel');
  await expect(panel).toBeVisible();
  await expect(page.locator('.cdk-overlay-connected-position-bounding-box')).toHaveAttribute(
    'dir',
    'rtl',
  );
  expect(await directionOf(panel)).toBe('rtl');
});
