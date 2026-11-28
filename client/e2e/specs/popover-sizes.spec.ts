// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { animationsSettled } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-popover's sizes: each fixed size holds its token
 * width while its content loads, a lg panel on a phone is full width less
 * the viewport margin on each side, and a sized panel is axe-clean in both
 * themes and directions.
 */

/** Opens the popover of one size and returns its settled panel. */
async function openSize(page: Page, size: 'auto' | 'sm' | 'md' | 'lg'): Promise<ReturnType<Page['locator']>> {
  await page.getByTestId(`size-trigger-${size}`).click();
  const panel = page.getByRole('dialog', { name: `Columns ${size}` });
  await expect(panel).toBeVisible();
  await animationsSettled(panel);
  return panel;
}

test.describe('fixed sizes', () => {
  for (const [size, width] of [
    ['sm', 320],
    ['md', 440],
    ['lg', 640],
  ] as const) {
    test(`a ${size} panel is ${width}px wide and keeps its geometry while content loads`, async ({
      page,
    }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(storyUrl('popover'));
      const panel = await openSize(page, size);
      const before = (await panel.boundingBox())!;
      expect(Math.abs(before.width - width)).toBeLessThanOrEqual(1);

      // More content lands while the panel is open: the long rows would
      // widen an 'auto' panel, but a fixed size never moves or resizes.
      await panel.getByTestId('sized-load').click();
      await expect(panel.getByTestId('sized-column')).toHaveCount(12);
      const after = (await panel.boundingBox())!;
      expect(after.width).toBe(before.width);
      expect(after.x).toBe(before.x);
      expect(after.y).toBe(before.y);
    });
  }

  test("an 'auto' panel takes its content's width, up to the cap", async ({ page }) => {
    await page.goto(storyUrl('popover'));
    const panel = await openSize(page, 'auto');
    const narrow = (await panel.boundingBox())!.width;
    await panel.getByTestId('sized-load').click();
    await expect(panel.getByTestId('sized-column')).toHaveCount(12);
    const wide = (await panel.boundingBox())!.width;
    expect(wide).toBeGreaterThan(narrow);
  });

  test('a lg panel on a phone is full width less the margin on each side', async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(storyUrl('popover'));
    const panel = await openSize(page, 'lg');
    const box = (await panel.boundingBox())!;
    const width = await page.evaluate(() => document.documentElement.clientWidth);
    expect(Math.abs(box.x - 8)).toBeLessThanOrEqual(1);
    expect(Math.abs(box.x + box.width - (width - 8))).toBeLessThanOrEqual(1);
  });

  test('a panel taller than the room below scrolls inside it', async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 500 });
    await page.goto(storyUrl('popover'));
    const panel = await openSize(page, 'md');
    // Load until the content is far taller than the viewport.
    for (let loads = 1; loads <= 4; loads += 1) {
      await panel.getByTestId('sized-load').click();
      await expect(panel.getByTestId('sized-column')).toHaveCount(12 * loads);
    }
    const box = (await panel.boundingBox())!;
    expect(box.y + box.height).toBeLessThanOrEqual(500 - 8 + 1);
    const scrolls = await panel.evaluate((element) => element.scrollHeight > element.clientHeight);
    expect(scrolls).toBe(true);
  });
});

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`sized popovers are axe-clean (${theme}, ${dir.toUpperCase()})`, async ({ page }) => {
        await page.goto(storyUrl('popover', { theme, dir }));
        const panel = await openSize(page, 'lg');
        await panel.getByTestId('sized-load').click();
        await expect(panel.getByTestId('sized-column')).toHaveCount(12);
        await expectNoAxeViolations(page);
      });
    }
  }
});
