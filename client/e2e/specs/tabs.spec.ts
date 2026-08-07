// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-tab-group (DoD 14): active-only DOM,
 * preserveContent inert survival, the aria keyboard model in LTR and RTL,
 * explicit selection instantiating nothing while scanning, overflow
 * scrolling, and the axe/forced-colors gates.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`tabs story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('tabs', { theme }));
      await expect(page.getByTestId('basic-group')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('active-only DOM (DoD 14)', () => {
  test('exactly one live panel; deactivation destroys; preserveContent keeps inert DOM', async ({
    page,
  }) => {
    await page.goto(storyUrl('tabs'));
    const group = page.getByTestId('basic-group');
    await expect(page.getByTestId('panel-details')).toBeVisible();
    await expect(page.getByTestId('preserved-input')).toHaveCount(0);

    // Activate the preserved tab and type into it.
    await group.getByRole('tab', { name: 'Lines' }).click();
    await page.getByTestId('preserved-input').fill('draft note');
    await expect(page.getByTestId('panel-details')).toHaveCount(0); // destroyed

    // Back to details: the preserved panel stays in the DOM, inert, state kept.
    await group.getByRole('tab', { name: 'Details' }).click();
    await expect(page.getByTestId('panel-details')).toBeVisible();
    const preserved = page.getByTestId('preserved-input');
    await expect(preserved).toHaveCount(1);
    await expect(preserved).toBeHidden(); // inert panel is display:none
    await expect(preserved).toHaveValue('draft note');

    // The non-preserved rich-label tab destroys on deactivate.
    await group.getByRole('tab', { name: 'Stats (7)' }).click();
    await expect(page.getByTestId('panel-stats')).toBeVisible();
    await group.getByRole('tab', { name: 'Details' }).click();
    await expect(page.getByTestId('panel-stats')).toHaveCount(0);
  });
});

test.describe('keyboard model (DoD 14)', () => {
  for (const dir of ['ltr', 'rtl'] as const) {
    test(`arrows are direction-mapped and follow-selection selects on focus (${dir})`, async ({
      page,
    }) => {
      await page.goto(storyUrl('tabs', { dir }));
      const group = page.getByTestId('basic-group');
      await group.getByRole('tab', { name: 'Details' }).click();

      // inline-end arrow moves to the next tab; selection follows focus.
      // The disabled Audit tab stays FOCUSABLE (softDisabled — the aria
      // default) but never selects: focus rests on it, selection holds.
      const nextKey = dir === 'ltr' ? 'ArrowRight' : 'ArrowLeft';
      await page.keyboard.press(nextKey);
      await expect(page.getByTestId('selected-id')).toHaveText('lines');
      await page.keyboard.press(nextKey);
      await expect(group.getByRole('tab', { name: 'Audit' })).toBeFocused();
      await expect(page.getByTestId('selected-id')).toHaveText('lines');
      await page.keyboard.press(nextKey);
      await expect(page.getByTestId('selected-id')).toHaveText('stats');

      await page.keyboard.press('Home');
      await expect(page.getByTestId('selected-id')).toHaveText('details');
      await page.keyboard.press('End');
      await expect(page.getByTestId('selected-id')).toHaveText('stats');
    });
  }

  test('explicit mode: arrows move focus without instantiating; Enter selects', async ({
    page,
  }) => {
    await page.goto(storyUrl('tabs'));
    const group = page.getByTestId('explicit-group');
    await group.getByRole('tab', { name: 'Alpha' }).click();
    await expect(page.getByTestId('panel-a')).toBeVisible();

    await page.keyboard.press('ArrowRight');
    await page.keyboard.press('ArrowRight');
    // Focus scanned to Gamma; nothing new instantiated.
    await expect(group.getByRole('tab', { name: 'Gamma' })).toBeFocused();
    await expect(page.getByTestId('panel-a')).toBeVisible();
    await expect(page.getByTestId('panel-c')).toHaveCount(0);

    await page.keyboard.press('Enter');
    await expect(page.getByTestId('panel-c')).toBeVisible();
    await expect(page.getByTestId('panel-a')).toHaveCount(0);
  });
});

test.describe('overflow strip (DoD 14)', () => {
  test('the strip scrolls on overflow and keyboard navigation reveals the focused tab', async ({
    page,
  }) => {
    await page.goto(storyUrl('tabs'));
    const list = page.getByTestId('overflow-group').getByRole('tablist');
    const overflow = await list.evaluate((el) => el.scrollWidth > el.clientWidth);
    expect(overflow).toBe(true);

    await list.getByRole('tab', { name: 'Tab 1', exact: true }).click();
    await page.keyboard.press('End'); // Tab 20
    const lastVisible = await list
      .getByRole('tab', { name: 'Tab 20' })
      .evaluate((el, container) => {
        const cell = el.getBoundingClientRect();
        const box = (container as Element).getBoundingClientRect();
        return cell.right <= box.right + 1 && cell.left >= box.left - 1;
      }, await list.elementHandle());
    expect(lastVisible).toBe(true);
  });

  test('the wheel scrolls the strip, and the fade lifts at the end of it', async ({ page }) => {
    await page.goto(storyUrl('tabs'));
    const list = page.getByTestId('overflow-group').getByRole('tablist');
    await expect(list).toHaveClass(/tm-tab-group__list--faded/);

    // A vertical wheel over a horizontal strip: the tabs past the edge are
    // reachable with a plain mouse, not only with the keyboard.
    const box = (await list.boundingBox())!;
    const before = await list.evaluate((el) => el.scrollLeft);
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.wheel(0, 400);
    await expect
      .poll(() => list.evaluate((el) => el.scrollLeft))
      .toBeGreaterThan(before);

    // Wheel to the far end: the fade is a promise of more, so it has to stop
    // once there is none.
    for (let i = 0; i < 12; i++) {
      await page.mouse.wheel(0, 400);
    }
    await expect(list).not.toHaveClass(/tm-tab-group__list--faded/);
  });
});

test.describe('forced-colors gate', () => {
  test('the selected indicator survives via system colors', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('tabs'));
    const selected = page.getByTestId('basic-group').locator('[aria-selected="true"]').first();
    // Forced-colors strips box-shadows — the indicator is a real border there.
    const border = await selected.evaluate(
      (el) => getComputedStyle(el).borderBottomStyle + '|' + getComputedStyle(el).borderBottomWidth,
    );
    expect(border).toMatch(/^solid\|[1-9]/);
  });

  test('the focus ring survives as a real outline', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('tabs'));
    const group = page.getByTestId('basic-group');
    // Arrow from a clicked tab so the move is a KEYBOARD one (:focus-visible).
    await group.getByRole('tab', { name: 'Details' }).click();
    await page.keyboard.press('ArrowRight');

    // The box-shadow ring is stripped here, so the transparent outline the
    // rule also declares is the whole indicator: `outline: none` would leave
    // a keyboard user with nothing at all.
    const ring = await page.evaluate(() => {
      const el = document.activeElement as HTMLElement;
      const style = getComputedStyle(el);
      return [
        el.getAttribute('role'),
        el.matches(':focus-visible'),
        style.outlineStyle,
        style.outlineWidth,
      ].join('|');
    });
    expect(ring).toMatch(/^tab\|true\|solid\|[1-9]/);
  });
});
