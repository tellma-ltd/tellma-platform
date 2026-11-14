// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-radio-group: the axe floor in both themes and
 * directions, one tab stop at the checked radio, and the arrows mapped by
 * the live direction, wrapping over the enabled radios.
 */

/** Flips the document direction the way an app's language switch does. */
async function flipDocumentTo(page: Page, dir: 'ltr' | 'rtl'): Promise<void> {
  await page.evaluate((value) => {
    document.documentElement.dir = value;
  }, dir);
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`radio story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('radio', { theme, dir }));
        await expect(page.getByTestId('ff-required').locator('.tm-form-field__error')).toHaveText(
          'Choose how the order is delivered',
        );
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('Tab enters the group at the checked radio, and leaves it in one stop', async ({ page }) => {
  await page.goto(storyUrl('radio'));
  const group = page.getByTestId('radio-terms');
  await expect(group.getByRole('radio', { name: 'Net 30' })).toBeChecked();
  // A click on the heading sets the sequential-focus starting point before the group.
  await page.getByRole('heading', { name: 'Radio group' }).click();
  await page.keyboard.press('Tab');
  await expect(group.getByRole('radio', { name: 'Net 30' })).toBeFocused();
  await page.keyboard.press('Tab');
  await expect(
    page.getByTestId('radio-horizontal').getByRole('radio', { name: 'Draft' }),
  ).toBeFocused();
});

test('the arrows check and move in RTL, Left and Right mapped, skipping disabled radios', async ({
  page,
}) => {
  await page.goto(storyUrl('radio', { dir: 'rtl' }));
  const group = page.getByTestId('radio-terms');
  await group.getByRole('radio', { name: 'Net 30' }).focus();
  // In RTL, Left is "next": Net 60 is disabled, so Card is next.
  await page.keyboard.press('ArrowLeft');
  await expect(group.getByRole('radio', { name: 'Card' })).toBeFocused();
  await expect(group.getByRole('radio', { name: 'Card' })).toBeChecked();
  await expect(page.getByTestId('model-json')).toContainText('"terms":"card"');
  // Past the end it wraps to the first.
  await page.keyboard.press('ArrowLeft');
  await expect(group.getByRole('radio', { name: 'Cash' })).toBeChecked();
  // Right is "previous", wrapping back to the last.
  await page.keyboard.press('ArrowRight');
  await expect(group.getByRole('radio', { name: 'Card' })).toBeChecked();
  // Up and Down never mirror.
  await page.keyboard.press('ArrowUp');
  await expect(group.getByRole('radio', { name: 'Net 30' })).toBeChecked();
  await page.keyboard.press('ArrowDown');
  await expect(group.getByRole('radio', { name: 'Card' })).toBeChecked();
});

test('the arrows follow a live switch of direction with no reload', async ({ page }) => {
  await page.goto(storyUrl('radio'));
  const group = page.getByTestId('radio-horizontal');
  await group.getByRole('radio', { name: 'Draft' }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(group.getByRole('radio', { name: 'Posted' })).toBeChecked();
  await flipDocumentTo(page, 'rtl');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.keyboard.press('ArrowLeft');
  await expect(group.getByRole('radio', { name: 'Void' })).toBeChecked();
  await expect(page.getByTestId('model-json')).toContainText('"state":"void"');
});

test('a readonly group moves focus but keeps its value', async ({ page }) => {
  await page.goto(storyUrl('radio'));
  const group = page.getByTestId('radio-readonly');
  await group.getByRole('radio', { name: 'Readonly and checked' }).focus();
  await page.keyboard.press('ArrowUp');
  await expect(group.getByRole('radio', { name: 'Readonly', exact: true })).toBeFocused();
  await expect(group.getByRole('radio', { name: 'Readonly and checked' })).toBeChecked();
  await group.getByText('Readonly', { exact: true }).click();
  await expect(group.getByRole('radio', { name: 'Readonly and checked' })).toBeChecked();
});
