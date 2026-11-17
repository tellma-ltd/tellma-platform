// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * A live English to Arabic switch, through the showcase's own language
 * control, while each directional control of the library additions is on
 * screen: the document turns right-to-left without a reload, and the arrow
 * keys remap at once, the forward arrow becoming ArrowLeft. A control that
 * cached the direction of its first render fails here.
 */

/** The page-side marker that survives only as long as the document does. */
interface MarkerWindow {
  /** Set before the switch; gone if the switch reloaded the page. */
  tmSameDocument?: boolean;
}

/** Switches the showcase to Arabic, and checks that the document flipped in place. */
async function switchToArabic(page: Page): Promise<void> {
  await page.evaluate(() => {
    (window as MarkerWindow).tmSameDocument = true;
  });
  await page.getByTestId('lang-ar').click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
  expect(
    await page.evaluate(() => (window as MarkerWindow).tmSameDocument === true),
    'the switch kept the document',
  ).toBe(true);
}

test('a radio group remaps its arrows', async ({ page }) => {
  await page.goto(storyUrl('radio'));
  const group = page.getByTestId('radio-horizontal');
  const radio = (name: string): Locator => group.getByRole('radio', { name });
  await radio('Draft').focus();
  await page.keyboard.press('ArrowRight');
  await expect(radio('Posted')).toBeChecked();

  await switchToArabic(page);
  await radio('Posted').focus();
  await page.keyboard.press('ArrowLeft');
  await expect(radio('Void')).toBeChecked();
  await page.keyboard.press('ArrowRight');
  await expect(radio('Posted')).toBeChecked();
});

test('a segmented control remaps its arrows', async ({ page }) => {
  await page.goto(storyUrl('segmented'));
  const track = page.getByTestId('seg-view');
  const segment = (name: string): Locator => track.getByRole('radio', { name });
  await segment('Grid').focus();
  await page.keyboard.press('ArrowRight');
  await expect(segment('Tiles')).toBeChecked();

  await switchToArabic(page);
  await segment('Tiles').focus();
  await page.keyboard.press('ArrowRight');
  await expect(segment('Grid')).toBeChecked();
  await page.keyboard.press('ArrowLeft');
  await expect(segment('Tiles')).toBeChecked();
});

test('a splitter remaps its arrows and speaks Arabic', async ({ page }) => {
  await page.goto(storyUrl('splitter'));
  const separator = page.getByTestId('splitter-horizontal').getByRole('separator');
  await separator.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('list-size')).toHaveText('List: 436');

  await switchToArabic(page);
  await expect(separator).toHaveAccessibleName('تغيير حجم اللوحتين');
  // Right-to-left, the list is on the right: ArrowRight shrinks it.
  await separator.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('list-size')).toHaveText('List: 420');
});

test('a tree remaps its arrows', async ({ page }) => {
  await page.goto(storyUrl('tree'));
  const agent = page
    .getByTestId('tree-fields')
    .getByRole('treeitem', { name: 'Agent', exact: true });
  await agent.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: Agent');

  await switchToArabic(page);
  await agent.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: none');
  await page.keyboard.press('ArrowLeft');
  await expect(page.getByTestId('tree-expanded')).toHaveText('Expanded: Agent');
});

test('tiles remap their arrows and speak Arabic', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const tile = (id: number): Locator =>
    page.getByTestId('tiles').locator(`.tm-tiles__item[data-index="${id - 1}"]`);
  await tile(1).focus();
  await page.keyboard.press('ArrowRight');
  await expect(tile(2)).toBeFocused();

  await switchToArabic(page);
  await tile(2).focus();
  await page.keyboard.press('ArrowLeft');
  await expect(tile(3)).toBeFocused();
  await expect(tile(3).locator('[role="checkbox"]')).toHaveAccessibleName('تحديد Customer 3');
  await page.keyboard.press('ArrowRight');
  await expect(tile(2)).toBeFocused();
});
