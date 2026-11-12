// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for TmToasts: the axe floor in both themes and
 * directions, the corner and the narrow-viewport width, the six-second
 * timer and the sticky error, the pause while the pointer or focus is on
 * the stack, focus() and Escape returning focus, the cap of three behind a
 * pill, a keyed toast replaced in place, the action, the live regions, and
 * a toast shown above an open modal.
 */

/** Where Playwright's fake clock starts. */
const CLOCK_START = new Date('2026-01-05T09:00:00Z');

/** The instant the fake clock jumps to and pauses at, well past any setup. */
const CLOCK_PAUSE = new Date('2026-01-05T09:10:00Z');

/** The toast cards on show, newest first. */
function cards(page: Page) {
  return page.locator('.tm-toast-host .tm-toast');
}

/** Opens the story with page time standing still, so the test advances every timer. */
async function openPaused(page: Page): Promise<void> {
  await page.clock.install({ time: CLOCK_START });
  await page.goto(storyUrl('toast'));
  await page.clock.pauseAt(CLOCK_PAUSE);
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`toast story is axe-clean with every tone on show (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('toast', { theme, dir }));
        await page.getByTestId('show-success').click();
        await page.getByTestId('show-accent').click();
        await page.getByTestId('show-error').click();
        await expect(cards(page)).toHaveCount(3);
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('placement', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`sits in the block-start, inline-end corner and never displaces the page (${dir})`, async ({
      page,
    }) => {
      await page.goto(storyUrl('toast', { dir }));
      const trigger = page.getByTestId('show-neutral');
      const before = (await trigger.boundingBox())!;
      await trigger.click();
      const card = (await cards(page).first().boundingBox())!;
      const width = page.viewportSize()!.width;
      expect(card.width).toBe(360);
      expect(card.y).toBe(8);
      if (dir === 'ltr') {
        expect(card.x + card.width).toBeCloseTo(width - 8, 0);
      } else {
        expect(card.x).toBeCloseTo(8, 0);
      }
      expect(await trigger.boundingBox()).toEqual(before);
    });
  }

  test('takes the full width less the gutters below the sm breakpoint', async ({ page }) => {
    await page.setViewportSize({ width: 400, height: 700 });
    await page.goto(storyUrl('toast'));
    await page.getByTestId('show-neutral').click();
    const card = (await cards(page).first().boundingBox())!;
    expect(card.x).toBeCloseTo(8, 0);
    expect(card.width).toBeCloseTo(400 - 16, 0);
  });
});

test.describe('timers', () => {
  test('a toast closes after six seconds; an error stays until dismissed', async ({ page }) => {
    await openPaused(page);
    await page.getByTestId('show-neutral').click();
    await page.getByTestId('show-error').click();
    await page.clock.runFor(5900);
    await expect(cards(page)).toHaveCount(2);
    await page.clock.runFor(200);
    await expect(cards(page)).toHaveCount(1);
    await expect(page.getByTestId('toast-log')).toHaveText('Draft saved: timeout');

    await page.clock.runFor(60_000);
    await expect(cards(page)).toHaveCount(1);
    await cards(page).first().locator('.tm-toast__close').click();
    await page.clock.runFor(20);
    await expect(cards(page)).toHaveCount(0);
    await expect(page.getByTestId('toast-log')).toHaveText(
      'Draft saved: timeout, Could not save: dismiss',
    );
  });

  test('the pointer on the stack pauses every timer, and leaving resumes them', async ({ page }) => {
    await openPaused(page);
    await page.getByTestId('show-neutral').click();
    await page.clock.runFor(3000);
    await cards(page).first().hover();
    await page.clock.runFor(30_000);
    await expect(cards(page)).toHaveCount(1);

    // The timer kept the three seconds it had left.
    await page.mouse.move(5, 400);
    await page.clock.runFor(2900);
    await expect(cards(page)).toHaveCount(1);
    await page.clock.runFor(200);
    await expect(cards(page)).toHaveCount(0);
  });

  test('focus in the stack pauses the timers; Escape dismisses and focus goes back', async ({
    page,
  }) => {
    await openPaused(page);
    await page.getByTestId('show-success').click();
    await page.clock.runFor(20);
    const trigger = page.getByTestId('focus-toasts');
    await trigger.click();
    await page.clock.runFor(20);
    await expect(page.getByTestId('toast-focused')).toHaveText('focus(): true');
    await expect(cards(page).first().locator('button').first()).toBeFocused();
    await page.clock.runFor(30_000);
    await expect(cards(page)).toHaveCount(1);

    await page.keyboard.press('Escape');
    await page.clock.runFor(20);
    await expect(cards(page)).toHaveCount(0);
    await expect(trigger).toBeFocused();
    await expect(page.getByTestId('toast-log')).toHaveText('Invoice posted: dismiss');
  });

  test('focus() answers false with nothing on show', async ({ page }) => {
    await page.goto(storyUrl('toast'));
    await page.getByTestId('focus-toasts').click();
    await expect(page.getByTestId('toast-focused')).toHaveText('focus(): false');
    await expect(page.getByTestId('focus-toasts')).toBeFocused();
  });
});

test.describe('stack', () => {
  test('shows three, newest on top, and counts the rest on a pill', async ({ page }) => {
    await page.goto(storyUrl('toast'));
    await page.getByTestId('show-five').click();
    await expect(cards(page)).toHaveCount(3);
    await expect(cards(page).locator('.tm-toast__title')).toHaveText(['Fifth', 'Fourth', 'Third']);
    await expect(page.locator('.tm-toasts__pill')).toHaveText('2 more');

    // Closing one brings the next hidden toast on show.
    await cards(page).first().locator('.tm-toast__close').click();
    await expect(cards(page).locator('.tm-toast__title')).toHaveText(['Fourth', 'Third', 'Second']);
    await expect(page.locator('.tm-toasts__pill')).toHaveText('1 more');
  });

  test('a keyed toast replaces the last one with its key, in place', async ({ page }) => {
    await page.goto(storyUrl('toast'));
    await page.getByTestId('show-keyed').click();
    await page.getByTestId('show-neutral').click();
    await page.getByTestId('show-keyed').click();
    await expect(cards(page).locator('.tm-toast__title')).toHaveText(['Draft saved', 'Upload step 2']);
    await expect(page.getByTestId('toast-log')).toHaveText('Upload step 1: replaced');
  });

  test('the action runs and closes the toast', async ({ page }) => {
    await page.goto(storyUrl('toast'));
    await page.getByTestId('show-accent').click();
    await cards(page).first().locator('.tm-toast__action').click();
    await expect(cards(page)).toHaveCount(0);
    await expect(page.getByTestId('toast-log')).toHaveText(
      'Track ran, Started in the background.: action',
    );
  });

  test('clear() closes them all', async ({ page }) => {
    await page.goto(storyUrl('toast'));
    await page.getByTestId('show-five').click();
    await page.getByTestId('clear-toasts').click();
    await expect(cards(page)).toHaveCount(0);
    await expect(page.locator('.tm-toasts')).toBeHidden();
  });
});

test.describe('announcements', () => {
  test('a toast is read politely, an error assertively, a moment after it shows', async ({ page }) => {
    await openPaused(page);
    const polite = page.locator('.tm-toast-host [role="status"]');
    const assertive = page.locator('.tm-toast-host [role="alert"]');
    await page.getByTestId('show-success').click();
    await page.getByTestId('show-error').click();
    await page.clock.runFor(150);
    await expect(polite).toHaveText('Invoice posted. SAL-0042 is now final.');
    await expect(assertive).toHaveText('Could not save. The server did not answer.');
  });
});

test.describe('layering', () => {
  test('a toast shown while a modal is open sits above the modal and takes the pointer', async ({
    page,
  }) => {
    await page.goto(storyUrl('toast'));
    await page.getByTestId('open-modal').click();
    await expect(page.getByTestId('modal-body')).toBeVisible();
    const card = cards(page).first();
    await expect(card).toBeVisible();
    const box = (await card.boundingBox())!;
    const onTop = await page.evaluate(
      ({ x, y }) => document.elementFromPoint(x, y)?.closest('.tm-toast') !== null,
      { x: box.x + box.width / 2, y: box.y + box.height / 2 },
    );
    expect(onTop).toBe(true);
    await card.locator('.tm-toast__close').click();
    await expect(cards(page)).toHaveCount(0);
    await expect(page.getByTestId('modal-body')).toBeVisible();
  });

  test('focus() from inside a modal reaches the toast, and Escape closes the toast only', async ({
    page,
  }) => {
    await page.goto(storyUrl('toast'));
    await page.getByTestId('open-modal').click();
    const trigger = page.getByTestId('modal-focus-toasts');
    await trigger.click();
    await expect(cards(page).first().locator('.tm-toast__close')).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(cards(page)).toHaveCount(0);
    await expect(page.getByTestId('modal-body')).toBeVisible();
    await expect(trigger).toBeFocused();
  });
});
