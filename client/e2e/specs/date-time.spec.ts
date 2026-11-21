// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { fontsLoaded } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-time-field and tm-date-time-field: the axe floor
 * in both themes and directions, Up and Down stepping the time, the instant
 * written on the zone's clock and described by the zone label, and the
 * label changing width across a daylight-saving change without moving
 * anything.
 */

/** An element's rendered box, which must exist. */
async function boxOf(
  locator: Locator,
): Promise<{ x: number; y: number; width: number; height: number }> {
  const box = await locator.boundingBox();
  expect(box, 'the element is rendered').not.toBeNull();
  return box!;
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`date-time story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('date-time', { theme, dir }));
        await expect(
          page
            .getByTestId('dt-required')
            .locator('tm-form-field')
            .first()
            .locator('.tm-form-field__error'),
        ).toHaveText('Enter when it is due');
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('Up and Down step the time by the step, across midnight', async ({ page }) => {
  await page.goto(storyUrl('date-time'));
  const time = page.getByTestId('time-closes').getByRole('textbox');
  await time.fill('11:45 pm');
  await time.press('Enter');
  await expect(time).toHaveValue('11:45 PM');
  await time.press('ArrowUp');
  await expect(time).toHaveValue('12:15 AM');
  await expect(page.getByTestId('model-json')).toContainText('"closes":"00:15"');
  await time.press('ArrowDown');
  await expect(time).toHaveValue('11:45 PM');
});

test('typed parts write the instant with the zone offset', async ({ page }) => {
  await page.goto(storyUrl('date-time'));
  const field = page.getByTestId('dt-beirut');
  await field.getByRole('combobox', { name: 'Date' }).fill('7/15/2026');
  await field.getByRole('textbox', { name: 'Time' }).fill('2 pm');
  await expect(page.getByTestId('model-json')).toContainText(
    '"startsAt":"2026-07-15T14:00:00+03:00"',
  );
  await expect(field.locator('.tm-date-time-field__zone')).toHaveText('GMT+3');
  // The zone is announced with the time it qualifies.
  await expect(field.getByRole('textbox', { name: 'Time' })).toHaveAccessibleDescription('GMT+3');
});

for (const dir of ['ltr', 'rtl'] as const) {
  test(`the zone label changes width across a DST change and moves nothing (${dir})`, async ({
    page,
  }) => {
    await page.goto(storyUrl('date-time', { dir }));
    // Adelaide in a fixed-width column: "GMT+10:30" in its summer, "GMT+9:30" in its winter.
    const field = page.getByTestId('dt-adelaide');
    const date = field.getByRole('combobox', { name: 'Date' });
    const time = field.getByRole('textbox', { name: 'Time' });
    const zone = field.locator('.tm-date-time-field__zone');

    await date.fill('1/15/2026');
    await time.fill('10');
    await expect(zone).toHaveText('GMT+10:30');
    await fontsLoaded(page);
    const dateBefore = await boxOf(date);
    const timeBefore = await boxOf(time);
    const zoneBefore = await boxOf(zone);

    await date.fill('7/15/2026');
    await expect(zone).toHaveText('GMT+9:30');
    expect(await boxOf(date)).toEqual(dateBefore);
    expect(await boxOf(time)).toEqual(timeBefore);
    const zoneAfter = await boxOf(zone);
    // The label's start edge (its side next to the time) holds.
    if (dir === 'ltr') {
      expect(zoneAfter.x).toBe(zoneBefore.x);
    } else {
      expect(zoneAfter.x + zoneAfter.width).toBe(zoneBefore.x + zoneBefore.width);
    }
    expect(zoneAfter.y).toBe(zoneBefore.y);
  });
}

test('one part without the other errs on the empty part', async ({ page }) => {
  await page.goto(storyUrl('date-time'));
  const field = page.getByTestId('dt-beirut');
  await field.getByRole('combobox', { name: 'Date' }).fill('1/15/2026');
  await field.getByRole('combobox', { name: 'Date' }).blur();
  const timeField = field.locator('tm-form-field').nth(1);
  await expect(timeField.locator('.tm-form-field__error')).toHaveText(
    'Enter both a date and a time',
  );
  await expect(page.getByTestId('model-json')).toContainText('"startsAt":null');
});
