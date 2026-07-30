// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-date-picker (DoD 5–8): the APG keyboard matrix in
 * LTR and RTL and per display calendar, the fixed 6-row grid, Today/Clear,
 * bounds clamping, the polite heading, axe with the popup open, and the
 * live calendar/locale switches with the ISO model unchanged.
 */

const popup = (page: Page) => page.locator('.tm-date-popup');
const openViaButton = async (page: Page, pickerId: string) => {
  await page.getByTestId(pickerId).locator('.tm-date-picker__toggle').click();
  await expect(popup(page)).toBeVisible();
};

test.describe('axe floor (popup open included)', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`date-picker story with open popup is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('date-picker', { theme }));
      await openViaButton(page, 'picker-due');
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('typed entry + popup selection (DoD 5/7)', () => {
  test('typing commits ISO; the popup opens on the committed day and selects', async ({
    page,
  }) => {
    await page.goto(storyUrl('date-picker'));
    const input = page.getByTestId('picker-due').locator('.tm-date-picker__input');
    await expect(input).toHaveValue('3/5/2026');

    await input.fill('7/14/2026');
    await input.press('Alt+ArrowDown'); // opens; commits pending text first
    await expect(popup(page)).toBeVisible();
    await expect(popup(page).locator('.tm-date-popup__view-switch')).toContainText('July');
    await expect(popup(page).locator('[data-tm-day="14"]')).toHaveAttribute(
      'aria-selected',
      'true',
    );

    await popup(page).locator('[data-tm-day="20"]').click();
    await expect(popup(page)).toBeHidden();
    await expect(input).toHaveValue('7/20/2026');
    await expect(page.getByTestId('model-json')).toContainText('"due":"2026-07-20"');
    await expect(input).toBeFocused();
  });

  test('Today and Clear commit today / null', async ({ page }) => {
    await page.goto(storyUrl('date-picker'));
    await openViaButton(page, 'picker-due');
    await popup(page).locator('.tm-date-popup__action').first().click();
    const today = await page.evaluate(() => {
      const now = new Date();
      return `${String(now.getFullYear()).padStart(4, '0')}-${String(now.getMonth() + 1).padStart(
        2,
        '0',
      )}-${String(now.getDate()).padStart(2, '0')}`;
    });
    await expect(page.getByTestId('model-json')).toContainText(`"due":"${today}"`);

    await openViaButton(page, 'picker-due');
    await popup(page).locator('.tm-date-popup__action').last().click();
    await expect(page.getByTestId('model-json')).toContainText('"due":null');
  });
});

test.describe('keyboard matrix (DoD 7)', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`grid keys navigate (direction-mapped) and Esc returns to the input (${dir})`, async ({
      page,
    }) => {
      await page.goto(storyUrl('date-picker', { dir }));
      const input = page.getByTestId('picker-due').locator('.tm-date-picker__input');
      await input.click();
      await input.press('Alt+ArrowDown');
      await expect(popup(page)).toBeVisible();

      // Every navigation re-renders the grid and re-focuses the roving cell
      // a beat later; a key pressed inside that beat lands on a detached
      // cell (or the input) and dies. Real fingers cannot race a render, so
      // each step waits for the day it EXPECTS to hold focus before the next
      // press. Waiting on "whichever cell is roving" would not do: until the
      // re-render lands, that is still the cell the key was pressed from, so
      // the assertion passes against the old state and the next press goes
      // out early.
      const rovingDay = popup(page).locator('.tm-date-popup__day[tabindex="0"]');
      const expectFocusedDay = async (day: string): Promise<void> => {
        await expect(popup(page).locator(`.tm-date-popup__day[data-tm-day="${day}"]`)).toBeFocused();
      };

      // Focus landed on the committed day (3/5/2026).
      await expectFocusedDay('5');

      // inline-end arrow: +1 day in LTR terms — ArrowRight in LTR, ArrowLeft in RTL.
      await page.keyboard.press(dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft');
      await expectFocusedDay('6');
      await page.keyboard.press('ArrowDown'); // +7
      await expectFocusedDay('13');
      // Home/End move to the week EDGES. The story locale is en-US, so
      // the week starts Sunday: from the 13th (a Friday) Home lands on the
      // 8th and End on the 14th. Asserting the exact day matters — a
      // bound like "<= 13" also passes for a no-op and for a wrong
      // jump-to-first-of-month.
      await page.keyboard.press('Home');
      await expectFocusedDay('8');
      await page.keyboard.press('End');
      await expectFocusedDay('14');
      await page.keyboard.press('Home');
      await expectFocusedDay('8');

      // PageDown: next month; the heading announces politely.
      await page.keyboard.press('PageDown');
      await expect(popup(page).locator('.tm-date-popup__view-switch')).toContainText('April');
      await expect(
        popup(page).locator('.tm-date-popup__view-switch [aria-live="polite"]'),
      ).toBeVisible();
      await expectFocusedDay('8'); // the day carries across the month page
      await page.keyboard.press('Shift+PageDown'); // +1 year
      await expect(popup(page).locator('.tm-date-popup__view-switch')).toContainText('2027');
      await expectFocusedDay('8');

      // Enter selects the focused day and returns focus to the input.
      await page.keyboard.press('Enter');
      await expect(popup(page)).toBeHidden();
      await expect(input).toBeFocused();
      await expect(page.getByTestId('model-json')).toContainText('"due":"2027-04');

      // Esc on a reopened popup closes WITHOUT committing.
      const committed = await input.inputValue();
      await input.press('Alt+ArrowDown');
      await expect(popup(page)).toBeVisible();
      await expect(rovingDay).toBeFocused();
      await page.keyboard.press('ArrowRight');
      await expect(rovingDay).toBeFocused();
      await page.keyboard.press('Escape');
      await expect(popup(page)).toBeHidden();
      await expect(input).toHaveValue(committed);
      await expect(input).toBeFocused();
    });
  }
});

test.describe('fixed geometry (DoD 7)', () => {
  test('the popup keeps its exact box across months and views', async ({ page }) => {
    await page.goto(storyUrl('date-picker'));
    await openViaButton(page, 'picker-due');
    const next = popup(page).locator('.tm-date-popup__nav').last();
    const heading = popup(page).locator('.tm-date-popup__view-switch');
    const march = (await popup(page).boundingBox())!; // March 2026 needs 5 rows

    // May 2026 spills into a sixth week; the box must not notice. A popup
    // that resizes under the pointer moves the very controls the user is
    // clicking (opened upward, its header arrows walk up the screen with
    // every page), and one that fitted the viewport on open can grow out
    // of it. A blank row on a short month is the cheaper compromise.
    await next.click();
    await next.click();
    await expect(heading).toContainText('May');
    const may = (await popup(page).boundingBox())!;
    expect(may.height).toBeCloseTo(march.height, 1);
    expect(may.width).toBeCloseTo(march.width, 1);

    // Same box in the coarser views. Wait for each view to actually render
    // before measuring — a box read in the click's own beat is the previous
    // view's, which is how a resizing popup passed this test once already.
    await heading.click();
    await expect(popup(page).locator('.tm-date-popup__months')).toBeVisible();
    const months = (await popup(page).boundingBox())!;
    expect(months.width).toBeCloseTo(march.width, 1);
    expect(months.height).toBeCloseTo(march.height, 1);
    await heading.click();
    await expect(popup(page).locator('.tm-date-popup__years')).toBeVisible();
    const years = (await popup(page).boundingBox())!;
    expect(years.width).toBeCloseTo(march.width, 1);
    expect(years.height).toBeCloseTo(march.height, 1);
  });

  test('bounds clamp navigation and disable out-of-range days', async ({ page }) => {
    await page.goto(storyUrl('date-picker'));
    await openViaButton(page, 'picker-bounded');
    // The bounded field is empty: the popup opens on today clamped into
    // March 2026.
    await expect(popup(page).locator('.tm-date-popup__view-switch')).toContainText('March');
    await expect(popup(page).locator('[data-tm-day="12"]')).not.toHaveAttribute(
      'aria-disabled',
      'true',
    );
  });
});

test.describe('display calendars (DoD 8)', () => {
  test('runtime calendar switch re-renders text and popup; the ISO model never moves', async ({
    page,
  }) => {
    await page.goto(storyUrl('date-picker'));
    const input = page.getByTestId('picker-due').locator('.tm-date-picker__input');
    await expect(input).toHaveValue('3/5/2026');

    await page.getByTestId('cal-umalqura').click();
    // 2026-03-05 is in Ramadan 1447 AH — the display re-renders in place.
    await expect(input).not.toHaveValue('3/5/2026');
    await expect(page.getByTestId('model-json')).toContainText('"due":"2026-03-05"');

    await openViaButton(page, 'picker-due');
    await expect(popup(page).locator('.tm-date-popup__view-switch')).toContainText('Ramadan');
    await page.keyboard.press('Escape');

    await page.getByTestId('cal-ethiopic').click();
    await openViaButton(page, 'picker-due');
    // The Ethiopic month grid shows 13 selectable months incl. Pagume.
    await popup(page).locator('.tm-date-popup__view-switch').click();
    await expect(popup(page).locator('[data-tm-month="13"]')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('model-json')).toContainText('"due":"2026-03-05"');
  });

  // The keyboard model runs on the DISPLAY calendar's arithmetic, so a
  // 13-month year (Ethiopic) and 29/30-day months (Hijri) exercise paths
  // the Gregorian matrix above never reaches — this is exactly the class
  // of bug that produced a ~10,000-year jump earlier in this branch.
  for (const calendar of ['umalqura', 'ethiopic'] as const) {
    test(`the grid keyboard model holds under ${calendar}`, async ({ page }) => {
      await page.goto(storyUrl('date-picker'));
      await page.getByTestId(`cal-${calendar}`).click();
      await openViaButton(page, 'picker-due');

      const rovingDay = popup(page).locator('.tm-date-popup__day[tabindex="0"]');
      const dayNumber = async (): Promise<number> =>
        Number(await rovingDay.getAttribute('data-tm-day'));
      const heading = popup(page).locator('.tm-date-popup__view-switch');
      // The calendar swap re-renders the grid under the open popup, so
      // settle on the roving cell BEFORE the first key: a press that lands
      // mid-render goes to a cell that is about to be replaced. Every later
      // step POLLS for the position it expects — reading once after "some
      // cell is roving" would read the cell the key was pressed from, since
      // that one still holds focus until the re-render lands.
      await expect(rovingDay).toBeFocused();
      const start = await dayNumber();

      await page.keyboard.press('ArrowRight');
      await expect(
        popup(page).locator(`.tm-date-popup__day[data-tm-day="${start + 1}"]`),
      ).toBeFocused();
      // +7 may cross the month end, so the day number is not predictable —
      // but it must MOVE, which is what a dropped key would not do.
      await page.keyboard.press('ArrowDown');
      await expect.poll(dayNumber).not.toBe(start + 1);
      // Home/End are WEEK edges. Asserting day numbers would be wrong
      // here: an Ethiopic or Hijri week routinely crosses a month end, so
      // the numbers wrap. The calendar-agnostic invariant is the roving
      // cell's position within its own week row.
      const columnOfFocus = (): Promise<number> =>
        popup(page)
          .locator('.tm-date-popup__week', { has: page.locator('[tabindex="0"]') })
          .first()
          .evaluate((row) =>
            [...row.children].findIndex((cell) => cell.getAttribute('tabindex') === '0'),
          );
      await page.keyboard.press('Home');
      await expect.poll(columnOfFocus).toBe(0);
      await page.keyboard.press('End');
      await expect.poll(columnOfFocus).toBe(6);

      // Paging must stay inside the calendar's own month/year structure.
      // The heading is the barrier for each step: it is the one thing that
      // provably changed, so the next key cannot go out early.
      const beforeMonth = await heading.textContent();
      await page.keyboard.press('PageDown');
      await expect(heading).not.toHaveText(beforeMonth!);
      const beforeYear = await heading.textContent();
      await page.keyboard.press('Shift+PageDown'); // +1 year
      await expect(heading).not.toHaveText(beforeYear!);
      const beforeBack = await heading.textContent();
      await page.keyboard.press('PageUp');
      await expect(heading).not.toHaveText(beforeBack!);

      // Space selects, like Enter (the cells are real buttons).
      const chosen = await dayNumber();
      await page.keyboard.press('Space');
      await expect(popup(page)).toBeHidden();
      await expect(page.getByTestId('model-json')).toContainText('"due":"');
      expect(chosen).toBeGreaterThan(0);
    });
  }

  test('live locale switch re-renders the display; the model never moves', async ({ page }) => {
    await page.goto(storyUrl('date-picker'));
    const input = page.getByTestId('picker-due').locator('.tm-date-picker__input');
    await expect(input).toHaveValue('3/5/2026');
    await page.getByTestId('lang-ar').click();
    await expect(input).not.toHaveValue('3/5/2026'); // ar-SA digits/order
    await expect(page.getByTestId('model-json')).toContainText('"due":"2026-03-05"');
    await page.getByTestId('lang-en').click();
    await expect(input).toHaveValue('3/5/2026');
  });
});

test.describe('forced-colors + reduced-motion gates', () => {
  test('forced-colors keeps the popup boundary and selection visible', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('date-picker'));
    await openViaButton(page, 'picker-due');
    const borderStyle = await popup(page).evaluate(
      (el) => getComputedStyle(el.querySelector('.tm-date-popup') ?? el).borderStyle,
    );
    expect(borderStyle).toBe('solid');
  });

  test('reduced-motion leaves no animation or transition running', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('date-picker'));
    await openViaButton(page, 'picker-due');
    await expect(popup(page)).toBeVisible();

    // The popup ships without motion today, so this pins the ABSENCE:
    // asserting only visibility would pass no matter what anyone adds
    // later, while this fails the moment an ungated animation or
    // transition appears on the panel or its cells.
    const motion = await popup(page).evaluate((host) => {
      const panel = host.querySelector('.tm-date-popup') ?? host;
      const cell = panel.querySelector('.tm-date-popup__day') ?? panel;
      return [panel, cell].map((element) => {
        const style = getComputedStyle(element);
        return `${style.animationName}|${style.transitionDuration}`;
      });
    });
    for (const entry of motion) {
      expect(entry).toBe('none|0s');
    }
  });
});
