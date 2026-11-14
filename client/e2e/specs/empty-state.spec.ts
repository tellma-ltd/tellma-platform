// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-empty-state: the axe floor in both themes and
 * directions (with the announced state up), the anatomy's geometry, the
 * page size centred in its frame, and the polite announcement when the
 * state replaces a list's rows.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`empty-state story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('empty-state', { theme, dir }));
        await page.getByTestId('toggle-search').click();
        await expect(page.getByTestId('no-matches')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('anatomy', () => {
  test('a 22 px icon, then the heading, the detail and the actions, centred', async ({ page }) => {
    await page.goto(storyUrl('empty-state'));
    const state = page.getByTestId('page');
    const icon = (await state.locator('.tm-empty-state__icon').boundingBox())!;
    const heading = (await state.locator('.tm-empty-state__heading').boundingBox())!;
    const detail = (await state.locator('.tm-empty-state__detail').boundingBox())!;
    const actions = (await state.locator('.tm-empty-state__actions').boundingBox())!;
    expect(icon.width).toBe(22);
    expect(icon.y).toBeLessThan(heading.y);
    expect(heading.y).toBeLessThan(detail.y);
    expect(detail.y).toBeLessThan(actions.y);
    const fontSize = await state
      .locator('.tm-empty-state__heading')
      .evaluate((el) => getComputedStyle(el).fontSize);
    expect(fontSize).toBe('14px');

    // The page size fills its frame's 280 px content box and centres the
    // column in it.
    const frame = (await state.boundingBox())!;
    expect(frame.height).toBe(280);
    const column = (await state.locator('.tm-empty-state__body').boundingBox())!;
    const above = column.y - frame.y;
    const below = frame.y + frame.height - (column.y + column.height);
    expect(Math.abs(above - below)).toBeLessThan(1.5);
  });

  test('announces the state politely when it replaces the rows', async ({ page }) => {
    await page.goto(storyUrl('empty-state'));
    await expect(page.getByTestId('rows')).toBeVisible();
    await page.getByTestId('toggle-search').click();
    const region = page.getByTestId('no-matches').locator('[role="status"]');
    await expect(region).toContainText('No matches');
    await expect(region).toContainText('Try fewer or different words.');
    // A state present from the start is silent.
    await expect(page.getByTestId('page').locator('[role="status"]')).toHaveCount(0);
  });

  test('the error tone tints the icon only', async ({ page }) => {
    await page.goto(storyUrl('empty-state'));
    const colours = await page.getByTestId('error').evaluate((el) => ({
      icon: getComputedStyle(el.querySelector('.tm-empty-state__icon')!).color,
      heading: getComputedStyle(el.querySelector('.tm-empty-state__heading')!).color,
    }));
    expect(colours.icon).not.toBe(colours.heading);
    await expect(page.getByTestId('error').locator('tm-reference')).toBeVisible();
  });
});
