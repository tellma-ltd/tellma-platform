// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-segmented: the axe floor in both themes and
 * directions, the arrows mapped by the live direction, labels dropped below
 * the container breakpoint (with the label kept as the name and tooltip),
 * and the active segment under forced colors.
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
      test(`segmented story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('segmented', { theme, dir }));
        await expect(
          page.getByTestId('seg-period').getByRole('radio', { name: 'Week' }),
        ).toBeChecked();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the arrows check and move in RTL, wrapping past a disabled segment', async ({ page }) => {
  await page.goto(storyUrl('segmented', { dir: 'rtl' }));
  const track = page.getByTestId('seg-period');
  await track.getByRole('radio', { name: 'Week' }).focus();
  await page.keyboard.press('ArrowLeft');
  await expect(track.getByRole('radio', { name: 'Month' })).toBeFocused();
  await expect(track.getByRole('radio', { name: 'Month' })).toBeChecked();
  // Year is disabled: the next enabled segment wraps to Day.
  await page.keyboard.press('ArrowLeft');
  await expect(track.getByRole('radio', { name: 'Day' })).toBeChecked();
  await expect(page.getByTestId('model-json')).toContainText('"period":"day"');
  await page.keyboard.press('ArrowRight');
  await expect(track.getByRole('radio', { name: 'Month' })).toBeChecked();
});

test('the arrows follow a live switch of direction with no reload', async ({ page }) => {
  await page.goto(storyUrl('segmented'));
  const track = page.getByTestId('seg-view');
  await track.getByRole('radio', { name: 'Grid' }).focus();
  await page.keyboard.press('ArrowRight');
  await expect(track.getByRole('radio', { name: 'Tiles' })).toBeChecked();
  await flipDocumentTo(page, 'rtl');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await page.keyboard.press('ArrowLeft');
  await expect(track.getByRole('radio', { name: 'List' })).toBeChecked();
  await expect(page.getByTestId('model-json')).toContainText('"view":"list"');
});

test('icon segments drop their labels below the container breakpoint, keeping the name', async ({
  page,
}) => {
  await page.goto(storyUrl('segmented'));
  const track = page.getByTestId('seg-view');
  const label = track.locator('.tm-segmented__label').first();
  await expect(label).toBeVisible();
  await page.getByTestId('container-narrow').click();
  await expect(label).toBeHidden();
  // The label stays the accessible name, and shows as the tooltip.
  const grid = track.getByRole('radio', { name: 'Grid' });
  await expect(grid).toBeVisible();
  await track.locator('.tm-segmented__segment').first().hover();
  await expect(page.locator('.tm-tooltip__panel')).toHaveText('Grid');
  await page.getByTestId('container-wide').click();
  await expect(label).toBeVisible();
});

test('the active segment keeps a system fill under forced colors', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await page.goto(storyUrl('segmented'));
  const segments = page.getByTestId('seg-period').locator('.tm-segmented__segment');
  await expect(segments.nth(1)).toHaveClass(/tm-segmented__segment--checked/);
  const paint = (index: number) =>
    segments.nth(index).evaluate((element) => {
      const style = getComputedStyle(element);
      return { background: style.backgroundColor, text: style.color };
    });
  const checked = await paint(1);
  const unchecked = await paint(0);
  expect(checked.background).not.toBe(unchecked.background);
  // The system highlight pair: the label stays legible on the fill.
  expect(checked.text).not.toBe(checked.background);
});
