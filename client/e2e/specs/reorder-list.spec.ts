// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-reorder-list: the axe floor in both themes and
 * directions, the handle's grab model with each step announced and focus
 * kept on the moving item, Escape restoring the order, Alt+Arrow moves, and
 * a pointer drag landing on the insertion line.
 */

/** The live region the list announces through. */
function liveRegion(page: Page) {
  return page.locator('.cdk-live-announcer-element');
}

/** A handle of the first list by its row's label. */
function handle(page: Page, label: string) {
  return page.getByTestId('reorder-columns').getByRole('button', { name: `Reorder ${label}` });
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`reorder-list story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('reorder-list', { theme, dir }));
        await expect(handle(page, 'Code')).toBeVisible();
        await expectNoAxeViolations(page);
        // Picked up, the grabbed row's look is checked too.
        await handle(page, 'Code').focus();
        await page.keyboard.press('Space');
        await expect(liveRegion(page)).toHaveText('Code picked up. Position 2 of 5.');
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the list and its handles are named and described', async ({ page }) => {
  await page.goto(storyUrl('reorder-list'));
  await expect(page.getByRole('list', { name: 'Shown columns' })).toBeVisible();
  await expect(handle(page, 'Code')).toHaveAccessibleDescription(
    'Press Space to pick up. Use the arrow keys to move, Space to drop, and Escape to cancel.',
  );
});

test('a keyboard move announces each step and keeps focus on the item', async ({ page }) => {
  await page.goto(storyUrl('reorder-list'));
  await handle(page, 'Code').focus();
  await page.keyboard.press('Space');
  await expect(liveRegion(page)).toHaveText('Code picked up. Position 2 of 5.');
  await page.keyboard.press('ArrowDown');
  await expect(liveRegion(page)).toHaveText('Position 3 of 5');
  await expect(handle(page, 'Code')).toBeFocused();
  // Nothing is written until the drop.
  await expect(page.getByTestId('reorder-order')).toHaveText(
    'Order: Name, Code, Date, Amount, Status',
  );
  await page.keyboard.press('Space');
  await expect(liveRegion(page)).toHaveText('Code dropped at position 3 of 5.');
  await expect(page.getByTestId('reorder-order')).toHaveText(
    'Order: Name, Date, Code, Amount, Status',
  );
  await expect(page.getByTestId('reorder-last-move')).toHaveText('Last move: Code 2→3');
  await expect(handle(page, 'Code')).toBeFocused();
});

test('Escape cancels a move and puts the item back', async ({ page }) => {
  await page.goto(storyUrl('reorder-list'));
  await handle(page, 'Name').focus();
  await page.keyboard.press('Enter');
  await page.keyboard.press('End');
  await expect(liveRegion(page)).toHaveText('Position 5 of 5');
  await page.keyboard.press('Escape');
  await expect(liveRegion(page)).toHaveText('Move cancelled. Name is back at position 1.');
  await expect(handle(page, 'Name')).toBeFocused();
  await expect(page.getByTestId('reorder-order')).toHaveText(
    'Order: Name, Code, Date, Amount, Status',
  );
  await expect(page.getByTestId('reorder-last-move')).toHaveText('Last move: none');
});

test('Tab drops the item and moves on', async ({ page }) => {
  await page.goto(storyUrl('reorder-list'));
  await handle(page, 'Name').focus();
  await page.keyboard.press('Space');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Tab');
  await expect(page.getByTestId('reorder-order')).toHaveText(
    'Order: Code, Name, Date, Amount, Status',
  );
  await expect(handle(page, 'Name')).not.toBeFocused();
});

test('Alt+Up and Alt+Down move an item one place at once', async ({ page }) => {
  await page.goto(storyUrl('reorder-list'));
  await handle(page, 'Amount').focus();
  await page.keyboard.press('Alt+ArrowUp');
  await expect(liveRegion(page)).toHaveText('Amount dropped at position 3 of 5.');
  await expect(page.getByTestId('reorder-order')).toHaveText(
    'Order: Name, Code, Amount, Date, Status',
  );
  await expect(handle(page, 'Amount')).toBeFocused();
  await page.keyboard.press('Alt+ArrowDown');
  await expect(page.getByTestId('reorder-order')).toHaveText(
    'Order: Name, Code, Date, Amount, Status',
  );
});

test('a pointer drag lands a row on the insertion line', async ({ page }) => {
  await page.goto(storyUrl('reorder-list'));
  const list = page.getByTestId('reorder-columns');
  const rows = list.locator('.tm-reorder-list__row');
  const target = (await rows.nth(3).boundingBox())!;
  await rows.nth(0).dragTo(rows.nth(3), {
    targetPosition: { x: target.width / 2, y: target.height - 2 },
  });
  await expect(page.getByTestId('reorder-order')).toHaveText(
    'Order: Code, Date, Amount, Name, Status',
  );
  await expect(page.getByTestId('reorder-last-move')).toHaveText('Last move: Name 1→4');
});
