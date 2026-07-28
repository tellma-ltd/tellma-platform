// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-modal (DoD 15): the four dismissal paths with
 * their typed results, the canDismiss guard, focus trap + restore, stacked
 * modals dismissing topmost-first with chained focus restore, the
 * tm-select-above-modal top-layer pin, and the axe/forced-colors/
 * reduced-motion gates.
 */

function shell(page: Page): ReturnType<Page['locator']> {
  return page.locator('tm-modal-shell');
}

async function openBasic(page: Page): Promise<void> {
  await page.getByTestId('open-basic').click();
  await expect(shell(page)).toBeVisible();
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`open modal is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('modal', { theme }));
      await openBasic(page);
      await expectNoAxeViolations(page);
    });
  }

  test('open modal is axe-clean in RTL', async ({ page }) => {
    await page.goto(storyUrl('modal', { dir: 'rtl' }));
    await openBasic(page);
    await expectNoAxeViolations(page);
  });
});

test.describe('dismissal paths and results (DoD 15)', () => {
  test('api close carries the value; X, backdrop, and Esc carry their via', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    const result = page.getByTestId('modal-result');

    // via: 'api' with a value (a footer action).
    await openBasic(page);
    await page.getByTestId('modal-save').click();
    await expect(result).toHaveText('{"via":"api","value":"posted"}');
    await expect(shell(page)).toHaveCount(0);

    // via: 'api' with no value (the Cancel action closes empty).
    await openBasic(page);
    await page.getByTestId('modal-cancel').click();
    await expect(result).toHaveText('{"via":"api"}');

    // via: 'close-button'.
    await openBasic(page);
    await page.locator('.tm-modal__close').click();
    await expect(result).toHaveText('{"via":"close-button"}');

    // via: 'backdrop' — click outside the panel.
    await openBasic(page);
    await page.locator('.cdk-overlay-backdrop').click({ position: { x: 4, y: 4 } });
    await expect(result).toHaveText('{"via":"backdrop"}');

    // via: 'escape'.
    await openBasic(page);
    await page.keyboard.press('Escape');
    await expect(result).toHaveText('{"via":"escape"}');
  });

  test('showClose/backdropDismiss off: no X, backdrop ignored, Esc still works', async ({
    page,
  }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-no-close').click();
    await expect(shell(page)).toBeVisible();
    await expect(page.locator('.tm-modal__close')).toHaveCount(0);
    await page.locator('.cdk-overlay-backdrop').click({ position: { x: 4, y: 4 } });
    await expect(shell(page)).toBeVisible(); // backdrop ignored
    await page.keyboard.press('Escape');
    await expect(shell(page)).toHaveCount(0);
    await expect(page.getByTestId('modal-result')).toHaveText('{"via":"escape"}');
  });

  test('template content receives the ref and data through the context', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-template').click();
    await expect(page.getByTestId('template-content')).toHaveText('Template body for INV-00042.');
    await page.getByTestId('template-done').click();
    await expect(page.getByTestId('modal-result')).toHaveText(
      '{"via":"api","value":"from-template"}',
    );
  });
});

test.describe('canDismiss guard (DoD 15)', () => {
  test('dirty state opens the confirm layer; Keep stays, Discard dismisses', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-guarded').click();
    await page.getByTestId('guarded-input').fill('draft');

    // Esc on the dirty modal opens the confirm layer instead of closing.
    await page.keyboard.press('Escape');
    await expect(page.getByTestId('confirm-content')).toBeVisible();
    await expect(page.getByTestId('guarded-input')).toBeAttached();

    // "Keep editing" resolves the guard false — both stay as they were.
    await page.getByTestId('confirm-keep').click();
    await expect(page.getByTestId('confirm-content')).toHaveCount(0);
    await expect(page.getByTestId('guarded-input')).toBeVisible();

    // Second attempt, this time discarding: the guarded modal closes.
    await page.keyboard.press('Escape');
    await page.getByTestId('confirm-discard').click();
    await expect(shell(page)).toHaveCount(0);
    await expect(page.getByTestId('modal-result')).toHaveText('{"via":"escape"}');
  });

  test('a pristine guarded modal dismisses without a confirm', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-guarded').click();
    await expect(page.getByTestId('guarded-input')).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(shell(page)).toHaveCount(0);
    await expect(page.getByTestId('confirm-content')).toHaveCount(0);
  });
});

test.describe('focus (DoD 15)', () => {
  test('focus is trapped inside the dialog and restored to the opener on close', async ({
    page,
  }) => {
    await page.goto(storyUrl('modal'));
    await openBasic(page);

    // Initial focus lands inside the dialog.
    const inDialog = () =>
      page.evaluate(() => {
        const container = document.querySelector('cdk-dialog-container');
        return container !== null && container.contains(document.activeElement);
      });
    expect(await inDialog()).toBe(true);

    // Tabbing cycles within the trap: it never leaves the dialog.
    for (let i = 0; i < 6; i += 1) {
      await page.keyboard.press('Tab');
      expect(await inDialog()).toBe(true);
    }

    await page.keyboard.press('Escape');
    await expect(shell(page)).toHaveCount(0);
    await expect(page.getByTestId('open-basic')).toBeFocused();
  });

  test('stacked modals dismiss topmost-first with chained focus restore', async ({ page }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-stacked').click();
    await page.getByTestId('open-inner').click();
    await expect(shell(page)).toHaveCount(2);

    // Esc closes the inner layer only; focus restores into the outer one.
    await page.keyboard.press('Escape');
    await expect(shell(page)).toHaveCount(1);
    await expect(page.getByTestId('stacking-content')).toBeVisible();
    await expect(page.getByTestId('open-inner')).toBeFocused();

    // Esc again closes the outer layer; focus restores to the opener.
    await page.keyboard.press('Escape');
    await expect(shell(page)).toHaveCount(0);
    await expect(page.getByTestId('open-stacked')).toBeFocused();
  });
});

test.describe('top-layer interplay (DoD 15, Playwright-pinned)', () => {
  test('a tm-select dropdown inside a modal paints above it and is clickable', async ({
    page,
  }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-select').click();
    const trigger = page.getByTestId('modal-select').locator('.tm-select__trigger');
    await trigger.click();
    const panel = page.locator('.tm-select__panel');
    await expect(panel).toBeVisible();

    // Pin the top-layer paint: the element at the first option's midpoint
    // IS the option, not the modal above it.
    const covered = await page
      .locator('.tm-option__row', { hasText: 'Posted' })
      .evaluate((option) => {
        const rect = option.getBoundingClientRect();
        const hit = document.elementFromPoint(
          rect.left + rect.width / 2,
          rect.top + rect.height / 2,
        );
        return hit === null ? true : !option.contains(hit) && !hit.contains(option);
      });
    expect(covered).toBe(false);

    // And it is genuinely clickable (Playwright refuses covered targets).
    await page.locator('.tm-option__row', { hasText: 'Posted' }).click();
    await expect(panel).toHaveCount(0);
    await expect(trigger).toContainText('Posted');

    // The select consumed its own Esc path; the modal is still open.
    await expect(shell(page)).toBeVisible();
  });

  test('Esc closes an open dropdown first, the modal second (innermost-first)', async ({
    page,
  }) => {
    await page.goto(storyUrl('modal'));
    await page.getByTestId('open-select').click();
    await page.getByTestId('modal-select').locator('.tm-select__trigger').click();
    await expect(page.locator('.tm-select__panel')).toBeVisible();

    await page.keyboard.press('Escape');
    await expect(page.locator('.tm-select__panel')).toHaveCount(0);
    await expect(shell(page)).toBeVisible(); // the modal survived

    await page.keyboard.press('Escape');
    await expect(shell(page)).toHaveCount(0);
  });
});

test.describe('appearance gates', () => {
  test('forced-colors: the panel keeps a visible border', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('modal'));
    await openBasic(page);
    const border = await page
      .locator('cdk-dialog-container')
      .evaluate((el) => getComputedStyle(el).borderTopStyle + '|' + getComputedStyle(el).borderTopWidth);
    expect(border).toMatch(/^solid\|[1-9]/);
  });

  test('reduced motion collapses the enter animation', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await page.goto(storyUrl('modal'));
    await openBasic(page);
    const animation = await page
      .locator('cdk-dialog-container')
      .evaluate((el) => getComputedStyle(el).animationName);
    expect(animation).toBe('none');
  });
});
