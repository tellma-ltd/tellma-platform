// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-chip and tm-chip-set: the axe floor in both themes
 * and directions, the set's roving keys in LTR and RTL, removal moving focus
 * on, the collapse fit following the set's width, the scrolling line
 * bringing the focused chip into view without scrolling the page, the
 * input hand-off of a multi-value field, and 24 px targets on the small
 * step.
 */

/** The text of the chip that holds focus. */
function focusedChipText(page: Page): Promise<string | null> {
  return page.evaluate(
    () => document.activeElement?.closest('tm-chip')?.textContent?.trim() ?? null,
  );
}

/** Whether focus is on the remove button of the focused chip. */
function focusOnRemove(page: Page): Promise<boolean> {
  return page.evaluate(
    () => document.activeElement?.classList.contains('tm-chip__remove') === true,
  );
}

/** Whether the focused chip lies wholly inside the line's box. */
function focusedChipInView(line: HTMLElement): boolean {
  const chip = document.activeElement!.closest('tm-chip')!.getBoundingClientRect();
  const box = line.getBoundingClientRect();
  return chip.left >= box.left - 0.5 && chip.right <= box.right + 0.5;
}

/** Whether the line's last chip lies wholly inside the line's box. */
function lastChipInView(line: HTMLElement): boolean {
  const chip = line.querySelector('tm-chip:last-of-type')!.getBoundingClientRect();
  const box = line.getBoundingClientRect();
  return chip.left >= box.left - 0.5 && chip.right <= box.right + 0.5;
}

/** The chips a set shows (collapse mode hides the rest). */
function visibleChips(set: Locator): Locator {
  return set.locator('tm-chip:not(.tm-chip--collapsed)');
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`chip story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('chip', { theme, dir }));
        await expect(
          page.getByTestId('set-collapse').locator('.tm-chip-set__more-button'),
        ).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('keyboard', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`one tab stop; the arrows move along the line, mapped (${dir})`, async ({ page }) => {
      await page.goto(storyUrl('chip', { dir }));
      const set = page.getByTestId('set-wrap');
      await page.getByTestId('chip-log').click();
      // Tab into the set lands on its first control: Service's remove button.
      await set.locator('.tm-chip__remove').first().focus();
      expect(await focusedChipText(page)).toBe('Service');
      const forward = dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft';
      const back = dir === 'ltr' ? 'ArrowLeft' : 'ArrowRight';
      await page.keyboard.press(forward);
      expect(await focusedChipText(page)).toBe('Sale');
      await page.keyboard.press(back);
      expect(await focusedChipText(page)).toBe('Service');
      await page.keyboard.press('End');
      expect(await focusedChipText(page)).toBe('Inventory');
      await page.keyboard.press('Home');
      expect(await focusedChipText(page)).toBe('Service');
      // One tab stop: Tab leaves the set for the Reset button.
      await page.keyboard.press('Tab');
      await expect(page.getByTestId('reset-wrap')).toBeFocused();
      await page.keyboard.press('Shift+Tab');
      expect(await focusedChipText(page)).toBe('Service');
      // Past the start: the set emits exit('start').
      await page.keyboard.press(back);
      await expect(page.getByTestId('chip-log')).toHaveText('exit start');
    });
  }

  test('Enter on a chip’s label button activates it; the arrows go on from there', async ({
    page,
  }) => {
    await page.goto(storyUrl('chip'));
    const set = page.getByTestId('set-collapse');
    await page.getByTestId('collapse-wide').click();
    await set.locator('button.tm-chip__label').first().focus();
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('chip-log')).toHaveText('activate Service');
    await page.keyboard.press('ArrowRight');
    expect(await focusedChipText(page)).toBe('Sale');
  });

  test('Delete removes the focused chip and focus moves to the next, else the previous', async ({
    page,
  }) => {
    await page.goto(storyUrl('chip'));
    const set = page.getByTestId('set-wrap');
    await set.locator('.tm-chip__remove').nth(1).focus();
    await page.keyboard.press('Delete');
    await expect(set.locator('tm-chip')).toHaveCount(5);
    expect(await focusedChipText(page)).toBe('Operation');
    expect(await focusOnRemove(page)).toBe(true);
    await page.keyboard.press('End');
    await page.keyboard.press('Backspace');
    await expect(set.locator('tm-chip')).toHaveCount(4);
    expect(await focusedChipText(page)).toBe('Payroll');
  });
});

test.describe('collapse', () => {
  test('hides what does not fit behind a "+N" chip, and refits when the set widens', async ({
    page,
  }) => {
    await page.goto(storyUrl('chip'));
    const set = page.getByTestId('set-collapse');
    const more = set.locator('.tm-chip-set__more-button');
    await expect(more).toBeVisible();
    const narrow = await visibleChips(set).count();
    expect(narrow).toBeLessThan(6);
    await expect(more.locator('.tm-chip-set__more-count')).toHaveText(`+${6 - narrow}`);
    await expect(more).toHaveAttribute('aria-label', new RegExp(`^${6 - narrow} more: `));
    const box = (await set.boundingBox())!;
    expect(box.height).toBeLessThan(40);

    await page.getByTestId('collapse-wide').click();
    await expect(more).toBeHidden();
    await expect(visibleChips(set)).toHaveCount(6);
    expect((await set.boundingBox())!.height).toBe(box.height);
  });

  test('the "+N" chip ends the line for the arrows and shows its list in a tooltip on focus', async ({
    page,
  }) => {
    await page.goto(storyUrl('chip'));
    const set = page.getByTestId('set-collapse');
    const more = set.locator('.tm-chip-set__more-button');
    await set.locator('button.tm-chip__label').first().focus();
    await page.keyboard.press('End');
    await expect(more).toBeFocused();
    await expect(page.locator('.tm-tooltip__panel')).toContainText('more:');
  });
});

test.describe('scroll', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`the focused chip scrolls into view along the line, and the page stays put (${dir})`, async ({
      page,
    }) => {
      await page.goto(storyUrl('chip', { dir }));
      const set = page.getByTestId('set-scroll');
      const pageScroll = await page.evaluate(() => window.scrollY);
      const lineTops = await set
        .locator('tm-chip')
        .evaluateAll(
          (chips) =>
            new Set(chips.map((chip) => Math.round(chip.getBoundingClientRect().top))).size,
        );
      expect(lineTops).toBe(1);

      // The last chip starts out of view, past the line's end.
      expect(await set.evaluate(lastChipInView)).toBe(false);
      await set.locator('.tm-chip__remove').first().focus();
      await page.keyboard.press('End');
      expect(await focusedChipText(page)).toBe('Inventory');
      expect(await set.evaluate(focusedChipInView)).toBe(true);
      expect(await page.evaluate(() => window.scrollY)).toBe(pageScroll);

      await page.keyboard.press('Home');
      expect(await focusedChipText(page)).toBe('Service');
      expect(await set.evaluate(focusedChipInView)).toBe(true);
      expect(await set.evaluate((line) => line.scrollLeft)).toBe(0);
    });
  }

  test('hands focus to the input past the end, and Backspace in the empty input comes back', async ({
    page,
  }) => {
    await page.goto(storyUrl('chip'));
    const set = page.getByTestId('set-scroll');
    const input = page.getByTestId('scroll-input');
    await set.locator('.tm-chip__remove').last().focus();
    await page.keyboard.press('ArrowRight');
    await expect(input).toBeFocused();
    await page.keyboard.press('Backspace');
    expect(await focusedChipText(page)).toBe('Inventory');
    await page.keyboard.press('Backspace');
    await expect(set.locator('tm-chip')).toHaveCount(5);
    expect(await focusedChipText(page)).toBe('Payroll');
  });
});

test.describe('targets', () => {
  test('a small chip’s remove button takes the pointer 24 px tall', async ({ page }) => {
    await page.goto(storyUrl('chip'));
    const remove = page.getByTestId('sizes').locator('tm-chip[size="sm"] .tm-chip__remove').first();
    const box = (await remove.boundingBox())!;
    expect(box.height).toBeLessThan(24);
    const hit = await page.evaluate(
      ({ x, y }) => document.elementFromPoint(x, y)?.classList.contains('tm-chip__remove') ?? false,
      { x: box.x + box.width / 2, y: box.y + box.height / 2 - 12 + 1 },
    );
    expect(hit).toBe(true);
  });
});
