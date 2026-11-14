// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Touch battery for the compact form controls (touch project): every
 * segment and every switch row is a target of at least 24×24 px, and a tap
 * checks the segment or toggles the switch.
 */

/** Asserts each element's box is at least 24 px on both axes. */
async function expectTargets(targets: Locator, what: string): Promise<void> {
  const boxes = await targets.evaluateAll((elements) =>
    elements.map((element) => {
      const rect = element.getBoundingClientRect();
      return { width: rect.width, height: rect.height };
    }),
  );
  expect(boxes.length, what).toBeGreaterThan(0);
  for (const [index, box] of boxes.entries()) {
    expect(box.width, `${what} ${index} width`).toBeGreaterThanOrEqual(24);
    expect(box.height, `${what} ${index} height`).toBeGreaterThanOrEqual(24);
  }
}

test('every segment is at least 24 px, and a tap checks it', async ({ page }) => {
  await page.goto(storyUrl('segmented'));
  const period = page.getByTestId('seg-period');
  await expect(period.getByRole('radio', { name: 'Week' })).toBeChecked();
  await expectTargets(page.locator('.tm-segmented__segment'), 'segment');
  await period.locator('.tm-segmented__segment').first().tap();
  await expect(period.getByRole('radio', { name: 'Day' })).toBeChecked();
});

test('every switch row is at least 24 px, a bare switch too, and a tap toggles it', async ({
  page,
}) => {
  await page.goto(storyUrl('switch'));
  await expect(page.getByTestId('switch-notify')).toBeVisible();
  await expectTargets(page.locator('.tm-switch__layout'), 'switch row');
  await expectTargets(page.getByTestId('switch-bare').getByRole('switch'), 'bare switch');
  // The native input is stretched over the padded track: a tap on the track lands on it.
  const notify = page.getByRole('switch', { name: 'Notify me by email' });
  await notify.tap();
  await expect(notify).toBeChecked();
});
