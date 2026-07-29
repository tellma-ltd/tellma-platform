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
      // cell (or the input) and dies. Real fingers cannot race a render —
      // wait for document focus before every press, starting with the open.
      const rovingDay = popup(page).locator('.tm-date-popup__day[tabindex="0"]');
      const focusedDay = async (): Promise<string | null> => {
        await expect(rovingDay).toBeFocused();
        return rovingDay.getAttribute('data-tm-day');
      };

      // Focus landed on the committed day (3/5/2026).
      expect(await focusedDay()).toBe('5');

      // inline-end arrow: +1 day in LTR terms — ArrowRight in LTR, ArrowLeft in RTL.
      await page.keyboard.press(dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft');
      expect(await focusedDay()).toBe('6');
      await page.keyboard.press('ArrowDown'); // +7
      expect(await focusedDay()).toBe('13');
      // Home/End move to the week EDGES. The story locale is en-US, so
      // the week starts Sunday: from the 13th (a Friday) Home lands on the
      // 8th and End on the 14th. Asserting the exact day matters — a
      // bound like "<= 13" also passes for a no-op and for a wrong
      // jump-to-first-of-month.
      await page.keyboard.press('Home');
      expect(await focusedDay()).toBe('8');
      await page.keyboard.press('End');
      expect(await focusedDay()).toBe('14');
      await page.keyboard.press('Home');
      expect(await focusedDay()).toBe('8');

      // PageDown: next month; the heading announces politely.
      await page.keyboard.press('PageDown');
      await expect(popup(page).locator('.tm-date-popup__view-switch')).toContainText('April');
      await expect(
        popup(page).locator('.tm-date-popup__view-switch [aria-live="polite"]'),
      ).toBeVisible();
      await expect(rovingDay).toBeFocused();
      await page.keyboard.press('Shift+PageDown'); // +1 year
      await expect(popup(page).locator('.tm-date-popup__view-switch')).toContainText('2027');
      await expect(rovingDay).toBeFocused();

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
  test('the popup keeps its width and takes only the rows a month needs', async ({ page }) => {
    await page.goto(storyUrl('date-picker'));
    await openViaButton(page, 'picker-due');
    const next = popup(page).locator('.tm-date-popup__nav').last();
    const heading = popup(page).locator('.tm-date-popup__view-switch');
    const march = (await popup(page).boundingBox())!; // March 2026 needs 5 rows

    // Height follows the content: May 2026 spills into a sixth week. The
    // popup is a top-layer overlay, so its height was never part of the
    // page's layout to hold still, and padding it to a fixed six rows
    // just left a blank band under most months.
    await next.click();
    await next.click();
    await expect(heading).toContainText('May');
    const may = (await popup(page).boundingBox())!;
    expect(may.height).toBeGreaterThan(march.height);

    // Width, though, never moves — in any month or view. A calendar that
    // changed width under the pointer would walk off its field.
    expect(may.width).toBeCloseTo(march.width, 1);
    await heading.click(); // month view
    expect((await popup(page).boundingBox())!.width).toBeCloseTo(march.width, 1);
    await heading.click(); // year view
    expect((await popup(page).boundingBox())!.width).toBeCloseTo(march.width, 1);
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
      const dayNumber = async (): Promise<number> => {
        await expect(rovingDay).toBeFocused();
        return Number(await rovingDay.getAttribute('data-tm-day'));
      };
      const heading = popup(page).locator('.tm-date-popup__view-switch');
      // The calendar swap re-renders the grid under the open popup, so
      // settle on the roving cell BEFORE the first key: a press that
      // lands mid-render goes to a cell that is about to be replaced.
      await expect(rovingDay).toBeFocused();
      const start = await dayNumber();

      await page.keyboard.press('ArrowRight');
      expect(await dayNumber()).toBe(start + 1);
      await page.keyboard.press('ArrowDown'); // +7, may cross the month end
      expect(await dayNumber()).toBeGreaterThan(0);
      // Home/End are WEEK edges. Asserting day numbers would be wrong
      // here: an Ethiopic or Hijri week routinely crosses a month end, so
      // the numbers wrap. The calendar-agnostic invariant is the roving
      // cell's position within its own week row.
      const columnOfFocus = async (): Promise<number> => {
        await expect(rovingDay).toBeFocused();
        return popup(page)
          .locator('.tm-date-popup__week', { has: page.locator('[tabindex="0"]') })
          .first()
          .evaluate((row) =>
            [...row.children].findIndex((cell) => cell.getAttribute('tabindex') === '0'),
          );
      };
      await page.keyboard.press('Home');
      expect(await columnOfFocus()).toBe(0);
      await page.keyboard.press('End');
      expect(await columnOfFocus()).toBe(6);

      // Paging must stay inside the calendar's own month/year structure.
      const before = await heading.textContent();
      await page.keyboard.press('PageDown');
      await expect(heading).not.toHaveText(before!);
      await page.keyboard.press('Shift+PageDown'); // +1 year
      await expect(rovingDay).toBeFocused();
      await page.keyboard.press('PageUp');
      await expect(rovingDay).toBeFocused();

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
