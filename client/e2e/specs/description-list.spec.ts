// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { fontsLoaded } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for dl[tmDescriptionList]: the axe floor in both themes
 * and directions, one and two columns, `auto` switching on its own width,
 * the inline layout, and the label and value type.
 */

/**
 * Opens the story and waits until its lists are rendered and their text is
 * in its final face, so geometry reads neither count an empty list nor
 * measure a wrap point in the fallback font.
 */
async function openStory(page: Page): Promise<void> {
  await page.goto(storyUrl('description-list'));
  await expect(page.getByTestId('dl-one').locator(':scope > div')).toHaveCount(5);
  await fontsLoaded(page);
}

/** The distinct rows the list's pairs sit on. */
async function rowsOf(list: Locator): Promise<number> {
  const tops = await list
    .locator(':scope > div')
    .evaluateAll((pairs) => pairs.map((pair) => Math.round(pair.getBoundingClientRect().top)));
  return new Set(tops).size;
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`description list story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('description-list', { theme, dir }));
        await expect(page.getByTestId('dl-one')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('lays the pairs out in one or two columns, auto by its own width', async ({ page }) => {
  await openStory(page);
  // Five pairs: five rows in one column, three in two.
  expect(await rowsOf(page.getByTestId('dl-one'))).toBe(5);
  expect(await rowsOf(page.getByTestId('dl-two'))).toBe(3);
  expect(await rowsOf(page.getByTestId('dl-auto-wide'))).toBe(3);
  expect(await rowsOf(page.getByTestId('dl-auto-narrow'))).toBe(5);
});

test('puts the label beside the value inline, above it stacked', async ({ page }) => {
  await openStory(page);
  const sameLine = (id: string) =>
    page
      .getByTestId(id)
      .locator(':scope > div')
      .first()
      .evaluate((pair) => {
        const label = pair.querySelector('dt')!.getBoundingClientRect();
        const value = pair.querySelector('dd')!.getBoundingClientRect();
        // On one line when their boxes overlap vertically.
        return label.top < value.bottom && value.top < label.bottom;
      });
  expect(await sameLine('dl-inline')).toBe(true);
  expect(await sameLine('dl-one')).toBe(false);
});

test('labels are 12 px secondary text, values 13 px', async ({ page }) => {
  await openStory(page);
  const sizes = await page
    .getByTestId('dl-one')
    .locator(':scope > div')
    .first()
    .evaluate((pair) => [
      getComputedStyle(pair.querySelector('dt')!).fontSize,
      getComputedStyle(pair.querySelector('dd')!).fontSize,
    ]);
  expect(sizes).toEqual(['12px', '13px']);
});
