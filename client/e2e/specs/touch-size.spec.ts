// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Touch sizes below the sm breakpoint: an unsized control in page content
 * reaches the 46 px touch height on a phone-width viewport and keeps the
 * workspace's dense default from 576 px up.
 */

test('a form field is 46 px tall at 390 px', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(storyUrl('form-field'));
  const box = page.getByTestId('ff-default').locator('.tm-form-field__box');
  await expect(page.getByTestId('ff-default')).toHaveClass(/tm-form-field--lg/);
  await expect.poll(async () => (await box.boundingBox())!.height).toBe(46);
});

test('the same field keeps the 30 px default at 768 px', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 1024 });
  await page.goto(storyUrl('form-field'));
  const box = page.getByTestId('ff-default').locator('.tm-form-field__box');
  await expect(page.getByTestId('ff-default')).toHaveClass(/tm-form-field--sm/);
  await expect.poll(async () => (await box.boundingBox())!.height).toBe(30);
});

test('the field grows as the viewport crosses 576 px, as on a rotation', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 390 });
  await page.goto(storyUrl('form-field'));
  const box = page.getByTestId('ff-default').locator('.tm-form-field__box');
  await expect.poll(async () => (await box.boundingBox())!.height).toBe(30);
  await page.setViewportSize({ width: 390, height: 768 });
  await expect.poll(async () => (await box.boundingBox())!.height).toBe(46);
});
