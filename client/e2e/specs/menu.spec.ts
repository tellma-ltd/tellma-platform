// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-menu + tmContextMenuTrigger: real right-clicks,
 * keyboard invocation, and overlay geometry that the TestBed layer cannot
 * exercise.
 *
 * TODO(touch project): long-press open belongs to the dedicated touch
 * Playwright project (page.touchscreen requires hasTouch); cover it there
 * once that project lands.
 */

/** Positioning tolerance: below-start anchoring plus sub-pixel rounding. */
const TOLERANCE = 4;

function panel(page: Page) {
  return page.locator('.tm-menu__panel');
}

function items(page: Page) {
  return page.locator('.tm-menu__item');
}

/** Right-clicks the context area at an offset and waits for the panel. */
async function openViaRightClick(page: Page, position = { x: 60, y: 40 }) {
  const area = page.getByTestId('menu-context-area');
  await area.click({ button: 'right', position });
  await expect(panel(page)).toBeVisible();
  return area;
}

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('menu'));
});

test.describe('axe floor (menu OPEN, §10)', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`menu story with the panel open is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('menu', { theme }));
      await openViaRightClick(page);
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('context-menu invocation (tmContextMenuTrigger)', () => {
  test('right-click opens the panel at the pointer', async ({ page }) => {
    const area = page.getByTestId('menu-context-area');
    const areaBox = (await area.boundingBox())!;
    const offset = { x: 80, y: 30 };
    const clickX = areaBox.x + offset.x;
    const clickY = areaBox.y + offset.y;

    await openViaRightClick(page, offset);

    const panelBox = (await panel(page).boundingBox())!;
    expect(Math.abs(panelBox.x - clickX)).toBeLessThanOrEqual(TOLERANCE);
    expect(Math.abs(panelBox.y - clickY)).toBeLessThanOrEqual(TOLERANCE);
  });

  test('Shift+F10 on the focused area opens anchored at the ELEMENT', async ({ page }) => {
    const area = page.getByTestId('menu-context-area');
    await area.focus();
    await page.keyboard.press('Shift+F10');

    await expect(panel(page)).toBeVisible();
    const areaBox = (await area.boundingBox())!;
    const panelBox = (await panel(page).boundingBox())!;
    // Below-start of the element, not at any pointer position.
    expect(Math.abs(panelBox.x - areaBox.x)).toBeLessThanOrEqual(TOLERANCE);
    expect(Math.abs(panelBox.y - (areaBox.y + areaBox.height))).toBeLessThanOrEqual(TOLERANCE);
  });

  test('button trigger opens anchored at the button', async ({ page }) => {
    const button = page.getByTestId('menu-trigger-button');
    await button.click();

    await expect(panel(page)).toBeVisible();
    const buttonBox = (await button.boundingBox())!;
    const panelBox = (await panel(page).boundingBox())!;
    expect(Math.abs(panelBox.x - buttonBox.x)).toBeLessThanOrEqual(TOLERANCE);
    expect(Math.abs(panelBox.y - (buttonBox.y + buttonBox.height))).toBeLessThanOrEqual(TOLERANCE);
  });
});

test.describe('keyboard (§6)', () => {
  test('Escape closes and returns focus to the invoking area', async ({ page }) => {
    const area = await openViaRightClick(page);

    await page.keyboard.press('Escape');

    await expect(panel(page)).toBeHidden();
    await expect(area).toBeFocused();
    await expect(page.getByTestId('menu-action-count')).toHaveText('0'); // dismissal ≠ activation
  });

  test('arrows/Home/End move data-active through the items', async ({ page }) => {
    await openViaRightClick(page);

    await expect(items(page).first()).toHaveAttribute('data-active', 'true'); // first item on open

    await page.keyboard.press('ArrowDown');
    await expect(items(page).nth(1)).toHaveAttribute('data-active', 'true'); // Duplicate

    await page.keyboard.press('End');
    await expect(items(page).last()).toHaveAttribute('data-active', 'true'); // Select an option

    await page.keyboard.press('Home');
    await expect(items(page).first()).toHaveAttribute('data-active', 'true'); // Increment
  });

  test('typeahead jumps to the first matching item', async ({ page }) => {
    await openViaRightClick(page);

    await page.keyboard.press('d');
    await expect(items(page).nth(1)).toHaveAttribute('data-active', 'true'); // Duplicate
  });

  test('Enter activates the active item (counter increments) and closes', async ({ page }) => {
    const count = page.getByTestId('menu-action-count');
    await expect(count).toHaveText('0');
    await openViaRightClick(page);

    await page.keyboard.press('Enter'); // first item 'Increment' is active

    await expect(count).toHaveText('1');
    await expect(panel(page)).toBeHidden();
  });
});

test.describe('mouse interaction', () => {
  test('clicking a disabled item does nothing — no action, no close', async ({ page }) => {
    await openViaRightClick(page);

    // force: Playwright's actionability check refuses aria-disabled targets;
    // the real events must still be dispatched to prove the no-op.
    await panel(page).getByRole('menuitem', { name: 'Unavailable' }).click({ force: true });

    await expect(panel(page)).toBeVisible();
    await expect(page.getByTestId('menu-action-count')).toHaveText('0');
  });

  test('clicking an enabled item runs the action and closes', async ({ page }) => {
    await openViaRightClick(page);

    await panel(page).getByRole('menuitem', { name: 'Duplicate' }).click();

    await expect(panel(page)).toBeHidden();
    await expect(page.getByTestId('menu-action-count')).toHaveText('1');
  });

  test('clicking outside closes the panel', async ({ page }) => {
    await openViaRightClick(page);

    // The point must land INSIDE <body> (the CDK outside-click dispatcher
    // listens on document.body): the story page's body is shorter than the
    // viewport, and a click below it targets <html>, which body-attached
    // listeners never see.
    await page.mouse.click(700, 200);

    await expect(panel(page)).toBeHidden();
  });
});

test.describe('RTL (mirrored overlay resolution)', () => {
  test('the overlay bounding box resolves dir=rtl', async ({ page }) => {
    await page.goto(storyUrl('menu', { dir: 'rtl' }));
    await openViaRightClick(page);

    await expect(page.locator('.cdk-overlay-connected-position-bounding-box')).toHaveAttribute(
      'dir',
      'rtl',
    );
  });
});
