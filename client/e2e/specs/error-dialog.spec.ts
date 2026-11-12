// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for TmErrorDialog: the axe floor in both themes and
 * directions with the Details open, the pending footer button keeping its
 * width and taking its pending name, in-place replacement, the item cap,
 * and Escape answering 'close' while a run is pending.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`an open error dialog is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('error-dialog', { theme, dir }));
        await page.getByTestId('open-server').click();
        const dialog = page.getByRole('alertdialog');
        await dialog.getByRole('button', { name: 'Details' }).click();
        await expect(dialog.getByText('UI version')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the pending button keeps its width and takes its pending name', async ({ page }) => {
  await page.goto(storyUrl('error-dialog'));
  await page.getByTestId('open-server').click();
  const dialog = page.getByRole('alertdialog', { name: 'Something went wrong' });
  const reload = dialog.getByRole('button', { name: 'Reload' });
  await expect(reload).toBeFocused();
  const footer = dialog.locator('[tmModalFooter]');
  const idle = (await reload.boundingBox())!;
  const closeBefore = (await footer.getByRole('button', { name: 'Close' }).boundingBox())!;

  await reload.click();
  const pending = dialog.getByRole('button', { name: 'Reloading the application' });
  await expect(pending).toHaveAttribute('aria-busy', 'true');
  const busy = (await pending.boundingBox())!;
  expect(busy.width).toBe(idle.width);
  expect(busy.x).toBe(idle.x);
  // Nothing else in the footer moved, and Close stays live.
  expect((await footer.getByRole('button', { name: 'Close' }).boundingBox())!.x).toBe(
    closeBefore.x,
  );

  // The run fails: the button returns to idle, the same width.
  await expect(reload).not.toHaveAttribute('aria-busy', 'true', { timeout: 5000 });
  expect((await reload.boundingBox())!.width).toBe(idle.width);
  await page.keyboard.press('Escape');
  await expect(page.getByTestId('error-outcome')).toHaveText('Outcome: close');
});

test('Escape closes while a run is pending; the late result is ignored', async ({ page }) => {
  await page.goto(storyUrl('error-dialog'));
  await page.getByTestId('open-server').click();
  const dialog = page.getByRole('alertdialog');
  await dialog.getByRole('button', { name: 'Reload' }).click();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  await expect(page.getByTestId('error-outcome')).toHaveText('Outcome: close');
});

test('a second show replaces the dialog in place and caps its items', async ({ page }) => {
  await page.goto(storyUrl('error-dialog'));
  await page.getByTestId('open-server').click();
  const dialog = page.getByRole('alertdialog');
  await dialog.getByRole('button', { name: 'Details' }).click();
  await dialog.getByRole('button', { name: 'Show another error' }).click();

  await expect(page.getByRole('alertdialog')).toHaveCount(1);
  await expect(page.getByRole('alertdialog', { name: 'The document was not saved' })).toBeVisible();
  await expect(page.getByTestId('error-outcome')).toHaveText('Outcome: replaced');
  await expect(dialog.locator('.tm-error-dialog__items > li')).toHaveCount(10);
  await expect(dialog.getByText('and 3 more')).toBeVisible();
  // The focused action left with the old content: focus is back on the footer.
  await expect(dialog.getByRole('button', { name: 'Go to the first line' })).toBeFocused();

  await dialog.getByRole('button', { name: 'Go to the first line' }).click();
  await expect(page.getByTestId('error-outcome')).toHaveText('Outcome: replaced, secondary');
});

test('the backdrop does not dismiss', async ({ page }) => {
  await page.goto(storyUrl('error-dialog'));
  await page.getByTestId('open-validation').click();
  await expect(page.getByRole('alertdialog')).toBeVisible();
  await page.mouse.click(5, 5);
  await expect(page.getByRole('alertdialog')).toBeVisible();
  await page
    .getByRole('alertdialog')
    .locator('[tmModalFooter]')
    .getByRole('button', { name: 'Close' })
    .click();
  await expect(page.getByTestId('error-outcome')).toHaveText('Outcome: close');
});
