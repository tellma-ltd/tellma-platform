// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-splitter: the axe floor in both themes and
 * directions, the keyboard in both directions, a mouse drag following the
 * pointer and stopping at the clamp, the 24 px hit area, and the clamp
 * re-applied when the container shrinks. The touch drag runs in the touch
 * project's own spec.
 */

/** The horizontal splitter's bar. */
function bar(page: Page) {
  return page.getByTestId('splitter-horizontal').getByRole('separator');
}

/** The list pane's rendered width. */
async function listWidth(page: Page): Promise<number> {
  return (await page.getByTestId('pane-list').boundingBox())!.width;
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`splitter story is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('splitter', { theme, dir }));
        await expect(bar(page)).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test('the bar is a named separator reporting the list share', async ({ page }) => {
  await page.goto(storyUrl('splitter'));
  const separator = bar(page);
  await expect(separator).toHaveAccessibleName('Resize panels');
  await expect(separator).toHaveAttribute('aria-orientation', 'vertical');
  await expect(separator).toHaveAttribute('aria-controls', /.+/);
  const container = (await page.getByTestId('splitter-horizontal').boundingBox())!.width;
  await expect(separator).toHaveAttribute(
    'aria-valuenow',
    String(Math.round((420 / container) * 100)),
  );
  await expect(page.getByTestId('splitter-vertical').getByRole('separator')).toHaveAccessibleName(
    'Resize the preview',
  );
});

for (const dir of ['ltr', 'rtl'] as const) {
  test(`the arrows move the bar the way they point (${dir})`, async ({ page }) => {
    await page.goto(storyUrl('splitter', { dir }));
    await bar(page).focus();
    await page.keyboard.press('ArrowRight');
    // In a right-to-left page the list is on the right: moving the bar
    // right shrinks it.
    const moved = dir === 'ltr' ? 436 : 404;
    await expect(page.getByTestId('list-size')).toHaveText(`List: ${moved}`);
    expect(await listWidth(page)).toBe(moved);
    await page.keyboard.press('Shift+ArrowLeft');
    await expect(page.getByTestId('list-size')).toHaveText(
      `List: ${moved + (dir === 'ltr' ? -64 : 64)}`,
    );
    await page.keyboard.press('Home');
    await expect(page.getByTestId('list-size')).toHaveText('List: 240');
  });

  test(`a mouse drag follows the pointer and stops at the clamp (${dir})`, async ({ page }) => {
    await page.goto(storyUrl('splitter', { dir }));
    const box = (await bar(page).boundingBox())!;
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    const sign = dir === 'ltr' ? 1 : -1;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + sign * 60, y, { steps: 4 });
    expect(await listWidth(page)).toBeCloseTo(480, 0);
    // Far past the end: the list stops at 66 % of the container.
    await page.mouse.move(x + sign * 2000, y, { steps: 4 });
    await page.mouse.up();
    // The fraction is of the container's content box, inside its border.
    const container = await page
      .getByTestId('splitter-horizontal')
      .evaluate((element) => element.clientWidth);
    expect(Math.abs((await listWidth(page)) - 0.66 * container)).toBeLessThanOrEqual(1);
  });
}

test('the bar takes the pointer 24 px across, with the resize cursor', async ({ page }) => {
  await page.goto(storyUrl('splitter'));
  const separator = bar(page);
  const box = (await separator.boundingBox())!;
  expect(box.width).toBe(7);
  const center = box.x + box.width / 2;
  const y = box.y + box.height / 4;
  for (const offset of [-11, 11]) {
    const hit = await page.evaluate(
      ([px, py]) => document.elementFromPoint(px, py)?.closest('[role=separator]') !== null,
      [center + offset, y],
    );
    expect(hit, `offset ${offset}`).toBe(true);
  }
  expect(await separator.evaluate((element) => getComputedStyle(element).cursor)).toBe(
    'col-resize',
  );
});

test('a shrinking container clamps the list and keeps the size asked for', async ({ page }) => {
  await page.goto(storyUrl('splitter'));
  await bar(page).focus();
  await page.keyboard.press('End');
  const asked = Number((await page.getByTestId('list-size').textContent())!.replace('List: ', ''));
  await page.setViewportSize({ width: 700, height: 800 });
  const container = (await page.getByTestId('splitter-horizontal').boundingBox())!.width;
  await expect.poll(() => listWidth(page)).toBeLessThan(asked);
  expect(await listWidth(page)).toBeLessThanOrEqual(Math.max(240, container - 200 - 7) + 0.5);
  await expect(page.getByTestId('list-size')).toHaveText(`List: ${asked}`);
});
