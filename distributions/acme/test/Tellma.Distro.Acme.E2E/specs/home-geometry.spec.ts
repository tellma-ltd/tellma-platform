// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Locator } from '@playwright/test';
import { holdNetwork, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';

// The home's geometry (spec 0035 §15.1, §15.3): a cold entry holds the
// route's home skeleton, two greeting lines over three tile frames, and
// the page that replaces it puts the greeting and each tile in the box its
// frame held, in the wide layout and in the stacked one below 768 px.

/** A box on the page, rounded to whole CSS pixels. */
interface AcmeBox {
  /** The distance from the viewport's left. */
  readonly x: number;
  /** The distance from the viewport's top. */
  readonly y: number;
  /** The width. */
  readonly width: number;
  /** The height. */
  readonly height: number;
}

/**
 * The boxes of every element a locator matches, in DOM order.
 *
 * @param locator - The elements.
 * @returns Their boxes, rounded to whole pixels.
 */
async function boxes(locator: Locator): Promise<AcmeBox[]> {
  return locator.evaluateAll((elements) =>
    elements.map((element) => {
      const rect = element.getBoundingClientRect();
      return {
        x: Math.round(rect.x),
        y: Math.round(rect.y),
        width: Math.round(rect.width),
        height: Math.round(rect.height),
      };
    }),
  );
}

test.describe('the home geometry', () => {
  for (const { layout, width, height } of [
    { layout: 'wide', width: 1440, height: 900 },
    { layout: 'stacked', width: 390, height: 844 },
  ]) {
    test(`a cold entry's home skeleton holds the greeting's and each tile's box (${layout}, ${width} px)`, async ({
      page,
      text,
      cast,
    }) => {
      await page.setViewportSize({ width, height });
      await signInAs(page, cast.user);
      // The shell and its fonts are on the device; the tenant's launch documents are not.
      await warmUp(page, '/tenants');
      const hold = await holdNetwork(page);

      // --- The skeleton: the greeting's lines over the three frames ---
      await page.goto('/1');
      const frames = page.locator('tm-page-skeleton .tm-home-skeleton > .tm-dashboard-frame');
      await expect(frames).toHaveCount(3);
      const greeting = await boxes(page.locator('tm-page-skeleton .tm-home-greeting'));
      const skeleton = await boxes(frames);
      expect(skeleton.every((box) => box.width > 0 && box.height > 0)).toBe(true);

      // --- The page: the same boxes, in reading order ---
      await hold.release();
      await expect(page.getByRole('heading', { level: 1, name: text.home })).toBeVisible();
      const tiles = page.locator('tm-dashboard-tile');
      await expect(tiles).toHaveCount(3);
      await expect(page.locator('tm-page-skeleton')).toHaveCount(0);
      expect(await boxes(page.locator('tm-home-page > tm-home-greeting'))).toEqual(greeting);
      expect(await boxes(tiles)).toEqual(skeleton);
    });
  }
});
