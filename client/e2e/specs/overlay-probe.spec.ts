import { expect, test, type Page } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Risk-spike specs for the spec §3.4 composition (aria combobox/listbox
 * nested in cdkConnectedOverlay with usePopover:'inline').
 *
 * The mouse specs drive REAL trusted mouse input (locator.click), guarding
 * the open upstream bug angular/components#32504 (aria-in-overlay mouse
 * interaction). If these fail, the explicit pointer path mitigation must be
 * switched on (spec §3.4). Superseded by the production tm-select specs in
 * stage 11, then deleted.
 */

async function openProbe(page: Page, testid: string) {
  const trigger = page.getByTestId(testid);
  await trigger.click();
  const panel = page.getByTestId(`${testid}-panel`);
  await expect(panel).toBeVisible();
  return { trigger, panel };
}

test.beforeEach(async ({ page }) => {
  await page.goto(storyUrl('overlay-probe'));
});

test.describe('clipping escape (usePopover: inline)', () => {
  test('panel escapes an overflow:hidden ancestor', async ({ page }) => {
    const clipbox = page.getByTestId('clipbox');
    const clipBounds = (await clipbox.boundingBox())!;

    const { panel } = await openProbe(page, 'clipped');
    const panelBounds = (await panel.boundingBox())!;

    // The clip box is 60px tall; a visible panel must extend well below it.
    expect(panelBounds.y + panelBounds.height).toBeGreaterThan(clipBounds.y + clipBounds.height);

    // And its options are actually visible (not just laid out but clipped).
    await expect(panel.getByRole('option', { name: 'Epsilon' })).toBeVisible();
  });
});

test.describe('flip-up near the viewport bottom', () => {
  test('panel flips above a bottom-pinned trigger', async ({ page }) => {
    const { trigger, panel } = await openProbe(page, 'flip');
    const triggerBounds = (await trigger.boundingBox())!;
    const panelBounds = (await panel.boundingBox())!;

    // With [bottom-start, top-start] and the updatePosition()-on-attach
    // macrotask fix, the panel must sit fully above the trigger.
    expect(panelBounds.y + panelBounds.height).toBeLessThanOrEqual(triggerBounds.y + 1);
  });
});

test.describe('ARIA id chain across the portal', () => {
  test('trigger -> listbox -> active option ids resolve', async ({ page }) => {
    const { trigger } = await openProbe(page, 'clipped');

    await expect(trigger).toHaveAttribute('aria-expanded', 'true');

    // aria-controls on the trigger must point at the portaled listbox.
    const controls = await trigger.getAttribute('aria-controls');
    expect(controls).toBeTruthy();
    const listbox = page.locator(`[id="${controls}"]`);
    await expect(listbox).toHaveRole('listbox');

    // Arrow down activates an option; aria-activedescendant must reference it.
    await expect(trigger).toHaveAttribute('aria-activedescendant', /.+/);
    const initialActiveId = await trigger.getAttribute('aria-activedescendant');
    await trigger.press('ArrowDown');
    await expect(trigger).not.toHaveAttribute('aria-activedescendant', initialActiveId!);
    const activeId = await trigger.getAttribute('aria-activedescendant');
    expect(activeId).toBeTruthy();
    const activeOption = page.locator(`[id="${activeId}"]`);
    await expect(activeOption).toHaveRole('option');
    await expect(activeOption).toHaveAttribute('data-active', 'true');
  });
});

test.describe('mouse interaction (angular/components#32504 guard, real events)', () => {
  test('clicking an option commits and closes', async ({ page }) => {
    const { trigger, panel } = await openProbe(page, 'clipped');

    await panel.getByRole('option', { name: 'Gamma' }).click();

    await expect(panel).toBeHidden();
    await expect(trigger).toContainText('Gamma');
    await expect(trigger).toHaveAttribute('aria-expanded', 'false');
  });

  test('clicking outside closes the panel', async ({ page }) => {
    const { panel } = await openProbe(page, 'clipped');

    // Raw trusted mouse input on empty page space (outside trigger + panel).
    await page.mouse.click(600, 400);

    await expect(panel).toBeHidden();
  });

  test('clicking the trigger toggles open and closed', async ({ page }) => {
    const trigger = page.getByTestId('clipped');
    const panel = page.getByTestId('clipped-panel');

    await trigger.click();
    await expect(panel).toBeVisible();

    await trigger.click();
    await expect(panel).toBeHidden();
  });
});

test.describe('keyboard interaction', () => {
  test('arrow + Enter selects and closes; focus stays on the trigger', async ({ page }) => {
    const { trigger, panel } = await openProbe(page, 'clipped');

    // Activate the next option (whatever open made active), read its label,
    // then commit with Enter — robust to the open-activates-first behavior.
    const initialActiveId = await trigger.getAttribute('aria-activedescendant');
    await trigger.press('ArrowDown');
    if (initialActiveId) {
      await expect(trigger).not.toHaveAttribute('aria-activedescendant', initialActiveId);
    } else {
      await expect(trigger).toHaveAttribute('aria-activedescendant', /.+/);
    }
    const activeId = await trigger.getAttribute('aria-activedescendant');
    const activeText = (await page.locator(`[id="${activeId}"]`).textContent())!.trim();
    await trigger.press('Enter');

    await expect(panel).toBeHidden();
    await expect(trigger).toContainText(activeText);
    await expect(trigger).toBeFocused();
  });

  test('Escape closes the panel without selecting', async ({ page }) => {
    const { trigger, panel } = await openProbe(page, 'clipped');

    await trigger.press('Escape');

    await expect(panel).toBeHidden();
    await expect(trigger).toContainText('Choose an option');
    await expect(trigger).toBeFocused();
  });
});
