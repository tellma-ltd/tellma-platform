// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-switch: the axe floor in both themes and
 * directions, Space and a click on the label, the knob mirrored in RTL, the
 * on state under forced colors, and a still knob under reduced motion.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`switch story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('switch', { theme, dir }));
        await expect(page.getByTestId('ff-terms').locator('.tm-form-field__error')).toHaveText(
          'Turn this on to continue',
        );
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('Space and a click on the label toggle the switch', async ({ page }) => {
  await page.goto(storyUrl('switch'));
  const control = page.getByRole('switch', { name: 'Notify me by email' });
  await control.focus();
  await page.keyboard.press('Space');
  await expect(control).toBeChecked();
  await expect(page.getByTestId('model-json')).toContainText('"notify":true');
  await page.getByText('Notify me by email').click();
  await expect(control).not.toBeChecked();
});

test('the knob sits at the inline end when on, in both directions', async ({ page }) => {
  for (const dir of ['ltr', 'rtl'] as const) {
    await page.goto(storyUrl('switch', { dir }));
    const host = page.getByTestId('switch-on');
    const track = (await host.locator('.tm-switch__track').boundingBox())!;
    const knob = (await host.locator('.tm-switch__knob').boundingBox())!;
    const knobCentre = knob.x + knob.width / 2;
    const trackCentre = track.x + track.width / 2;
    expect(knobCentre > trackCentre, dir).toBe(dir === 'ltr');
  }
});

test('the on state survives forced colors', async ({ page }) => {
  await page.emulateMedia({ forcedColors: 'active' });
  await page.goto(storyUrl('switch'));
  const fill = (id: string) =>
    page
      .getByTestId(id)
      .locator('.tm-switch__track')
      .evaluate((element) => getComputedStyle(element).backgroundColor);
  const off = await fill('switch-notify');
  const on = await fill('switch-on');
  expect(on).not.toBe(off);
  const knob = await page
    .getByTestId('switch-on')
    .locator('.tm-switch__knob')
    .evaluate((element) => getComputedStyle(element).backgroundColor);
  expect(knob).not.toBe(on);
});

test('the knob does not travel under reduced motion', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.goto(storyUrl('switch'));
  const duration = await page
    .getByTestId('switch-notify')
    .locator('.tm-switch__knob')
    .evaluate((element) => getComputedStyle(element).transitionDuration);
  expect(duration).toBe('0s');
});
