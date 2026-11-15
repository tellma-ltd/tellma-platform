// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { heldResponses, holdResponses, releaseResponses } from '../support/held-responses';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-pager: the axe floor in both themes and
 * directions, the status read as a sentence, a click while a page loads
 * writing the page again, the buttons standing still from page 1 to page 6
 * of 120, a capped count, an unknown total and item mode.
 */

/** The range pager over 120 rows. */
function rangePager(page: Page) {
  return page.getByTestId('pager-range');
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`pager story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('pager', { theme, dir }));
        await expect(rangePager(page).getByRole('button', { name: 'Next page' })).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

for (const dir of ['ltr', 'rtl'] as const) {
  test(`the status is a left-to-right fraction read as a sentence (${dir})`, async ({ page }) => {
    await page.goto(storyUrl('pager', { dir }));
    const fraction = rangePager(page).locator('.tm-pager__fraction');
    await expect(fraction).toHaveText('1–20 / 120');
    await expect(fraction).toHaveAttribute('dir', 'ltr');
    await expect(fraction).toHaveAttribute('aria-hidden', 'true');
    await expect(rangePager(page).locator('.tm-pager__sentence')).toHaveText(
      'Items 1 to 20 of 120',
    );
    expect(await fraction.evaluate((element) => getComputedStyle(element).direction)).toBe('ltr');
  });
}

test('a click while a page loads writes the page again, and the later request wins', async ({
  page,
}) => {
  await page.goto(storyUrl('pager'));
  await expect(page.getByTestId('page-rows')).toHaveText('Rows 1 to 20');
  await holdResponses(page, 'page');
  const next = rangePager(page).getByRole('button', { name: 'Next page' });
  await next.click();
  await expect(heldResponses(page, 'page')).toHaveText('1');
  await expect(page.getByTestId('page-rows')).toHaveText('Loading…');
  // The pager never waits for the load: the second click writes page 3.
  await expect(next).toBeEnabled();
  await next.click();
  await expect(page.getByTestId('page-state')).toHaveText('Page 3 of size 20');
  await expect(page.getByTestId('page-requests')).toHaveText('Requests: 1, 2, 3');
  await releaseResponses(page, 'page');
  await expect(page.getByTestId('page-rows')).toHaveText('Rows 41 to 60');
});

test('the buttons stand still from page 1 to page 6 of 120', async ({ page }) => {
  await page.goto(storyUrl('pager'));
  const pager = rangePager(page);
  const previous = pager.getByRole('button', { name: 'Previous page' });
  const next = pager.getByRole('button', { name: 'Next page' });
  const size = pager.locator('tm-select');
  await appStable(page);
  const boxes = async () => ({
    previous: await previous.boundingBox(),
    next: await next.boundingBox(),
    size: await size.boundingBox(),
  });
  const before = await boxes();
  for (let step = 0; step < 5; step += 1) {
    await next.click();
  }
  await expect(pager.locator('.tm-pager__fraction')).toHaveText('101–120 / 120');
  await appStable(page);
  expect(await boxes()).toEqual(before);
  await expect(next).toBeDisabled();
});

test('the size select keeps its width whichever size it shows', async ({ page }) => {
  await page.goto(storyUrl('pager'));
  const select = rangePager(page).locator('tm-select');
  await appStable(page);
  const before = await select.boundingBox();
  await select.click();
  await page.getByRole('option', { name: '100 per page' }).click();
  await expect(page.getByTestId('page-state')).toHaveText('Page 1 of size 100');
  await appStable(page);
  expect(await select.boundingBox()).toEqual(before);
});

test('a capped count keeps Next enabled up to its last page', async ({ page }) => {
  await page.goto(storyUrl('pager'));
  const pager = page.getByTestId('pager-lower-bound');
  await expect(pager.locator('.tm-pager__fraction')).toHaveText('961–980 / 1,000+');
  const next = pager.getByRole('button', { name: 'Next page' });
  await next.click();
  await expect(pager.locator('.tm-pager__fraction')).toHaveText('981–1,000 / 1,000+');
  await expect(next).toBeDisabled();
});

test('an unknown total hides the status and keeps Next enabled', async ({ page }) => {
  await page.goto(storyUrl('pager'));
  const pager = page.getByTestId('pager-unknown');
  await expect(pager.locator('.tm-pager__fraction')).toHaveCount(0);
  await pager.getByRole('button', { name: 'Next page' }).click();
  await expect(pager.getByRole('button', { name: 'Previous page' })).toBeEnabled();
});

test('item mode steps through items', async ({ page }) => {
  await page.goto(storyUrl('pager'));
  const pager = page.getByTestId('pager-item');
  await expect(pager.locator('.tm-pager__sentence')).toHaveText('Item 3 of 293');
  await pager.getByRole('button', { name: 'Next item' }).click();
  await expect(pager.locator('.tm-pager__fraction')).toHaveText('4 / 293');
  await pager.getByRole('button', { name: 'Previous item' }).click();
  await expect(pager.locator('.tm-pager__fraction')).toHaveText('3 / 293');
});

test('a disabled pager disables its buttons and its size select', async ({ page }) => {
  await page.goto(storyUrl('pager'));
  const pager = page.getByTestId('pager-disabled');
  await expect(pager.getByRole('button', { name: 'Next page' })).toBeDisabled();
  await expect(pager.getByRole('button', { name: 'Previous page' })).toBeDisabled();
  await expect(pager.getByRole('combobox', { name: 'Page size' })).toHaveAttribute(
    'aria-disabled',
    'true',
  );
});
