// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for the production tm-select (DoD 4/5/6/7 browser half).
 * Supersedes the stage-3 probe specs; the real-mouse specs remain the
 * standing guard for angular/components#32504.
 */

async function openSelect(page: Page, testid: string) {
  const trigger = page.getByTestId(testid).locator('.tm-select__trigger');
  await trigger.click();
  const panel = page.locator('.tm-select__panel');
  await expect(panel).toBeVisible();
  return { trigger, panel };
}

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('select'));
});

test.describe('axe floor (incl. the OPEN panel, §10)', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`select story with open panel is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('select', { theme }));
      await openSelect(page, 'select-country');
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('mouse interaction (angular/components#32504 guard, real events)', () => {
  test('clicking an option commits and closes', async ({ page }) => {
    const { trigger, panel } = await openSelect(page, 'select-country');

    await panel.getByRole('option', { name: 'Ethiopia' }).click();

    await expect(panel).toBeHidden();
    await expect(trigger).toContainText('Ethiopia');
    await expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });

  test('clicking outside closes the panel', async ({ page }) => {
    const { panel } = await openSelect(page, 'select-country');
    await page.mouse.click(600, 400);
    await expect(panel).toBeHidden();
  });

  test('clicking the trigger toggles open and closed', async ({ page }) => {
    const trigger = page.getByTestId('select-country').locator('.tm-select__trigger');
    const panel = page.locator('.tm-select__panel');

    await trigger.click();
    await expect(panel).toBeVisible();
    await trigger.click();
    await expect(panel).toBeHidden();
  });
});

test.describe('overlay composition (§3.4/DoD 6)', () => {
  test('the top-layer panel escapes the overflow:hidden ancestor', async ({ page }) => {
    const clipbox = page.getByTestId('ff-country').locator('..'); // .clipbox
    const clipBounds = (await clipbox.boundingBox())!;
    const { panel } = await openSelect(page, 'select-country');
    const panelBounds = (await panel.boundingBox())!;

    expect(panelBounds.y + panelBounds.height).toBeGreaterThan(clipBounds.y + clipBounds.height);
    await expect(panel.getByRole('option', { name: 'Oman' })).toBeVisible();
  });

  test('the panel flips above a bottom-pinned trigger', async ({ page }) => {
    const { trigger, panel } = await openSelect(page, 'select-flip');
    const triggerBounds = (await trigger.boundingBox())!;
    const panelBounds = (await panel.boundingBox())!;
    expect(panelBounds.y + panelBounds.height).toBeLessThanOrEqual(triggerBounds.y + 1);
  });

  test('the ARIA id chain resolves across the portal', async ({ page }) => {
    const { trigger } = await openSelect(page, 'select-country');

    const controls = await trigger.getAttribute('aria-controls');
    expect(controls).toBeTruthy();
    await expect(page.locator(`[id="${controls}"]`)).toHaveRole('listbox');

    const initialActive = await trigger.getAttribute('aria-activedescendant');
    await trigger.press('ArrowDown');
    if (initialActive) {
      await expect(trigger).not.toHaveAttribute('aria-activedescendant', initialActive);
    } else {
      await expect(trigger).toHaveAttribute('aria-activedescendant', /.+/);
    }
    const activeId = await trigger.getAttribute('aria-activedescendant');
    const activeOption = page.locator(`[id="${activeId}"]`);
    await expect(activeOption).toHaveRole('option');
    await expect(activeOption).toHaveAttribute('data-active', 'true');
  });
});

test.describe('keyboard + focus (§6)', () => {
  test('focus never leaves the trigger; Enter commits; focus retained on close', async ({
    page,
  }) => {
    const { trigger, panel } = await openSelect(page, 'select-country');

    await trigger.press('ArrowDown');
    await expect(trigger).toBeFocused(); // activedescendant model — no focus move

    const activeId = await trigger.getAttribute('aria-activedescendant');
    const activeText = (await page.locator(`[id="${activeId}"]`).textContent())!.trim();
    await trigger.press('Enter');

    await expect(panel).toBeHidden();
    await expect(trigger).toContainText(activeText);
    await expect(trigger).toBeFocused();
  });

  test('Escape closes the panel without selecting (stage-1 Esc only)', async ({ page }) => {
    const { trigger, panel } = await openSelect(page, 'select-country');
    await trigger.press('Escape');
    await expect(panel).toBeHidden();
    await expect(trigger).toContainText('Select an option'); // localized default placeholder
    await expect(trigger).toBeFocused();
  });
});

test.describe('prepopulated/async value in the browser (DoD 7)', () => {
  test('displayWith labels the closed trigger before options exist; selection re-asserts on load', async ({
    page,
  }) => {
    const trigger = page.getByTestId('select-async').locator('.tm-select__trigger');
    await expect(trigger).toContainText('Ethiopia'); // no options yet

    await page.getByTestId('load-options').click();
    await trigger.click();
    const selected = page.locator('.tm-option__row[aria-selected="true"]');
    await expect(selected).toContainText('Ethiopia');
  });
});

test.describe('RTL (§3.4 residual, DoD 5/6)', () => {
  // With matchWidth the panel and trigger have EQUAL widths, so left- and
  // right-edge alignment are the same assertion — edge geometry cannot tell
  // a mirrored position resolution from an unmirrored one. The mirroring
  // evidence is (a) the direction CDK resolved for its positioning math,
  // stamped as `dir` on the overlay bounding box, and (b) the side
  // inline-END content actually lands on inside the overlay.
  for (const dir of ['ltr', 'rtl'] as const) {
    const endSide = dir === 'rtl' ? 'left' : 'right';
    test(`overlay resolves ${dir}; inline-end content lands on the ${endSide}`, async ({
      page,
    }) => {
      await page.goto(storyUrl('select', { dir }));
      const { trigger, panel } = await openSelect(page, 'select-country');

      await expect(page.locator('.cdk-overlay-connected-position-bounding-box')).toHaveAttribute(
        'dir',
        dir,
      );

      // matchWidth: the panel takes the trigger's width in both directions.
      const triggerBounds = (await trigger.boundingBox())!;
      const panelBounds = (await panel.boundingBox())!;
      expect(Math.abs(panelBounds.width - triggerBounds.width)).toBeLessThanOrEqual(1);

      // Select an option and reopen: the selected row's check glyph sits at
      // the inline end — opposite halves of the row in the two directions.
      await panel.getByRole('option', { name: 'Jordan' }).click();
      await expect(panel).toBeHidden();
      await openSelect(page, 'select-country');

      const row = page.locator('.tm-option__row[aria-selected="true"]');
      const rowBox = (await row.boundingBox())!;
      const glyphBox = (await row.locator('.tm-option__check').boundingBox())!;
      const glyphMid = glyphBox.x + glyphBox.width / 2;
      const rowMid = rowBox.x + rowBox.width / 2;
      if (dir === 'rtl') {
        expect(glyphMid).toBeLessThan(rowMid);
      } else {
        expect(glyphMid).toBeGreaterThan(rowMid);
      }
    });
  }
});

test.describe('live dir toggle (shell Dir wrapper)', () => {
  test('overlay Directionality follows a RUNTIME dir flip, not just fresh loads', async ({
    page,
  }) => {
    await page.goto(storyUrl('select')); // fresh LTR load
    await page.getByTestId('lang-ar').click(); // flip at runtime — no reload
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

    await openSelect(page, 'select-country');
    // The root Directionality reads <html dir> once at construction; without
    // the shell's Dir wrapper the overlay would still stamp (and position)
    // dir="ltr" here.
    await expect(page.locator('.cdk-overlay-connected-position-bounding-box')).toHaveAttribute(
      'dir',
      'rtl',
    );
  });
});

test.describe('reduced motion (§6/DoD 15)', () => {
  test('caret transition collapses under prefers-reduced-motion', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('select'));
    const caret = page.getByTestId('select-country').locator('.tm-select__caret');
    const transition = await caret.evaluate((el) => getComputedStyle(el).transitionProperty);
    expect(transition).toBe('none');
  });
});
