// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tmTooltip (DoD 16): hover-after-delay and
 * focus-visible show paths, WCAG 1.4.13 Escape dismiss + hoverable
 * surface, the always-available AriaDescriber description, the single-open
 * invariant, and the axe/reduced-motion gates.
 */

const PANEL = '.tm-tooltip__panel';

test.describe('show/hide paths (DoD 16)', () => {
  test('hover shows after the delay; moving away hides', async ({ page }) => {
    await page.goto(storyUrl('tooltip'));
    const host = page.getByTestId('tooltip-first');
    await host.hover();
    const panel = page.locator(PANEL);
    await expect(panel).toBeVisible(); // auto-wait spans the 500ms delay
    await expect(panel).toHaveText('Refresh the list');

    await page.mouse.move(10, 10);
    await expect(panel).toHaveCount(0);
  });

  test('keyboard focus shows immediately; blur hides; Escape dismisses', async ({ page }) => {
    await page.goto(storyUrl('tooltip'));
    await page.getByTestId('tooltip-first').focus();
    // Focus alone is not enough — the show path requires :focus-visible,
    // so drive it with a real Tab from the element before it.
    await page.keyboard.press('Shift+Tab');
    await page.keyboard.press('Tab');
    const panel = page.locator(PANEL);
    await expect(panel).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(panel).toHaveCount(0);

    await page.keyboard.press('Tab');
    await expect(panel).toBeVisible(); // next host's tooltip on focus
    await page.keyboard.press('Shift+Tab');
    await page.keyboard.press('Shift+Tab');
    await expect(panel).toHaveCount(0); // blur hid it
  });

  test('the surface is hoverable (1.4.13): crossing onto it keeps it shown', async ({ page }) => {
    await page.goto(storyUrl('tooltip'));
    const host = page.getByTestId('tooltip-first');
    await host.hover();
    const panel = page.locator(PANEL);
    await expect(panel).toBeVisible();

    const box = (await panel.boundingBox())!;
    // Travel from the host onto the tooltip surface in small steps.
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 8 });
    await page.waitForTimeout(250); // longer than the grace period
    await expect(panel).toBeVisible();

    await page.mouse.move(10, 10);
    await expect(panel).toHaveCount(0);
  });

  test('one tooltip at a time: hovering the second replaces the first', async ({ page }) => {
    await page.goto(storyUrl('tooltip'));
    await page.getByTestId('tooltip-first').hover();
    await expect(page.locator(PANEL)).toHaveText('Refresh the list');

    await page.getByTestId('tooltip-second').hover();
    await expect(page.locator(PANEL)).toHaveCount(1);
    await expect(page.locator(PANEL)).toHaveText('Post the selected documents');
  });

  test('the wrapper pattern gives disabled controls a working tooltip', async ({ page }) => {
    await page.goto(storyUrl('tooltip'));
    await page.getByTestId('tooltip-wrapper').hover();
    await expect(page.locator(PANEL)).toHaveText('Select at least one line first');
  });
});

test.describe('description without opening (DoD 16)', () => {
  test('AriaDescriber registers the text before any tooltip has shown', async ({ page }) => {
    await page.goto(storyUrl('tooltip'));
    const description = await page.getByTestId('tooltip-first').evaluate((el) => {
      const ids = el.getAttribute('aria-describedby');
      if (ids === null) {
        return null;
      }
      return ids
        .split(/\s+/)
        .map((id) => document.getElementById(id)?.textContent?.trim() ?? '')
        .join(' ')
        .trim();
    });
    expect(description).toBe('Refresh the list');
    await expect(page.locator(PANEL)).toHaveCount(0);
  });
});

test.describe('appearance gates', () => {
  test('tooltip-visible page is axe-clean (light + dark)', async ({ page }) => {
    for (const theme of ['light', 'dark'] as const) {
      await page.goto(storyUrl('tooltip', { theme }));
      await page.getByTestId('tooltip-first').hover();
      await expect(page.locator(PANEL)).toBeVisible();
      await expectNoAxeViolations(page);
    }
  });

  test('reduced motion removes the fade', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('tooltip'));
    await page.getByTestId('tooltip-first').hover();
    const panel = page.locator(PANEL);
    await expect(panel).toBeVisible();
    expect(await panel.evaluate((el) => getComputedStyle(el).animationName)).toBe('none');
  });
});
