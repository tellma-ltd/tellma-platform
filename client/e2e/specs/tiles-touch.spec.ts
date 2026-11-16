// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Touch battery for tm-tiles (touch project): a tap on a tile opens it, and
 * a tap on its checkbox selects it without opening it. A phone's width
 * stacks the tiles as a divided list. Runs on the coarse-pointer device
 * project only.
 */

test('a tap opens a tile and a tap on its checkbox selects it', async ({ page }) => {
  await page.goto(storyUrl('tiles'));
  const tiles = page.getByTestId('tiles');
  await expect(tiles).toHaveAttribute('data-layout', 'list');
  const tile = (id: number) => tiles.locator(`.tm-tiles__item[data-index="${id - 1}"]`);

  await tile(2).locator('.tm-tiles__corner--bottom-start').tap();
  await expect(page.getByTestId('activated')).toHaveText('2 (pointer)');

  const checkbox = tile(3).locator('[role="checkbox"]');
  const box = (await checkbox.boundingBox())!;
  // The checkbox is a full 24 px target.
  expect(Math.min(box.width, box.height)).toBeGreaterThanOrEqual(24);
  await checkbox.tap();
  await expect(page.getByTestId('selected')).toHaveText('1');
  await expect(tile(3)).toHaveAttribute('aria-selected', 'true');
  await expect(page.getByTestId('activated')).toHaveText('2 (pointer)');
});
