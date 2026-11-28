// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Touch battery for tm-splitter (touch project): a finger dragging the bar
 * resizes the panes instead of scrolling the page. The vertical splitter
 * is the one a phone's narrow viewport leaves room to move. Runs on the
 * coarse-pointer device project only.
 */

test('a touch drag on the bar resizes the panes', async ({ page }) => {
  await page.goto(storyUrl('splitter'));
  const separator = page.getByTestId('splitter-vertical').getByRole('separator');
  await separator.scrollIntoViewIfNeeded();
  const box = (await separator.boundingBox())!;
  const x = Math.round(box.x + box.width / 2);
  const y = Math.round(box.y + box.height / 2);
  const scrollBefore = await page.evaluate(() => [window.scrollX, window.scrollY]);

  const client = await page.context().newCDPSession(page);
  try {
    await client.send('Input.dispatchTouchEvent', {
      type: 'touchStart',
      touchPoints: [{ x, y, id: 1 }],
    });
    for (const step of [-10, -20, -30, -40, -50]) {
      await client.send('Input.dispatchTouchEvent', {
        type: 'touchMove',
        touchPoints: [{ x, y: y + step, id: 1 }],
      });
    }
    await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  } finally {
    await client.detach();
  }

  // Up 50 px: the preview, sized from the bottom, grows from 120 to 170.
  await expect(page.getByTestId('preview-size')).toHaveText('Preview: 170');
  expect((await page.getByTestId('pane-preview').boundingBox())!.height).toBe(170);
  // The drag never panned the page.
  expect(await page.evaluate(() => [window.scrollX, window.scrollY])).toEqual(scrollBefore);
});
