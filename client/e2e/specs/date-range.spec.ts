// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-date-range: the axe floor in both themes and
 * directions, typed ends written to the bound value, and the order error
 * shown on the "to" field.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`date-range story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('date-range', { theme, dir }));
        await expect(
          page
            .getByTestId('range-reversed')
            .locator('tm-form-field')
            .nth(1)
            .locator('.tm-form-field__error'),
        ).toHaveText('The end date must be on or after the start date');
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('typed ends write the range, and an out-of-order range errs on "to"', async ({ page }) => {
  await page.goto(storyUrl('date-range'));
  const range = page.getByTestId('range-period');
  const from = range.getByRole('combobox', { name: 'From' });
  const to = range.getByRole('combobox', { name: 'To' });
  await from.fill('3/10/2026');
  await to.fill('3/31/2026');
  await expect(page.getByTestId('model-json')).toContainText(
    '"period":{"from":"2026-03-10","to":"2026-03-31"}',
  );
  await to.fill('3/1/2026');
  await to.blur();
  const toField = range.locator('tm-form-field').nth(1);
  await expect(toField.locator('.tm-form-field__error')).toHaveText(
    'The end date must be on or after the start date',
  );
  await expect(to).toHaveAttribute('aria-invalid', 'true');
  await expect(from).not.toHaveAttribute('aria-invalid', 'true');
});
