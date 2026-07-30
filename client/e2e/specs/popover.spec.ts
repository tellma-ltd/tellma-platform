// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-popover (DoD 16): trigger semantics, lazy
 * content, focus in/out, Escape/outside-click/Tab-out close, logical
 * positioning with RTL mirroring and the viewport-edge flip, and the
 * axe/reduced-motion gates.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`open popover is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('popover', { theme }));
      await page.getByTestId('popover-trigger').click();
      await expect(page.getByTestId('popover-content')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('open/close + focus (DoD 16)', () => {
  test('click toggles; content is lazy; initial focus lands on the first tabbable', async ({
    page,
  }) => {
    await page.goto(storyUrl('popover'));
    const trigger = page.getByTestId('popover-trigger');
    await expect(trigger).toHaveAttribute('aria-haspopup', 'dialog');
    await expect(trigger).toHaveAttribute('aria-expanded', 'false');
    await expect(page.getByTestId('popover-content')).toHaveCount(0); // lazy

    await trigger.click();
    await expect(page.getByTestId('popover-content')).toBeVisible();
    await expect(trigger).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByTestId('popover-input')).toBeFocused();

    await trigger.click();
    await expect(page.getByTestId('popover-content')).toHaveCount(0); // destroyed
    await expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });

  test('Escape closes and restores focus to the trigger', async ({ page }) => {
    await page.goto(storyUrl('popover'));
    await page.getByTestId('popover-trigger').click();
    await expect(page.getByTestId('popover-input')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('popover-content')).toHaveCount(0);
    await expect(page.getByTestId('popover-trigger')).toBeFocused();
  });

  test('an outside click closes without stealing focus back', async ({ page }) => {
    await page.goto(storyUrl('popover'));
    await page.getByTestId('popover-trigger').click();
    await expect(page.getByTestId('popover-content')).toBeVisible();
    await page.getByTestId('after-trigger').click();
    await expect(page.getByTestId('popover-content')).toHaveCount(0);
    await expect(page.getByTestId('after-trigger')).toBeFocused();
  });

  test('tabbing past the content closes (non-modal) and focus continues', async ({ page }) => {
    await page.goto(storyUrl('popover'));
    await page.getByTestId('popover-trigger').click();
    await expect(page.getByTestId('popover-input')).toBeFocused();
    await page.keyboard.press('Tab'); // → Done button
    await page.keyboard.press('Tab'); // past the content → out of the panel
    await expect(page.getByTestId('popover-content')).toHaveCount(0);
    // Focus continued forward in document order, not back to the trigger.
    await expect(page.getByTestId('popover-trigger')).not.toBeFocused();
  });

  test('programmatic open anchors at a rectangle', async ({ page }) => {
    await page.goto(storyUrl('popover'));
    await page.getByTestId('open-at-rect').click();
    const content = page.getByTestId('rect-content');
    await expect(content).toBeVisible();
    const box = (await content.boundingBox())!;
    // Anchored near the synthetic rect (240, 160), not at the button.
    expect(Math.abs(box.x - 240)).toBeLessThan(120);
    expect(box.y).toBeGreaterThan(150);
  });
});

test.describe('positioning (DoD 16)', () => {
  test('block-end/start alignment mirrors under RTL', async ({ page }) => {
    await page.goto(storyUrl('popover'));
    const trigger = page.getByTestId('popover-trigger');
    await trigger.click();
    const panelLtr = page.locator('.tm-popover__panel');
    await expect(panelLtr).toBeVisible();
    const triggerBox = (await trigger.boundingBox())!;
    const panelBox = (await panelLtr.boundingBox())!;
    expect(Math.abs(panelBox.x - triggerBox.x)).toBeLessThan(2); // start = left in LTR
    expect(panelBox.y).toBeGreaterThanOrEqual(triggerBox.y + triggerBox.height - 1);

    await page.goto(storyUrl('popover', { dir: 'rtl' }));
    const rtlTrigger = page.getByTestId('popover-trigger');
    await rtlTrigger.click();
    const panelRtl = page.locator('.tm-popover__panel');
    await expect(panelRtl).toBeVisible();
    const rtlTriggerBox = (await rtlTrigger.boundingBox())!;
    const rtlPanelBox = (await panelRtl.boundingBox())!;
    // start = RIGHT edge in RTL.
    expect(
      Math.abs(rtlPanelBox.x + rtlPanelBox.width - (rtlTriggerBox.x + rtlTriggerBox.width)),
    ).toBeLessThan(2);
  });

  test('flips above when the preferred side has no room', async ({ page }) => {
    await page.goto(storyUrl('popover'));
    const trigger = page.getByTestId('edge-trigger');
    await trigger.click();
    const content = page.getByTestId('edge-content');
    await expect(content).toBeVisible();
    const triggerBox = (await trigger.boundingBox())!;
    const panelBox = (await page.locator('.tm-popover__panel').last().boundingBox())!;
    expect(panelBox.y + panelBox.height).toBeLessThanOrEqual(triggerBox.y + 1); // above
  });
});

test.describe('appearance gates', () => {
  test('reduced motion collapses the enter animation', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('popover'));
    await page.getByTestId('popover-trigger').click();
    const animation = await page
      .locator('.tm-popover__panel')
      .evaluate((el) => getComputedStyle(el).animationName);
    expect(animation).toBe('none');
  });
});
