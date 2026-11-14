// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * The tm-entity-multi-picker battery over the entity-picker story's
 * "Several suppliers" section: keyboard-only add, toggle-remove and chip
 * removal in both directions, the one-line fixed height with 0, 1 and 30
 * chips, the chip painted in the frame its pick lands in, and axe with
 * chips and an open panel.
 *
 * The section's multi-picker searches synchronously over the story's
 * directory ('Alice Green', 'Bob Stone', 'Carol White', …) plus 24
 * "Supplier NN" entries; "Choose 30", "Choose 1" and "Clear" set the value.
 */

/** The field-wrapped multi-picker. */
function picker(page: Page): Locator {
  return page.getByTestId('multi-picker');
}

/** The multi-picker's search input. */
function input(page: Page): Locator {
  return picker(page).locator('.tm-entity-picker__input');
}

/** The chip texts, in order. */
function chips(page: Page): Locator {
  return picker(page).locator('tm-chip .tm-chip__text');
}

/** The open dropdown's entity rows. */
function options(page: Page): Locator {
  return page.locator('.tm-entity-picker__option:not(.tm-entity-picker__action)');
}

/** The field's bordered box: the line whose height must never change. */
function fieldBox(page: Page): Locator {
  return page.getByTestId('multi-ff').locator('.tm-form-field__box');
}

/** The accessible name of the focused element. */
async function focusedName(page: Page): Promise<string | null> {
  return page.evaluate(() => {
    const active = document.activeElement;
    return active?.getAttribute('aria-label') ?? active?.getAttribute('id') ?? null;
  });
}

/** Whether the multi-picker's input holds focus. */
async function inputFocused(page: Page): Promise<boolean> {
  return input(page).evaluate((element) => element === document.activeElement);
}

/** The story's chosen ids, read once the app is stable. */
async function chosenIds(page: Page): Promise<number[]> {
  await appStable(page);
  const text = await page.getByTestId('model-json').textContent();
  return (JSON.parse(text ?? '{}') as { multiIds: number[] }).multiIds;
}

/**
 * A box measured after the frame that follows the last change has run its
 * animation callbacks — stable geometry, never a fixed sleep. In document
 * coordinates, so a click that scrolled its button into view moves nothing.
 */
async function boxAfterFrame(
  locator: Locator,
): Promise<{ x: number; y: number; width: number; height: number }> {
  await appStable(locator.page());
  return locator.evaluate(
    (element) =>
      new Promise((resolve) => {
        requestAnimationFrame(() => {
          const rect = element.getBoundingClientRect();
          resolve({
            x: rect.x + window.scrollX,
            y: rect.y + window.scrollY,
            width: rect.width,
            height: rect.height,
          });
        });
      }),
  );
}

/** Types a query and commits its highlighted first row with Enter. */
async function addByKeyboard(page: Page, query: string): Promise<void> {
  await input(page).fill(query);
  await expect(options(page).first()).toBeVisible();
  await appStable(page);
  await input(page).press('Enter');
  await expect(input(page)).toHaveValue('');
}

test.describe('keyboard paths', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`keyboard-only add, toggle-remove and chip removal (${dir})`, async ({ page }) => {
      await page.goto(storyUrl('entity-picker', { dir }));
      const backward = dir === 'ltr' ? 'ArrowLeft' : 'ArrowRight';
      const forward = dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft';
      await input(page).focus();

      // Add three, keyboard only; the panel stays open between picks.
      await addByKeyboard(page, 'Alice');
      await addByKeyboard(page, 'Bob');
      await addByKeyboard(page, 'Carol');
      await expect(chips(page)).toHaveText(['Alice Green', 'Bob Stone', 'Carol White']);
      expect(await chosenIds(page)).toEqual([3, 5, 6]);
      await expect(page.locator('.tm-entity-picker__panel')).toBeVisible();

      // The chips sit at the inline start: left of the input in LTR, right in RTL.
      const chipBox = await boxAfterFrame(picker(page).locator('tm-chip').first());
      const inputBox = await boxAfterFrame(input(page));
      if (dir === 'ltr') {
        expect(chipBox.x).toBeLessThan(inputBox.x);
      } else {
        expect(chipBox.x).toBeGreaterThan(inputBox.x);
      }

      // The backward arrow at the caret's start enters the chips at the last
      // one; the arrows walk them, and forward past the last returns.
      await input(page).press('Escape');
      await input(page).press(backward);
      expect(await focusedName(page)).toBe('Remove Carol White');
      await page.keyboard.press(backward);
      expect(await focusedName(page)).toBe('Remove Bob Stone');
      await page.keyboard.press(forward);
      await page.keyboard.press(forward);
      expect(await inputFocused(page)).toBe(true);

      // Backspace twice: to the last chip, then it goes; focus falls back to
      // the chip before it.
      await input(page).press('Backspace');
      expect(await focusedName(page)).toBe('Remove Carol White');
      await page.keyboard.press('Backspace');
      await expect(chips(page)).toHaveText(['Alice Green', 'Bob Stone']);
      await appStable(page);
      expect(await focusedName(page)).toBe('Remove Bob Stone');

      // Toggle-remove: Enter on a chosen row of the browse list takes it off,
      // and Tab out of the field does not put it back.
      await input(page).focus();
      await input(page).press('ArrowDown');
      await expect(options(page).first()).toBeVisible();
      await appStable(page);
      // The browse list opens with nothing highlighted, so Bob Stone's row is
      // (its index + 1) ArrowDown presses away. The picker steps from its own
      // state, not from the rows on screen, so the presses need no wait in
      // between; the highlight is painted by a later render, which the
      // assertions below wait for. (Reading the highlight right after each
      // press reads the frame before it, and can step past the row.)
      const highlighted = page.locator('.tm-entity-picker__option[data-active="true"]');
      await expect(highlighted).toHaveCount(0);
      const labels = (
        await options(page).locator('.tm-entity-picker__option-label').allTextContents()
      ).map((label) => label.trim());
      const bob = labels.indexOf('Bob Stone');
      expect(bob).toBeGreaterThanOrEqual(0);
      for (let step = 0; step <= bob; step++) {
        await input(page).press('ArrowDown');
      }
      await expect(options(page).nth(bob)).toHaveAttribute('data-active', 'true');
      await expect(highlighted.locator('.tm-entity-picker__option-label')).toHaveText('Bob Stone');
      await input(page).press('Enter');
      await expect(chips(page)).toHaveText(['Alice Green']);
      await input(page).press('Tab');
      await appStable(page);
      await expect(chips(page)).toHaveText(['Alice Green']);
      expect(await chosenIds(page)).toEqual([3]);

      // Delete on the last chip standing returns focus to the input.
      await input(page).press('Escape');
      await input(page).press(backward);
      expect(await focusedName(page)).toBe('Remove Alice Green');
      await page.keyboard.press('Delete');
      await expect(chips(page)).toHaveCount(0);
      await appStable(page);
      expect(await inputFocused(page)).toBe(true);
      expect(await chosenIds(page)).toEqual([]);
      expect(Number(await page.getByTestId('multi-removed').textContent())).toBe(3);
    });
  }

  test('the chips are one tab stop before the input', async ({ page }) => {
    await page.goto(storyUrl('entity-picker'));
    await page.getByTestId('multi-fill').click();
    await expect(chips(page)).toHaveCount(30);
    await page.getByTestId('multi-clear').focus();
    await page.keyboard.press('Tab');
    expect(await focusedName(page)).toBe('Remove Adam Brown');
    await page.keyboard.press('Tab');
    expect(await inputFocused(page)).toBe(true);
    await page.keyboard.press('Shift+Tab');
    expect(await focusedName(page)).toBe('Remove Adam Brown');
  });
});

test.describe('stable layout', () => {
  test('the field is one line of identical height with 0, 1 and 30 chips', async ({ page }) => {
    await page.goto(storyUrl('entity-picker'));
    const below = page.getByTestId('multi-picker-bare');

    const empty = await boxAfterFrame(fieldBox(page));
    const belowEmpty = await boxAfterFrame(below);

    await page.getByTestId('multi-one').click();
    await expect(chips(page)).toHaveCount(1);
    const one = await boxAfterFrame(fieldBox(page));
    const badgeOne = await boxAfterFrame(picker(page).locator('.tm-entity-multi-picker__count'));

    await page.getByTestId('multi-fill').click();
    await expect(chips(page)).toHaveCount(30);
    const thirty = await boxAfterFrame(fieldBox(page));
    const badgeThirty = await boxAfterFrame(picker(page).locator('.tm-entity-multi-picker__count'));
    const belowThirty = await boxAfterFrame(below);

    expect(one.height).toBe(empty.height);
    expect(thirty.height).toBe(empty.height);
    expect(thirty.width).toBe(empty.width);
    // Nothing below the field moved.
    expect(belowThirty.y).toBe(belowEmpty.y);
    // The badge is as wide at 30 as at 1, and shows the total.
    expect(badgeThirty.width).toBe(badgeOne.width);
    await expect(picker(page).locator('.tm-entity-multi-picker__count-value')).toHaveText('30');
    // The chips scroll in their one line, and the input keeps room to type.
    const scrolls = await picker(page)
      .locator('tm-chip-set')
      .evaluate((element) => element.scrollWidth > element.clientWidth);
    expect(scrolls).toBe(true);
    // The search (the input with its magnifier) keeps its 80px floor.
    const searchBox = await boxAfterFrame(picker(page).locator('.tm-entity-multi-picker__search'));
    expect(searchBox.width).toBeGreaterThanOrEqual(80);
    // Every chip sits on the one line.
    const tops = await picker(page)
      .locator('tm-chip')
      .evaluateAll((elements) => elements.map((element) => element.getBoundingClientRect().top));
    expect(new Set(tops).size).toBe(1);

    await page.getByTestId('multi-clear').click();
    await expect(chips(page)).toHaveCount(0);
    expect((await boxAfterFrame(fieldBox(page))).height).toBe(empty.height);
  });

  test('the last chip scrolls into view when the keyboard reaches it', async ({ page }) => {
    await page.goto(storyUrl('entity-picker'));
    await page.getByTestId('multi-fill').click();
    await expect(chips(page)).toHaveCount(30);
    await input(page).focus();
    await input(page).press('Backspace');
    expect(await focusedName(page)).toBe('Remove Supplier 23');
    const set = await boxAfterFrame(picker(page).locator('tm-chip-set'));
    const last = await boxAfterFrame(picker(page).locator('tm-chip').last());
    expect(last.x).toBeGreaterThanOrEqual(set.x - 1);
    expect(last.x + last.width).toBeLessThanOrEqual(set.x + set.width + 1);
  });
});

test.describe('instant feedback', () => {
  test('a picked chip is in the page before the frame after the pick paints', async ({ page }) => {
    await page.goto(storyUrl('entity-picker'));
    await input(page).fill('Bob');
    await expect(options(page)).toHaveCount(1);
    await appStable(page);
    // Registered after the picker's own handlers have run (a window
    // listener in the bubble phase): the animation-frame callback it
    // queues runs before that frame paints, after any render the pick
    // scheduled for it.
    await page.evaluate(() => {
      const state = window as unknown as { tmChipInFrame?: boolean | null };
      state.tmChipInFrame = null;
      window.addEventListener(
        'click',
        () => {
          requestAnimationFrame(() => {
            state.tmChipInFrame = Array.from(
              document.querySelectorAll('[data-testid="multi-picker"] tm-chip .tm-chip__text'),
            ).some((text) => text.textContent?.trim() === 'Bob Stone');
          });
        },
        { once: true },
      );
    });
    await options(page).first().click();
    await expect
      .poll(() =>
        page.evaluate(
          () => (window as unknown as { tmChipInFrame?: boolean | null }).tmChipInFrame,
        ),
      )
      .toBe(true);
  });
});

test.describe('axe', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`no violations with chips and an open panel (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('entity-picker', { theme, dir }));
        await page.getByTestId('multi-one').click();
        await input(page).fill('A');
        await expect(options(page).first()).toBeVisible();
        // A chosen row (aria-selected in a multiselectable listbox) is on screen.
        await expect(page.locator('.tm-entity-picker__option[aria-selected="true"]')).toHaveCount(
          1,
        );
        await expectNoAxeViolations(page);
      });
    }
  }
});
