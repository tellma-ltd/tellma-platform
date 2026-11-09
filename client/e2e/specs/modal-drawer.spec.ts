// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for the modal drawer (placement 'end') and the alert
 * dialog: the drawer's edge and width at phone, tablet and desktop widths
 * in both directions, a live direction flip moving an open drawer to the
 * other edge, the drawer rendering in the click's frame with no motion,
 * the shell's focus trap and Escape, and the axe floor of both in both
 * themes and directions.
 */

/** The drawer's pane (the box the geometry is read from). */
function pane(page: Page): ReturnType<Page['locator']> {
  return page.locator('.tm-modal-panel--end');
}

/** The viewport's visible width (excluding a classic scrollbar). */
function clientWidth(page: Page): Promise<number> {
  return page.evaluate(() => document.documentElement.clientWidth);
}

/** Opens the md drawer and waits for its shell. */
async function openDrawer(page: Page, testId = 'open-drawer'): Promise<void> {
  await page.getByTestId(testId).click();
  await expect(page.locator('tm-modal-shell.tm-modal--end')).toBeVisible();
}

/** The drawer's box. */
async function drawerBox(page: Page): Promise<{ x: number; width: number; y: number; height: number }> {
  const box = await pane(page).boundingBox();
  expect(box).not.toBeNull();
  return box!;
}

test.describe('drawer geometry', () => {
  const viewports = [
    { width: 390, height: 844, expected: 'full' },
    { width: 768, height: 1024, expected: 480 },
    { width: 1440, height: 900, expected: 480 },
  ] as const;

  for (const viewport of viewports) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`docks to the inline-end edge at ${viewport.width}px in ${dir.toUpperCase()}`, async ({
        page,
      }) => {
        await page.setViewportSize({ width: viewport.width, height: viewport.height });
        await page.goto(storyUrl('modal', { dir }));
        await openDrawer(page);
        const width = await clientWidth(page);
        const box = await drawerBox(page);

        // Full height, at the top.
        expect(Math.abs(box.y)).toBeLessThanOrEqual(1);
        expect(Math.abs(box.height - viewport.height)).toBeLessThanOrEqual(1);
        // The md width, or the full width below the sm breakpoint.
        const expectedWidth = viewport.expected === 'full' ? width : viewport.expected;
        expect(Math.abs(box.width - expectedWidth)).toBeLessThanOrEqual(1);
        // Right in LTR, left in RTL.
        if (dir === 'ltr') {
          expect(Math.abs(box.x + box.width - width)).toBeLessThanOrEqual(1);
        } else {
          expect(Math.abs(box.x)).toBeLessThanOrEqual(1);
        }
      });
    }
  }

  test('the sizes pick the drawer widths, and every size is full width on a phone', async ({
    page,
  }) => {
    await page.goto(storyUrl('modal'));
    for (const [testId, width] of [
      ['open-drawer-sm', 360],
      ['open-drawer-lg', 720],
    ] as const) {
      await openDrawer(page, testId);
      expect(Math.abs((await drawerBox(page)).width - width)).toBeLessThanOrEqual(1);
      await page.keyboard.press('Escape');
      await expect(pane(page)).toHaveCount(0);
    }

    await page.setViewportSize({ width: 390, height: 844 });
    await openDrawer(page, 'open-drawer-lg');
    expect(Math.abs((await drawerBox(page)).width - (await clientWidth(page)))).toBeLessThanOrEqual(
      1,
    );
  });

  test('a live direction flip moves an open drawer to the other edge', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    await openDrawer(page);
    const width = await clientWidth(page);
    const atEnd = async (): Promise<boolean> => {
      const box = await drawerBox(page);
      return Math.abs(box.x + box.width - width) <= 1;
    };
    expect(await atEnd()).toBe(true);

    await page.evaluate(() => (document.documentElement.dir = 'rtl'));
    await expect.poll(async () => Math.abs((await drawerBox(page)).x)).toBeLessThanOrEqual(1);
    await expect(page.locator('tm-modal-shell')).toHaveCSS('direction', 'rtl');

    await page.evaluate(() => (document.documentElement.dir = 'ltr'));
    await expect.poll(atEnd).toBe(true);
  });

  test('the drawer is present in its final geometry one frame after the click', async ({
    page,
  }) => {
    await page.goto(storyUrl('modal'));
    const state = await page.evaluate(
      () =>
        new Promise<{ present: boolean; titled: boolean; animations: number; right: number }>(
          (resolve) => {
            (document.querySelector('[data-testid="open-drawer"]') as HTMLElement).click();
            requestAnimationFrame(() => {
              const drawer = document.querySelector('.tm-modal-panel--end');
              const container = drawer?.querySelector('cdk-dialog-container');
              resolve({
                present: drawer !== null,
                titled: drawer?.querySelector('.tm-modal__title')?.textContent === 'Activity',
                animations: container?.getAnimations({ subtree: true }).length ?? -1,
                right: drawer?.getBoundingClientRect().right ?? -1,
              });
            });
          },
        ),
    );
    expect(state.present).toBe(true);
    expect(state.titled).toBe(true);
    // No slide or fade: nothing animates in.
    expect(state.animations).toBe(0);
    expect(Math.abs(state.right - (await clientWidth(page)))).toBeLessThanOrEqual(1);
  });

  test('the drawer keeps its box while its content loads', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    await openDrawer(page);
    const before = await drawerBox(page);
    await page.getByTestId('drawer-load').click();
    await expect(page.getByTestId('drawer-entry')).toHaveCount(50);
    const after = await drawerBox(page);
    expect(after).toEqual(before);
  });

  test('focus is trapped in the drawer, Escape closes it, and focus returns', async ({
    page,
  }) => {
    await page.goto(storyUrl('modal'));
    await openDrawer(page);
    const inDrawer = () =>
      page.evaluate(() => {
        const container = document.querySelector('.tm-modal-panel--end cdk-dialog-container');
        return container !== null && container.contains(document.activeElement);
      });
    await expect.poll(inDrawer).toBe(true);
    for (let i = 0; i < 4; i += 1) {
      await page.keyboard.press('Tab');
      expect(await inDrawer()).toBe(true);
    }
    await page.keyboard.press('Escape');
    await expect(pane(page)).toHaveCount(0);
    await expect(page.getByTestId('modal-result')).toHaveText('{"via":"escape"}');
    await expect(page.getByTestId('open-drawer')).toBeFocused();
  });
});

test.describe('alert dialog', () => {
  test('is an alertdialog described by its message', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-alert').click();
    const dialog = page.getByRole('alertdialog', { name: 'Discard the draft?' });
    await expect(dialog).toBeVisible();
    await expect(dialog).toHaveAccessibleDescription(
      'Discard the draft of INV-00042? Unsaved changes will be lost.',
    );
    await page.getByTestId('alert-discard').click();
    await expect(page.getByTestId('modal-result')).toHaveText('{"via":"api","value":true}');
  });
});

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`an open drawer is axe-clean (${theme}, ${dir.toUpperCase()})`, async ({ page }) => {
        await page.goto(storyUrl('modal', { theme, dir }));
        await openDrawer(page);
        await expectNoAxeViolations(page);
      });

      test(`an open alert dialog is axe-clean (${theme}, ${dir.toUpperCase()})`, async ({
        page,
      }) => {
        await page.goto(storyUrl('modal', { theme, dir }));
        await page.getByTestId('open-alert').click();
        await expect(page.getByRole('alertdialog')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});
