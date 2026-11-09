// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Touch battery for tmTooltip (DoD 16, touch project): a long-press shows
 * the tooltip and it persists until the next tap; a plain tap never shows
 * it, except on a button disabled with a reason, whose tap shows the
 * reason. Runs on the coarse-pointer device project only.
 */

const PANEL = '.tm-tooltip__panel';

/**
 * Presses a finger down and HOLDS it until `until` resolves (the 500ms
 * long-press timer fires while the finger is still on the glass — the
 * expectation doubles as the hold, no sleeps), then lifts it as a
 * CANCELLED tap, the way a real gesture recognizer ends a consumed
 * long-press (no synthesized click).
 */
async function touchHold(
  page: Page,
  point: { x: number; y: number },
  until: () => Promise<void>,
): Promise<void> {
  const client = await page.context().newCDPSession(page);
  try {
    await client.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: [{ x: point.x, y: point.y, id: 1 }],
    });
    await until();
    await client.send('Input.dispatchTouchEvent', { type: 'touchCancel', touchPoints: [] });
  } finally {
    await client.detach();
  }
}

test('a long-press shows the tooltip until the next tap; a plain tap does not show it', async ({
  page,
}) => {
  await page.goto(storyUrl('tooltip'));
  const host = page.getByTestId('tooltip-first');
  const panel = page.locator(PANEL);

  // A plain tap: no tooltip (long-press owns touch).
  await host.tap();
  await page.waitForTimeout(700);
  await expect(panel).toHaveCount(0);

  // Measured AFTER the tap's auto-scroll settled: the raw CDP dispatch
  // below never auto-scrolls, and a stale point past the visual viewport
  // would land on the document root instead of the host.
  await host.scrollIntoViewIfNeeded();
  const box = (await host.boundingBox())!;
  const center = { x: box.x + box.width / 2, y: box.y + box.height / 2 };

  // The hold crosses the 500ms long-press threshold while pressed.
  await touchHold(page, center, async () => {
    await expect(panel).toBeVisible();
  });
  await expect(panel).toHaveText('Refresh the list');

  // It persists (no hover to sustain it) until the next tap anywhere.
  await page.waitForTimeout(400);
  await expect(panel).toBeVisible();
  await page.touchscreen.tap(10, 10);
  await expect(panel).toHaveCount(0);
});

test('a tap on a button disabled with a reason shows the reason until the next tap', async ({
  page,
}) => {
  await page.goto(storyUrl('button'));
  const button = page.getByTestId('reason-btn');
  const panel = page.locator(PANEL);

  // The tap does nothing else: the activation is swallowed. Tapped at its
  // centre, because Playwright's tap() refuses an aria-disabled target.
  await button.scrollIntoViewIfNeeded();
  const box = (await button.boundingBox())!;
  await page.touchscreen.tap(box.x + box.width / 2, box.y + box.height / 2);
  await expect(panel).toHaveText('Needs the “Customers: Create and edit” permission.');
  await expect(page.getByTestId('reason-click-count')).toHaveText('0');

  await page.touchscreen.tap(10, 10);
  await expect(panel).toHaveCount(0);
});
