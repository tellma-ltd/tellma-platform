// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-badge and tm-counter: the axe floor in both themes
 * and directions, an inline count stepping from 9 to 10 without moving
 * anything, the overlay counter on its button's corner, and the counter
 * tone under forced colours.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`badge story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('badge', { theme, dir }));
        await expect(page.getByTestId('badge-error')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('inline counter', () => {
  test('9 to 10 moves nothing: the counter and the text after it keep their boxes', async ({
    page,
  }) => {
    await page.goto(storyUrl('badge'));
    const counter = page.getByTestId('counter-inline');
    const row = page.getByTestId('inline-row');
    await expect(counter).toHaveText('9');
    const before = (await counter.boundingBox())!;
    const rowBefore = (await row.boundingBox())!;

    await page.getByTestId('increment').click();
    await expect(counter).toHaveText('10');
    const after = (await counter.boundingBox())!;
    const rowAfter = (await row.boundingBox())!;
    expect(after).toEqual(before);
    expect(rowAfter).toEqual(rowBefore);
  });

  test('caps at max and reads left to right in RTL', async ({ page }) => {
    await page.goto(storyUrl('badge', { dir: 'rtl' }));
    await expect(page.getByTestId('counter-capped')).toHaveText('99+');
    await expect(page.getByTestId('counter-custom-max')).toHaveText('999+');
    await expect(page.getByTestId('counter-capped')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByTestId('counter-zero')).toHaveText('0');
    await expect(page.getByTestId('counter-hidden-zero')).toBeHidden();
    await expect(page.getByTestId('counter-null')).toBeHidden();
  });
});

test.describe('overlay counter', () => {
  test('sits on the button corner, hidden from assistive technology, displacing nothing', async ({
    page,
  }) => {
    await page.goto(storyUrl('badge'));
    const button = page.getByTestId('overlay-button');
    const counter = page.getByTestId('counter-overlay');
    await expect(counter).toHaveAttribute('aria-hidden', 'true');
    const buttonBox = (await button.boundingBox())!;
    const counterBox = (await counter.boundingBox())!;
    expect(counterBox.height).toBe(16);
    // Block-start, inline-end corner: it overhangs the top and the right.
    expect(counterBox.y).toBeLessThan(buttonBox.y);
    expect(counterBox.x + counterBox.width).toBeGreaterThan(buttonBox.x + buttonBox.width);
    // The icon stays centred: the counter takes no space in the button.
    const icon = (await button.locator('tm-icon').boundingBox())!;
    const iconCentre = icon.x + icon.width / 2;
    expect(Math.abs(iconCentre - (buttonBox.x + buttonBox.width / 2))).toBeLessThan(1);
  });
});

test.describe('forced colours', () => {
  test('the attention tone keeps a system highlight fill, the others a border', async ({
    page,
  }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('badge'));
    const paint = (testId: string) =>
      page.getByTestId(testId).evaluate((el) => {
        const style = getComputedStyle(el);
        return { fill: style.backgroundColor, edge: style.borderTopColor };
      });
    const attention = await paint('counter-attention');
    const neutral = await paint('counter-capped');
    // Every pill keeps a visible edge; attention alone keeps a fill.
    expect(neutral.edge).not.toBe('rgba(0, 0, 0, 0)');
    expect(attention.fill).not.toBe('rgba(0, 0, 0, 0)');
    expect(attention.fill).not.toBe(neutral.fill);
  });
});
