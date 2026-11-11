// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { appStable } from '../support/settle';
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
    // Derived from the panel rather than a fixed point: the shell's
    // layout decides where "outside" is, and a hard-coded coordinate
    // silently starts landing INSIDE the panel when that layout moves.
    const box = (await panel.boundingBox())!;
    const viewport = page.viewportSize()!;
    await page.mouse.click(
      Math.min(box.x + box.width + 40, viewport.width - 5),
      Math.max(box.y - 40, 5),
    );
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
    // The wrapping story nav grows with the library and pushes the trigger
    // down; keep room BELOW it so the panel opens downward — this escape
    // assertion measures a downward-opening panel crossing the clip edge.
    await page.setViewportSize({ width: 1280, height: 1400 });
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

    await appStable(page); // the open's highlight has rendered before it is read
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

    await appStable(page); // the open's highlight has rendered before it is read
    const before = await trigger.getAttribute('aria-activedescendant');
    await trigger.press('ArrowDown');
    await expect(trigger).toBeFocused(); // activedescendant model — no focus move
    // The highlight is read out of the DOM, so the keypress's render has to
    // have landed first — otherwise this reads the PREVIOUS option and the
    // Enter below commits a different one.
    await expect(trigger).not.toHaveAttribute('aria-activedescendant', before ?? '');

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

  test('typing on the CLOSED trigger opens the panel and moves to the match — never commits', async ({
    page,
  }) => {
    const { trigger, panel } = await openSelect(page, 'select-country');
    await trigger.press('Escape'); // closed again, focus retained
    await expect(panel).toBeHidden();

    // Record every data-active transition the DOM ever carries, so the
    // assertion below can prove the panel never flashes the DEFAULT
    // highlight before jumping to the match.
    await page.evaluate(() => {
      const log: string[] = [];
      (window as unknown as { __activeLog: string[] }).__activeLog = log;
      const record = (el: Element) => log.push(el.textContent?.trim() ?? '');
      new MutationObserver((records) => {
        for (const rec of records) {
          const target = rec.target as Element;
          if (rec.type === 'attributes' && target.getAttribute('data-active') === 'true') {
            record(target);
          }
          if (rec.type === 'childList') {
            for (const node of rec.addedNodes) {
              if (node.nodeType === 1) {
                for (const row of (node as Element).querySelectorAll('[data-active="true"]')) {
                  record(row);
                }
              }
            }
          }
        }
      }).observe(document.body, {
        subtree: true,
        childList: true,
        attributes: true,
        attributeFilter: ['data-active'],
      });
    });

    await trigger.press('j'); // Jordan
    await expect(panel).toBeVisible();
    await appStable(page); // the typeahead's highlight has rendered before it is read
    const activeId = await trigger.getAttribute('aria-activedescendant');
    await expect(page.locator(`[id="${activeId}"]`)).toContainText('Jordan');
    // The keystroke searched; it did not silently change the value.
    await expect(trigger).toContainText('Select an option');

    // No jump: the FIRST highlight the DOM ever carried is the match — a
    // frame showing the default option would appear here as a leading entry.
    const transitions = await page.evaluate(
      () => (window as unknown as { __activeLog: string[] }).__activeLog,
    );
    expect(transitions.length).toBeGreaterThan(0);
    expect(transitions[0]).toContain('Jordan');

    await trigger.press('Enter'); // committing stays explicit
    await expect(panel).toBeHidden();
    await expect(trigger).toContainText('Jordan');
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

test.describe('option groups and tm-multi-select: axe in both themes and directions', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`the grouped select's open panel is axe-clean (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('select', { theme, dir }));
        const { panel } = await openSelect(page, 'select-grouped');
        await expect(panel.getByRole('group')).toHaveCount(2);
        await expectNoAxeViolations(page);
      });

      test(`the grouped multi-select's open panel is axe-clean (${theme}, ${dir})`, async ({
        page,
      }) => {
        await page.goto(storyUrl('select', { theme, dir }));
        const { panel } = await openSelect(page, 'multi-grouped');
        await expect(panel.getByRole('option', { name: 'Sale' })).toHaveAttribute(
          'aria-selected',
          'true',
        );
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('option groups', () => {
  test('each group is named by its heading; the heading row has the option height', async ({
    page,
  }) => {
    const { panel } = await openSelect(page, 'select-grouped');
    await expect(panel.getByRole('group', { name: 'Operating' })).toBeVisible();
    await expect(
      panel.getByRole('group', { name: 'Operating' }).getByRole('option'),
    ).toHaveText(['Service', 'Operation']);
    const heading = (await panel.locator('.tm-option-group__heading').first().boundingBox())!;
    const option = (await panel.getByRole('option').first().boundingBox())!;
    expect(Math.abs(heading.height - option.height)).toBeLessThanOrEqual(0.5);
  });

  test('arrow keys step from the last option of one group to the first of the next', async ({
    page,
  }) => {
    const { trigger, panel } = await openSelect(page, 'select-grouped');
    await appStable(page); // the open's highlight has rendered before keys move it
    const active = () => panel.locator('.tm-option__row[data-active="true"]');
    await expect(active()).toHaveText('Service');
    await trigger.press('ArrowDown');
    await expect(active()).toHaveText('Operation');
    await trigger.press('ArrowDown'); // across the "Commercial" heading
    await expect(active()).toHaveText('Sale');
    await trigger.press('End');
    await expect(active()).toHaveText('Other');
    await trigger.press('Enter');
    await expect(panel).toBeHidden();
    await expect(trigger).toContainText('Other');
  });
});

test.describe('tm-multi-select', () => {
  test('clicks toggle options and keep the panel open, with focus on the trigger', async ({
    page,
  }) => {
    const { trigger, panel } = await openSelect(page, 'multi-grouped');
    await panel.getByRole('option', { name: 'Marketing' }).click();
    await panel.getByRole('option', { name: 'Service' }).click();
    await expect(panel).toBeVisible();
    await expect(trigger).toBeFocused();
    // Option order, not click order.
    await expect(page.getByTestId('multi-value')).toHaveText('Service | Sale | Marketing');
    await expect(trigger).toContainText('Service, Sale, Marketing');

    await panel.getByRole('option', { name: 'Sale' }).click();
    await expect(page.getByTestId('multi-value')).toHaveText('Service | Marketing');
    await expect(panel.getByRole('option', { name: 'Sale' })).toHaveAttribute(
      'aria-selected',
      'false',
    );
  });

  test('Space and Enter toggle the active option; Escape closes and keeps the value', async ({
    page,
  }) => {
    const { trigger, panel } = await openSelect(page, 'multi-grouped');
    await appStable(page);
    await trigger.press('Home'); // Service
    await trigger.press('Space');
    await expect(page.getByTestId('multi-value')).toHaveText('Service | Sale');
    await trigger.press('ArrowDown');
    await trigger.press('Enter');
    await expect(page.getByTestId('multi-value')).toHaveText('Service | Operation | Sale');
    await expect(panel).toBeVisible();

    await trigger.press('Escape');
    await expect(panel).toBeHidden();
    await expect(trigger).toBeFocused();
    await expect(trigger).toContainText('Service, Operation, Sale');
  });

  test('Tab and a press outside close the panel', async ({ page }) => {
    const { trigger, panel } = await openSelect(page, 'multi-grouped');
    await trigger.press('Tab');
    await expect(panel).toBeHidden();

    await openSelect(page, 'multi-grouped');
    const box = (await panel.boundingBox())!;
    const viewport = page.viewportSize()!;
    await page.mouse.click(
      Math.min(box.x + box.width + 40, viewport.width - 5),
      Math.max(box.y - 40, 5),
    );
    await expect(panel).toBeHidden();
  });

  test('the trigger keeps one line at the field height however much is selected', async ({
    page,
  }) => {
    const trigger = page.getByTestId('multi-grouped').locator('.tm-select__trigger');
    const before = (await trigger.boundingBox())!.height;
    const { panel } = await openSelect(page, 'multi-grouped');
    for (const name of ['Service', 'Operation', 'Marketing', 'Other']) {
      await panel.getByRole('option', { name }).click();
    }
    await expect(page.getByTestId('multi-value')).toHaveText(
      'Service | Operation | Sale | Marketing | Other',
    );
    expect((await trigger.boundingBox())!.height).toBe(before);
    // The rendering ellipsizes; the text, and so the accessible value, is whole.
    await expect(trigger.locator('.tm-select__value')).toHaveText(
      'Service, Operation, Sale, Marketing, Other',
    );
  });

  for (const dir of ['ltr', 'rtl'] as const) {
    const endSide = dir === 'rtl' ? 'left' : 'right';
    test(`the row checkbox sits at the inline end (${dir}: the ${endSide})`, async ({ page }) => {
      await page.goto(storyUrl('select', { dir }));
      const { panel } = await openSelect(page, 'multi-grouped');
      const row = panel.getByRole('option', { name: 'Sale' });
      const rowBox = (await row.boundingBox())!;
      const checkbox = (await row.locator('.tm-option__box').boundingBox())!;
      const checkboxMid = checkbox.x + checkbox.width / 2;
      const rowMid = rowBox.x + rowBox.width / 2;
      if (dir === 'rtl') {
        expect(checkboxMid).toBeLessThan(rowMid);
      } else {
        expect(checkboxMid).toBeGreaterThan(rowMid);
      }
    });
  }
});
