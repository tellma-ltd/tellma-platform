// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tmButton's tm-icon children, link buttons and
 * disabledReason: the icon box at each button size and hidden while
 * pending; a link keeping its native link behaviour; a reason button that
 * keeps focus, swallows activation and shows its reason on hover and on
 * keyboard focus; and the axe floor of a reason button with its tooltip
 * shown, in both themes and directions. The tap path runs on the touch
 * project (tooltip-touch.spec.ts).
 */

const REASON = 'Needs the “Customers: Create and edit” permission.';

/** The visible tooltip surface. */
function tooltip(page: Page): ReturnType<Page['locator']> {
  return page.locator('.tm-tooltip__panel');
}

/** Moves keyboard focus onto the reason button (Tab from the link before it). */
async function tabToReason(page: Page): Promise<void> {
  await page.getByTestId('link-secondary').focus();
  await page.keyboard.press('Tab');
  await expect(page.getByTestId('reason-btn')).toBeFocused();
}

test.describe('tm-icon children', () => {
  test('each size gives its tm-icon the button icon size, and the icon never takes the click', async ({
    page,
  }) => {
    await page.goto(storyUrl('button'));
    const expected = await page.evaluate(() =>
      Number.parseFloat(
        getComputedStyle(document.documentElement).getPropertyValue('--button-icon-size'),
      ),
    );
    for (const size of ['sm', 'md', 'lg']) {
      const icon = page.getByTestId(`tm-icon-${size}`).locator('tm-icon');
      const box = (await icon.boundingBox())!;
      expect(box.width).toBeCloseTo(expected, 1);
      expect(box.height).toBeCloseTo(expected, 1);
      await expect(icon).toHaveCSS('pointer-events', 'none');
      await expect(icon).toHaveCSS('flex-shrink', '0');
      // The glyph fills the box.
      const glyph = (await icon.locator('svg').boundingBox())!;
      expect(glyph.width).toBeCloseTo(expected, 1);
    }
  });

  test('pending hides a tm-icon child and keeps the box', async ({ page }) => {
    await page.goto(storyUrl('button'));
    const button = page.getByTestId('tm-icon-pending');
    const icon = button.locator('tm-icon');
    await expect(icon).toBeVisible();
    const before = (await button.boundingBox())!;

    await page.getByTestId('tm-icon-pending-toggle').click();
    await expect(button).toHaveAttribute('aria-busy', 'true');
    await expect(icon).toBeHidden();
    await expect(button.locator('.tm-button__spinner')).toBeVisible();
    const during = (await button.boundingBox())!;
    expect(during.width).toBeCloseTo(before.width, 1);
    expect(during.height).toBeCloseTo(before.height, 1);
  });
});

test.describe('link buttons', () => {
  test('a link wears the button look and stays a link', async ({ page }) => {
    await page.goto(storyUrl('button'));
    const link = page.getByTestId('link-primary');
    await expect(link).toHaveRole('link');
    await expect(link).not.toHaveAttribute('type');
    await expect(link).toHaveClass(/tm-button--primary/);
    await expect(link).toHaveCSS('text-decoration-line', 'none');
    const primary = page.getByTestId('variants').locator('.tm-button--primary');
    expect((await link.boundingBox())!.height).toBeCloseTo(
      (await primary.boundingBox())!.height,
      1,
    );
    await link.click();
    await expect(page).toHaveURL(/#new-record$/);
  });
});

test.describe('disabled with a reason', () => {
  test('keeps focus, is described by the reason, and swallows every activation', async ({
    page,
  }) => {
    await page.goto(storyUrl('button'));
    const button = page.getByTestId('reason-btn');
    await expect(button).toHaveAttribute('aria-disabled', 'true');
    // Not natively disabled (Playwright's enabled check reads aria-disabled
    // too, so the property is read directly).
    expect(await button.evaluate((element) => (element as HTMLButtonElement).disabled)).toBe(false);
    await expect(button).toHaveAccessibleDescription(REASON);

    // A real click on it: forced past Playwright's own aria-disabled gate.
    await button.click({ force: true });
    await tabToReason(page);
    await page.keyboard.press('Enter');
    await page.keyboard.press(' ');
    await expect(page.getByTestId('reason-click-count')).toHaveText('0');

    // Without the reason, the same button activates.
    await page.getByTestId('reason-toggle').click();
    await expect(button).not.toHaveAttribute('aria-disabled');
    await button.click();
    await expect(page.getByTestId('reason-click-count')).toHaveText('1');
  });

  test('hover shows the reason as a tooltip', async ({ page }) => {
    await page.goto(storyUrl('button'));
    await page.getByTestId('reason-btn').hover();
    await expect(tooltip(page)).toHaveText(REASON);
    await page.mouse.move(0, 0);
    await expect(tooltip(page)).toHaveCount(0);
  });

  test('keyboard focus shows the reason at once', async ({ page }) => {
    await page.goto(storyUrl('button'));
    await tabToReason(page);
    await expect(tooltip(page)).toHaveText(REASON);
    await page.keyboard.press('Escape');
    await expect(tooltip(page)).toHaveCount(0);
    await expect(page.getByTestId('reason-btn')).toBeFocused();
  });

  test('a tmTooltip on the same button shows its label, then the reason', async ({ page }) => {
    await page.goto(storyUrl('button'));
    const button = page.getByTestId('reason-icon-btn');
    await button.hover();
    // The icon-only button keeps its only visible label, above the reason.
    await expect(tooltip(page)).toHaveText(`Delete ${REASON}`);
    const lines = await tooltip(page).evaluate((element) => {
      const range = document.createRange();
      range.selectNodeContents(element);
      return new Set([...range.getClientRects()].map((rect) => Math.round(rect.top))).size;
    });
    expect(lines).toBeGreaterThanOrEqual(2);
    await expect(button).toHaveAccessibleName('Delete');
    await expect(button).toHaveAccessibleDescription(REASON);

    await page.mouse.move(0, 0);
    await expect(tooltip(page)).toHaveCount(0);
    await page.getByTestId('reason-toggle').click();
    await button.hover();
    await expect(tooltip(page)).toHaveText('Delete');
  });
});

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`a reason button with its tooltip shown is axe-clean (${theme}, ${dir.toUpperCase()})`, async ({
        page,
      }) => {
        await page.goto(storyUrl('button', { theme, dir }));
        await tabToReason(page);
        await expect(tooltip(page)).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});
