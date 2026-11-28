// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-progress-bar and tm-progress-ring: the axe floor
 * in both themes and directions, fills from inline-start in both
 * directions, the minimum display time, the sweep and the spin static
 * under reduced motion, and the fill visible under forced colours.
 */

/** Where Playwright's fake clock starts. */
const CLOCK_START = new Date('2026-01-05T09:00:00Z');

/** The instant the fake clock jumps to and pauses at, well past any setup. */
const CLOCK_PAUSE = new Date('2026-01-05T09:10:00Z');

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`progress story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('progress', { theme, dir }));
        await expect(page.getByTestId('bar-40')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('bar', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`fills from inline-start (${dir})`, async ({ page }) => {
      await page.goto(storyUrl('progress', { dir }));
      const bar = page.getByTestId('bar-40');
      await expect(bar).toHaveAttribute('role', 'progressbar');
      await expect(bar).toHaveAttribute('aria-valuenow', '40');
      const track = (await bar.boundingBox())!;
      const fill = (await bar.locator('.tm-progress-bar__fill').boundingBox())!;
      expect(track.height).toBe(4);
      expect(Math.round((fill.width / track.width) * 100)).toBe(40);
      if (dir === 'ltr') {
        expect(fill.x).toBe(track.x);
      } else {
        expect(fill.x + fill.width).toBeCloseTo(track.x + track.width, 0);
      }
    });
  }

  test('stays on screen for its minimum time when the task ends at once', async ({ page }) => {
    await page.clock.install({ time: CLOCK_START });
    await page.goto(storyUrl('progress'));
    const held = page.getByTestId('held');
    const toggle = page.getByTestId('toggle-active');
    await expect(held).toBeHidden();

    // Page time stands still from here, so the real time two clicks take
    // on a loaded runner never eats into the hold; renders happen as the
    // test advances it.
    await page.clock.pauseAt(CLOCK_PAUSE);
    await toggle.click();
    await page.clock.runFor(20);
    await expect(held).toBeVisible();
    await toggle.click();
    await page.clock.runFor(100);
    await expect(held).toBeVisible();
    await expect(held).toHaveAttribute('role', 'progressbar');
    await page.clock.runFor(250);
    await expect(held).toBeHidden();
  });
});

test.describe('reduced motion', () => {
  test('the sweep and the ring are static', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('progress'));
    const sweep = await page
      .getByTestId('sweep')
      .locator('.tm-progress-bar__fill')
      .evaluate((el) => getComputedStyle(el).animationName);
    const spin = await page
      .getByTestId('ring-spin')
      .locator('.tm-progress-ring__arc')
      .evaluate((el) => getComputedStyle(el).animationName);
    expect(sweep).toBe('none');
    expect(spin).toBe('none');
  });

  test('without the preference, both move', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'no-preference' });
    await page.goto(storyUrl('progress'));
    const sweep = await page
      .getByTestId('sweep')
      .locator('.tm-progress-bar__fill')
      .evaluate((el) => getComputedStyle(el).animationName);
    const spin = await page
      .getByTestId('ring-spin')
      .locator('.tm-progress-ring__arc')
      .evaluate((el) => getComputedStyle(el).animationName);
    expect(sweep).not.toBe('none');
    expect(spin).not.toBe('none');
  });
});

test.describe('forced colours', () => {
  test('the fill stays distinct from the track', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('progress'));
    const colours = await page.getByTestId('bar-40').evaluate((el) => ({
      track: getComputedStyle(el.querySelector('.tm-progress-bar__track')!).backgroundColor,
      fill: getComputedStyle(el.querySelector('.tm-progress-bar__fill')!).backgroundColor,
    }));
    expect(colours.fill).not.toBe(colours.track);
  });
});
