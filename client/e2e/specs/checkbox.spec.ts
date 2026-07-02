import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/** Browser battery for tm-checkbox (DoD 4): axe, trusted-input semantics,
 *  mixed exposure, forced-colors, and the 24px hit-target rule (§6). */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`checkbox story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('checkbox', { theme }));
      await expect(page.getByTestId('cb-simple')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }
});

test.describe('interaction (trusted input)', () => {
  test('space toggles the focused checkbox', async ({ page }) => {
    await page.goto(storyUrl('checkbox'));
    const native = page.getByTestId('cb-simple').getByRole('checkbox');
    await native.focus();
    await page.keyboard.press('Space');
    await expect(native).toBeChecked();
    await page.keyboard.press('Space');
    await expect(native).not.toBeChecked();
  });

  test('clicking the label text toggles', async ({ page }) => {
    await page.goto(storyUrl('checkbox'));
    const checkbox = page.getByTestId('cb-simple');
    await checkbox.getByText('Email me updates').click();
    await expect(checkbox.getByRole('checkbox')).toBeChecked();
  });

  test('tri-state parent exposes checked="mixed" to assistive tech', async ({ page }) => {
    await page.goto(storyUrl('checkbox'));
    const parent = page.getByTestId('cb-parent').getByRole('checkbox');
    // 1/3 rows selected -> indeterminate -> AT sees the mixed state
    // (browser-computed from the IDL property; no manual aria-checked, §3.3).
    await expect(parent).toHaveJSProperty('indeterminate', true);
    const ariaSnapshot = await page.getByTestId('cb-parent').ariaSnapshot();
    expect(ariaSnapshot).toContain('[checked=mixed]');

    // Toggling the parent selects all and clears the mixed state.
    await parent.click();
    await expect(parent).toBeChecked();
    await expect(parent).toHaveJSProperty('indeterminate', false);
    await expect(page.getByTestId('cb-row-2').getByRole('checkbox')).toBeChecked();
  });

  test('checkbox bound via [formField] shows the field error after touch', async ({ page }) => {
    await page.goto(storyUrl('checkbox'));
    const terms = page.getByTestId('cb-terms').getByRole('checkbox');
    await terms.focus();
    await page.keyboard.press('Tab'); // blur -> touched
    const error = page.getByTestId('ff-terms').locator('.tm-form-field__error');
    await expect(error).toHaveText('Please accept the terms');
    await terms.check();
    await expect(error).toHaveText('');
  });
});

test.describe('target size (§6, WCAG 2.5.8)', () => {
  test('the clickable region clears 24x24 even for a bare checkbox', async ({ page }) => {
    await page.goto(storyUrl('checkbox'));
    for (const id of ['cb-simple', 'cb-bare']) {
      const native = page.getByTestId(id).getByRole('checkbox');
      const box = (await native.boundingBox())!;
      expect(box.width, `${id} width`).toBeGreaterThanOrEqual(24);
      expect(box.height, `${id} height`).toBeGreaterThanOrEqual(24);
    }
  });
});

test.describe('forced-colors (§6)', () => {
  test('the box boundary and checked state survive forced colors', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('checkbox'));
    const box = page.getByTestId('cb-simple').locator('.tm-checkbox__box');
    const borderStyle = await box.evaluate((el) => getComputedStyle(el).borderStyle);
    expect(borderStyle).toBe('solid');

    await page.getByTestId('cb-simple').getByRole('checkbox').check();
    const background = await box.evaluate((el) => getComputedStyle(el).backgroundColor);
    expect(background).not.toBe('rgba(0, 0, 0, 0)'); // Highlight paint applied
  });
});
