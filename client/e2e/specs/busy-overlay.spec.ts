// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for TmBusyOverlay: the axe floor in both themes and
 * directions, idle and with the scrim up; the pointer and the keyboard held
 * from the click's own frame with the progress cursor; the scrim at 200 ms,
 * never for fast work, at once with `immediate`; nested runs' labels; and
 * focus returned to the trigger when the work settles.
 */

/** Where Playwright's fake clock starts. */
const CLOCK_START = new Date('2026-01-05T09:00:00Z');

/** The instant the fake clock jumps to and pauses at, well past any setup. */
const CLOCK_PAUSE = new Date('2026-01-05T09:10:00Z');

/** The overlay host. */
function overlay(page: Page) {
  return page.locator('.tm-busy-overlay');
}

/** The overlay's status text. */
function label(page: Page) {
  return overlay(page).locator('[role="status"]');
}

/** Opens the story with page time standing still, so the test advances every timer. */
async function openPaused(page: Page): Promise<void> {
  await page.clock.install({ time: CLOCK_START });
  await page.goto(storyUrl('busy-overlay'));
  await page.clock.pauseAt(CLOCK_PAUSE);
  await page.clock.runFor(20);
}

/** Clicks the centre of an element with the raw mouse, without waiting for it to take the click. */
async function clickThrough(page: Page, testId: string): Promise<void> {
  const box = (await page.getByTestId(testId).boundingBox())!;
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`busy-overlay story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('busy-overlay', { theme, dir }));
        await expect(page.getByTestId('save')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }

  test('the scrim, spinner and label are axe-clean', async ({ page }) => {
    // Real time here: the scan itself runs on timers, so the long import
    // keeps the scrim up for as long as the scan takes.
    await page.goto(storyUrl('busy-overlay', { theme: 'dark' }));
    await page.getByTestId('import').click();
    await expect(overlay(page)).toHaveClass(/tm-busy-overlay--scrim/);
    await expect(label(page)).toHaveText('Importing…');
    await expectNoAxeViolations(page);
  });
});

test.describe('blocking', () => {
  test('holds the pointer and the keys from the click, with the progress cursor', async ({ page }) => {
    await openPaused(page);
    const save = page.getByTestId('save');
    await save.click();
    await page.clock.runFor(20);
    await expect(save).toHaveClass(/tm-button--pending/);
    await expect(overlay(page)).toHaveClass(/tm-busy-overlay--active/);
    expect(await overlay(page).evaluate((host) => getComputedStyle(host).cursor)).toBe('progress');

    // The pointer lands on the overlay, not on the page.
    await clickThrough(page, 'counter');
    await clickThrough(page, 'notes');
    await page.keyboard.type('abc');
    await page.keyboard.press('Enter');
    await page.clock.runFor(20);
    await expect(page.getByTestId('counter')).toHaveText('Clicked 0 times');
    await expect(page.getByTestId('notes')).toHaveValue('');

    await page.clock.runFor(1000);
    await expect(page.getByTestId('busy-log')).toHaveText('saved after 1000 ms');
    await expect(overlay(page)).not.toHaveClass(/tm-busy-overlay--active/);
    await clickThrough(page, 'counter');
    await page.clock.runFor(20);
    await expect(page.getByTestId('counter')).toHaveText('Clicked 1 times');
  });

  test('returns focus to the trigger when the work settles', async ({ page }) => {
    await openPaused(page);
    const save = page.getByTestId('save');
    await save.focus();
    await page.keyboard.press('Enter');
    await page.clock.runFor(20);
    await expect(save).not.toBeFocused();
    await page.clock.runFor(1000);
    await expect(page.getByTestId('busy-log')).toHaveText('saved after 1000 ms');
    await expect(save).toBeFocused();
  });
});

test.describe('scrim', () => {
  test('shows the scrim, spinner and label at 200 ms', async ({ page }) => {
    await openPaused(page);
    await page.getByTestId('save').click();
    await page.clock.runFor(150);
    await expect(overlay(page)).not.toHaveClass(/tm-busy-overlay--scrim/);
    await expect(label(page)).toHaveText('');
    await page.clock.runFor(60);
    await expect(overlay(page)).toHaveClass(/tm-busy-overlay--scrim/);
    await expect(overlay(page).locator('tm-spinner')).toBeVisible();
    await expect(label(page)).toHaveText('Saving…');
  });

  test('never shows for work that settles first', async ({ page }) => {
    await openPaused(page);
    await page.getByTestId('save-fast').click();
    await page.clock.runFor(150);
    await expect(page.getByTestId('busy-log')).toHaveText('saved after 100 ms');
    await page.clock.runFor(200);
    await expect(overlay(page)).not.toHaveClass(/tm-busy-overlay--scrim/);
    await expect(label(page)).toHaveText('');
  });

  test('shows at once with immediate', async ({ page }) => {
    await openPaused(page);
    await page.getByTestId('sign-out').click();
    await page.clock.runFor(20);
    await expect(overlay(page)).toHaveClass(/tm-busy-overlay--scrim/);
    await expect(label(page)).toHaveText('Signing out…');
    await page.clock.runFor(1000);
    await expect(page.getByTestId('busy-log')).toHaveText('signed out');
    await expect(overlay(page)).not.toHaveClass(/tm-busy-overlay--scrim/);
  });

  test('nested runs show the latest label, and the outer label as it changes', async ({ page }) => {
    await openPaused(page);
    await page.getByTestId('nested').click();
    await page.clock.runFor(250);
    await expect(label(page)).toHaveText('Finishing uploads…');
    await page.clock.runFor(400);
    await expect(label(page)).toHaveText('Posting…');
    await page.clock.runFor(600);
    await expect(page.getByTestId('busy-log')).toHaveText('nested done');
    await expect(overlay(page)).not.toHaveClass(/tm-busy-overlay--active/);
  });
});
