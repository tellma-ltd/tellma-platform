// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-card: the axe floor in both themes and
 * directions, the disclosure by keyboard, the error dot and the heading's
 * name collapsed and expanded, a preserved body that is hidden and out of
 * the tab order, and the subtitle whose first part gives way first.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`card story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('card', { theme, dir }));
        await expect(page.getByTestId('card-full')).toBeVisible();
        await page.getByTestId('toggle-invalid').click();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the disclosure answers Enter and Space', async ({ page }) => {
  await page.goto(storyUrl('card'));
  const toggle = page.getByRole('button', { name: 'Notes' });
  const body = page.getByTestId('card-collapsible').locator('.tm-card__body');
  await toggle.focus();
  await page.keyboard.press('Enter');
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await expect(body).toHaveCount(0);
  await page.keyboard.press('Space');
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  await expect(body).toBeVisible();
});

test('an invalid card shows the dot and names its heading, collapsed and expanded', async ({
  page,
}) => {
  await page.goto(storyUrl('card'));
  const card = page.getByTestId('card-invalid');
  const dot = card.locator('.tm-card__dot');
  const heading = card.locator('h3');
  const width = (await heading.boundingBox())!.width;
  await expect(dot).toBeHidden();

  await page.getByTestId('toggle-invalid').click();
  await expect(dot).toBeVisible();
  const toggle = page.getByRole('button', { name: 'Address, has errors' });
  await expect(toggle).toHaveAttribute('aria-expanded', 'true');
  // The dot's box was reserved: the heading kept its width.
  expect((await heading.boundingBox())!.width).toBe(width);

  await toggle.click();
  await expect(toggle).toHaveAttribute('aria-expanded', 'false');
  await expect(page.getByRole('heading', { level: 3, name: 'Address, has errors' })).toBeVisible();
  await expect(dot).toBeVisible();
});

test('a preserved body is hidden and out of the tab order while collapsed', async ({ page }) => {
  await page.goto(storyUrl('card'));
  const city = page.getByTestId('card-city');
  const toggle = page.getByRole('button', { name: 'Address' });
  await city.fill('Jeddah');
  await toggle.click();
  await expect(city).toBeHidden();
  await toggle.focus();
  await page.keyboard.press('Tab');
  await expect(city).not.toBeFocused();
  await toggle.click();
  // The field kept its value: it never left the DOM.
  await expect(city).toHaveValue('Jeddah');
});

test('the subtitle cuts its first part short and keeps the rest whole', async ({ page }) => {
  await page.goto(storyUrl('card'));
  const parts = page.getByTestId('card-full').locator('.tm-card__subtitle > * > span');
  const cut = await parts.evaluateAll((spans) =>
    spans.map((span) => span.scrollWidth > span.clientWidth),
  );
  expect(cut).toEqual([true, false]);
});
