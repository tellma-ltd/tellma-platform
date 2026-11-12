// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for TmConfirm: the axe floor in both themes and
 * directions with the dialog open, the alert dialog's name and
 * description, the initial focus, and each answer: a choice, Escape and
 * the backdrop.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`an open confirm is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('confirm', { theme, dir }));
        await page.getByTestId('open-delete').click();
        await expect(page.getByRole('alertdialog')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the leave guard is a named, described alert dialog focused on its primary answer', async ({
  page,
}) => {
  await page.goto(storyUrl('confirm'));
  await page.getByTestId('open-leave').click();
  const dialog = page.getByRole('alertdialog', { name: 'Leave without saving?' });
  await expect(dialog).toBeVisible();
  await expect(dialog).toHaveAccessibleDescription('This center has unsaved changes.');
  await expect(dialog.getByRole('button', { name: 'Stay' })).toBeFocused();
  await dialog.getByRole('button', { name: 'Discard changes' }).click();
  await expect(page.getByTestId('confirm-answer')).toHaveText('Answer: discard');
});

test('a destructive confirm starts on Cancel, so Enter cancels', async ({ page }) => {
  await page.goto(storyUrl('confirm'));
  await page.getByTestId('open-delete').click();
  const dialog = page.getByRole('alertdialog', { name: 'Delete this center?' });
  await expect(dialog.getByRole('button', { name: 'Cancel' })).toBeFocused();
  await expect(dialog.locator('.tm-confirm__icon')).toBeVisible();
  await page.keyboard.press('Enter');
  await expect(dialog).toBeHidden();
  await expect(page.getByTestId('confirm-answer')).toHaveText('Answer: false');
});

test('Escape and the backdrop answer null; OK answers true', async ({ page }) => {
  await page.goto(storyUrl('confirm'));
  const answer = page.getByTestId('confirm-answer');

  await page.getByTestId('open-leave').click();
  await page.keyboard.press('Escape');
  await expect(answer).toHaveText('Answer: null');

  await page.getByTestId('open-leave').click();
  await expect(page.getByRole('alertdialog')).toBeVisible();
  await page.mouse.click(5, 5);
  await expect(page.getByRole('alertdialog')).toBeHidden();
  await expect(answer).toHaveText('Answer: null');

  await page.getByTestId('open-post').click();
  const dialog = page.getByRole('alertdialog', { name: 'Post the document?' });
  await expect(dialog.getByRole('button', { name: 'OK' })).toBeFocused();
  await dialog.getByRole('button', { name: 'OK' }).click();
  await expect(answer).toHaveText('Answer: true');
});
