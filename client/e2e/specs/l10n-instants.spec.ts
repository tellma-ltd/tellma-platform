// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

// Browser battery for the instant, period and name formats (spec 0010
// §D.4, §D.7, §D.8), on the i18n story's fake clock: 07:42 UTC on 20
// October 2026, shown in Asia/Riyadh to a device in Europe/London. Nothing
// here reads the machine's zone or clock, so the texts are the same on any
// runner.

/** The bidirectional controls: the marks, the embeddings and overrides, the isolates. */
const BIDI_CONTROLS = new RegExp(
  `[${[0x061c, 0x200e, 0x200f, 0x202a, 0x202b, 0x202c, 0x202d, 0x202e, 0x2066, 0x2067, 0x2068, 0x2069]
    .map((codePoint) => String.fromCodePoint(codePoint))
    .join('')}]`,
);

/** One cell of a sample instant's row: its date-time, time, relative time or date. */
function instantCell(
  page: Page,
  id: string,
  column: 'date-time' | 'time' | 'relative' | 'date',
): Locator {
  return page.getByTestId(`i18n-instant-${id}`).locator(`[data-cell="${column}"]`);
}

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('i18n'));
  await expect(page.getByTestId('i18n-instants')).toBeVisible();
});

test.describe('axe floor', () => {
  test('the i18n story is axe-clean', async ({ page }) => {
    await expectNoAxeViolations(page);
  });
});

test.describe('instants in the display zone', () => {
  test('an instant shows in the display zone, whatever offset it was written with', async ({
    page,
  }) => {
    await expect(page.getByTestId('i18n-now')).toHaveText('Tuesday, Oct 20, 2026, 10:42:00 AM');
    // Written as 10:40+03:00 and as 04:12Z: both are Riyadh wall clocks here.
    await expect(instantCell(page, 'minutes', 'date-time')).toHaveText(
      '10/20/2026, 10:40 AM GMT+3',
    );
    await expect(instantCell(page, 'hours', 'date-time')).toHaveText('10/20/2026, 7:12 AM GMT+3');
    await expect(instantCell(page, 'minutes', 'date')).toHaveText('Tuesday, Oct 20, 2026');
    // 21:30 UTC on the 18th is already the 19th in Riyadh.
    await expect(instantCell(page, 'yesterday', 'date')).toHaveText('Monday, Oct 19, 2026');

    await page.getByTestId('i18n-zone').selectOption('UTC');
    await expect(instantCell(page, 'hours', 'date-time')).toHaveText('10/20/2026, 4:12 AM GMT+0');
    await expect(instantCell(page, 'yesterday', 'date')).toHaveText('Sunday, Oct 18, 2026');
  });

  test('relative times truncate, and count days in the display zone', async ({ page }) => {
    await expect(instantCell(page, 'seconds', 'relative')).toHaveText('now');
    await expect(instantCell(page, 'minutes', 'relative')).toHaveText('2 min. ago');
    await expect(instantCell(page, 'hours', 'relative')).toHaveText('3 hr. ago');
    await expect(instantCell(page, 'yesterday', 'relative')).toHaveText('yesterday');
    await expect(instantCell(page, 'days', 'relative')).toHaveText('5 days ago');
    await expect(instantCell(page, 'weeks', 'relative')).toHaveText('Sep 28, 2026');
    await expect(instantCell(page, 'soon', 'relative')).toHaveText('in 5 min.');

    // The same instant is two dates back where midnight falls later.
    await page.getByTestId('i18n-zone').selectOption('UTC');
    await expect(instantCell(page, 'yesterday', 'relative')).toHaveText('2 days ago');
  });

  test('the texts follow the clock', async ({ page }) => {
    await page.getByTestId('i18n-advance-minute').click();
    await expect(page.getByTestId('i18n-now')).toHaveText('Tuesday, Oct 20, 2026, 10:43:00 AM');
    await expect(instantCell(page, 'seconds', 'relative')).toHaveText('1 min. ago');
    await expect(instantCell(page, 'minutes', 'relative')).toHaveText('3 min. ago');
    await expect(instantCell(page, 'soon', 'relative')).toHaveText('in 4 min.');

    await page.getByTestId('i18n-advance-hour').click();
    await expect(instantCell(page, 'minutes', 'relative')).toHaveText('1 hr. ago');
    await expect(instantCell(page, 'soon', 'relative')).toHaveText('56 min. ago');

    await page.getByTestId('i18n-advance-day').click();
    await expect(instantCell(page, 'minutes', 'relative')).toHaveText('yesterday');
    await expect(instantCell(page, 'days', 'relative')).toHaveText('6 days ago');
    // An absolute text never moves with the clock.
    await expect(instantCell(page, 'minutes', 'date-time')).toHaveText(
      '10/20/2026, 10:40 AM GMT+3',
    );

    await page.getByTestId('i18n-reset-clock').click();
    await expect(instantCell(page, 'minutes', 'relative')).toHaveText('2 min. ago');
  });
});

test.describe('zone labels', () => {
  test('a device at another offset is told whose clock it reads', async ({ page }) => {
    await expect(page.getByTestId('i18n-zone-mismatch')).toHaveText('yes');
    await expect(page.getByTestId('i18n-zone-label')).toHaveText('GMT+3');
    // The column's header carries the label, so its cells stay bare.
    await expect(page.getByTestId('i18n-time-header')).toContainText('GMT+3');
    await expect(instantCell(page, 'minutes', 'time')).toHaveText('10:40 AM');
    // A relative time and a date are never labelled.
    await expect(instantCell(page, 'minutes', 'relative')).not.toContainText('GMT');
    await expect(instantCell(page, 'minutes', 'date')).not.toContainText('GMT');
  });

  test('equal offsets never label, whatever the zones are called', async ({ page }) => {
    await page.getByTestId('i18n-device-zone').selectOption('Asia/Kuwait');
    await expect(page.getByTestId('i18n-zone-mismatch')).toHaveText('no');
    await expect(page.getByTestId('i18n-zone-label')).toHaveText('—');
    await expect(page.getByTestId('i18n-time-header')).not.toContainText('GMT');
    await expect(instantCell(page, 'minutes', 'date-time')).toHaveText('10/20/2026, 10:40 AM');
    await expect(instantCell(page, 'minutes', 'time')).toHaveText('10:40 AM');
  });

  test('offsets are compared at the instant shown, across a daylight-saving change', async ({
    page,
  }) => {
    // Beirut on a Riyadh device: level in October, an hour apart in January.
    await page.getByTestId('i18n-zone').selectOption('Asia/Beirut');
    await page.getByTestId('i18n-device-zone').selectOption('Asia/Riyadh');
    await expect(page.getByTestId('i18n-zone-mismatch')).toHaveText('no');
    await expect(instantCell(page, 'minutes', 'date-time')).toHaveText('10/20/2026, 10:40 AM');
    await expect(instantCell(page, 'winter', 'date-time')).toHaveText('1/15/2026, 11:00 AM GMT+2');
    // Under the bare header, the January cell labels on its own.
    await expect(instantCell(page, 'winter', 'time')).toHaveText('11:00 AM GMT+2');
    await expect(instantCell(page, 'minutes', 'time')).toHaveText('10:40 AM');
  });

  test('a half-hour offset is written in full', async ({ page }) => {
    await page.getByTestId('i18n-zone').selectOption('Asia/Kolkata');
    await expect(page.getByTestId('i18n-zone-label')).toHaveText('GMT+5:30');
    await expect(instantCell(page, 'minutes', 'date-time')).toHaveText(
      '10/20/2026, 1:10 PM GMT+5:30',
    );
  });
});

test.describe('periods and names', () => {
  test('the date styles, the month names and the weekday names', async ({ page }) => {
    await expect(page.getByTestId('i18n-date-numeric')).toHaveText('10/20/2026');
    await expect(page.getByTestId('i18n-date-medium')).toHaveText('Oct 20, 2026');
    await expect(page.getByTestId('i18n-date-long')).toHaveText('October 20, 2026');
    await expect(page.getByTestId('i18n-date-weekday')).toHaveText('Tuesday, Oct 20, 2026');
    await expect(page.getByTestId('i18n-date-month')).toHaveText('Oct 2026');
    await expect(page.getByTestId('i18n-date-quarter')).toHaveText('Q4 2026');
    await expect(page.getByTestId('i18n-date-year')).toHaveText('2026');

    const months = page.getByTestId('i18n-month-names').locator('li');
    await expect(months).toHaveCount(12);
    await expect(months.first()).toHaveText('January');
    await expect(months.last()).toHaveText('December');
    await expect(page.getByTestId('i18n-weekday-names').locator('li')).toHaveText([
      'Monday',
      'Tuesday',
      'Wednesday',
      'Thursday',
      'Friday',
      'Saturday',
      'Sunday',
    ]);
  });

  test('periods and month names follow the ambient calendar', async ({ page }) => {
    // 20 October 2026 is 9 Jumada I 1448: the fifth month, the second quarter.
    await page.getByTestId('shell-calendar').selectOption('islamic-umalqura');
    await expect(page.getByTestId('i18n-date-quarter')).toHaveText('Q2 1448');
    await expect(page.getByTestId('i18n-date-year')).toHaveText('1448');
    await expect(page.getByTestId('i18n-date-month')).toHaveText('Jum. I 1448');
    const months = page.getByTestId('i18n-month-names').locator('li');
    await expect(months).toHaveCount(12);
    await expect(months.first()).toHaveText('Muharram');
    await expect(instantCell(page, 'minutes', 'date-time')).toHaveText('5/9/1448, 10:40 AM GMT+3');

    // The Ethiopic year has thirteen months.
    await page.getByTestId('shell-calendar').selectOption('ethiopic');
    await expect(months).toHaveCount(13);
    await expect(months.last()).toHaveText('Pagumen');
    await expect(page.getByTestId('i18n-date-quarter')).toHaveText('Q1 2019');
  });
});

test.describe('Arabic', () => {
  test('instants, periods and labels speak Arabic, with no direction mark', async ({ page }) => {
    await page.getByTestId('lang-ar').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    // The quarter is written through the Arabic pack's pattern.
    await expect(page.getByTestId('i18n-date-quarter')).toHaveText('الربع ٤ ٢٠٢٦');
    await expect(page.getByTestId('i18n-zone-label')).toHaveText('غرينتش+٣');
    await expect(instantCell(page, 'minutes', 'time')).toHaveText('١٠:٤٠ ص');
    await expect(page.getByTestId('i18n-date-numeric')).toHaveText('٢٠/١٠/٢٠٢٦');
    await expect(page.getByTestId('i18n-weekday-names').locator('li').last()).toHaveText('الأحد');

    // No text of the story holds a bidirectional control character.
    const text = await page.locator('main.story-main').innerText();
    expect(text).not.toMatch(BIDI_CONTROLS);
  });

  test('a left-to-right run keeps a minus at the left of Arabic-Indic digits', async ({ page }) => {
    await page.getByTestId('lang-ar').click();
    const grouped = page.getByTestId('i18n-number-grouped');
    await expect(grouped).toHaveText('-١٬٢٣٤٬٥٦٧٫٥');
    await expect(page.getByTestId('i18n-number-ungrouped')).toHaveText('٢٠٢٦');
    const minusIsLeftmost = await grouped.evaluate((element) => {
      const node = element.firstChild as Text;
      const range = document.createRange();
      range.setStart(node, 0);
      range.setEnd(node, 1);
      const minus = range.getBoundingClientRect();
      range.setStart(node, 1);
      range.setEnd(node, node.length);
      return minus.right <= range.getBoundingClientRect().left + 0.5;
    });
    expect(minusIsLeftmost).toBe(true);
  });
});
