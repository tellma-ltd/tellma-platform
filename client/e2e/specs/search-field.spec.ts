// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-search-field: the axe floor in both themes and
 * directions; the clear button and Escape clearing through an edit, so
 * Mod+Z brings the text back, on every engine; and the clear slot reserved
 * so the text never moves.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`search-field story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('search-field', { theme, dir }));
        await expect(page.getByTestId('search-filled').getByRole('searchbox')).toHaveValue(
          'INV-1001',
        );
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the clear button clears, emits and refocuses; Mod+Z brings the text back @cross-engine', async ({
  page,
}) => {
  await page.goto(storyUrl('search-field'));
  const input = page.getByTestId('search-main').getByRole('searchbox');
  await input.click();
  await page.keyboard.type('jed');
  await expect(page.getByTestId('search-value')).toHaveText('jed');

  await page.getByTestId('search-main').getByRole('button', { name: 'Clear search' }).click();
  await expect(input).toHaveValue('');
  await expect(input).toBeFocused();
  await expect(page.getByTestId('search-value')).toHaveText('');
  // The clear emitted the empty query.
  await expect(page.getByTestId('search-emitted')).toHaveText(/"jed",""\]$/);

  await page.keyboard.press('ControlOrMeta+z');
  await expect(input).toHaveValue('jed');
  await expect(page.getByTestId('search-value')).toHaveText('jed');
});

test('Escape clears the text; Mod+Z brings it back @cross-engine', async ({ page }) => {
  await page.goto(storyUrl('search-field'));
  const input = page.getByTestId('search-main').getByRole('searchbox');
  await input.click();
  await page.keyboard.type('acme');
  await expect(page.getByTestId('search-value')).toHaveText('acme');

  await page.keyboard.press('Escape');
  await expect(input).toHaveValue('');
  await expect(page.getByTestId('search-value')).toHaveText('');

  await page.keyboard.press('ControlOrMeta+z');
  await expect(input).toHaveValue('acme');
  await expect(page.getByTestId('search-value')).toHaveText('acme');
});

test('the first keystroke emits at once and the text never moves for the clear button', async ({
  page,
}) => {
  await page.goto(storyUrl('search-field'));
  const input = page.getByTestId('search-main').getByRole('searchbox');
  const before = (await input.boundingBox())!;
  await input.click();
  await page.keyboard.type('j');
  await expect(page.getByTestId('search-emitted')).toHaveText('["j"]');
  await expect(
    page.getByTestId('search-main').getByRole('button', { name: 'Clear search' }),
  ).toBeVisible();
  expect(await input.boundingBox()).toEqual(before);
});
