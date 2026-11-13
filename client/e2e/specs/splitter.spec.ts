// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-splitter: the axe floor in both themes and
 * directions, the keyboard in both directions and across a live switch to
 * Arabic, a mouse drag following the pointer and stopping at the clamp, the 24 px hit area, and the clamp
 * re-applied when the container shrinks. The touch drag runs in the touch
 * project's own spec.
 */

/** The horizontal splitter's bar. */
function bar(page: Page) {
  return page.getByTestId('splitter-horizontal').getByRole('separator');
}

// A key or a pointer move sets the size at once, but the pane and the
// story's readout change only on the following render; on a starved runner
// that render can still be queued when the action's promise resolves. Every
// read below therefore waits for the app to be stable first.

/** The list pane's rendered width, once every pending render has run. */
async function listWidth(page: Page): Promise<number> {
  await appStable(page);
  return (await page.getByTestId('pane-list').boundingBox())!.width;
}

/** The list size the story reports (the splitter's `size`), once every pending render has run. */
async function reportedListSize(page: Page): Promise<number> {
  await appStable(page);
  const text = await page.getByTestId('list-size').textContent();
  const match = /^List: (\d+)$/.exec(text ?? '');
  expect(match, `the list size reads "${text}"`).not.toBeNull();
  return Number(match![1]);
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

// The app switches English to Arabic while the splitter is on screen: the
// direction is read live, at each key and each drag, never cached from the
// first render.
test('a live English to Arabic switch remaps the arrows and the drag', async ({ page }) => {
  await page.goto(storyUrl('splitter'));
  const separator = bar(page);
  await separator.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('list-size')).toHaveText('List: 436');

  await page.getByTestId('lang-ar').click();
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(separator).toHaveAccessibleName('تغيير حجم اللوحتين');

  // Right-to-left, the list is on the right: ArrowRight shrinks it.
  await separator.focus();
  await page.keyboard.press('ArrowRight');
  await expect(page.getByTestId('list-size')).toHaveText('List: 420');
  expect(await listWidth(page)).toBe(420);

  // Dragging the bar left now grows the list.
  const box = (await separator.boundingBox())!;
  const x = box.x + box.width / 2;
  const y = box.y + box.height / 2;
  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x - 60, y, { steps: 4 });
  await page.mouse.up();
  expect(await listWidth(page)).toBeCloseTo(480, 0);
});

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
  const separator = bar(page);
  await separator.focus();
  await page.keyboard.press('End');
  const asked = await reportedListSize(page);
  const minShareBefore = await separator.getAttribute('aria-valuemin');

  await page.setViewportSize({ width: 700, height: 800 });
  const container = await page
    .getByTestId('splitter-horizontal')
    .evaluate((element) => element.clientWidth);
  // The story's clamp in the narrower container: at least 240, at most 66 %
  // and at most what leaves the details 200 px beside the 7 px bar.
  const clamped = Math.max(240, Math.min(asked, 0.66 * container, container - 200 - 7));
  const share = (px: number): string => String(Math.round((px / container) * 100));
  // The 240 px minimum is a larger share of the narrower container, so
  // seeing the new minimum proves the resize's own render (the observer's)
  // has run; the value is then the clamped share.
  expect(share(240)).not.toBe(minShareBefore);
  await expect(separator).toHaveAttribute('aria-valuemin', share(240));
  await expect(separator).toHaveAttribute('aria-valuenow', share(clamped));

  // The pane is clamped, and the size asked for survived that render.
  expect(Math.abs((await listWidth(page)) - clamped)).toBeLessThanOrEqual(1);
  await expect(page.getByTestId('list-size')).toHaveText(`List: ${asked}`);
});
