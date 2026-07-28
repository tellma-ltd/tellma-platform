// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Touch battery for tmTooltip (DoD 16, touch project): a long-press shows
 * the tooltip and it persists until the next tap; a plain tap never shows
 * it. Runs on the coarse-pointer device project only.
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
  const box = (await host.boundingBox())!;
  const center = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
  const panel = page.locator(PANEL);

  // A plain tap: no tooltip (long-press owns touch).
  await host.tap();
  await page.waitForTimeout(700);
  await expect(panel).toHaveCount(0);

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
