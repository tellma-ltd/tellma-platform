// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expectNoAxeViolations, signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';

// The bell's and the tray's panels (spec 0028 §6, §7.2): each opens from its
// app-bar button in a popover from lg and in the end drawer below it, names
// itself, passes axe in both themes (each project runs its own language and
// direction), and closes on Escape with focus back on its button.

/** The themes. */
const THEMES = ['light', 'dark'] as const;

/** A desktop at lg and a phone below it. */
const WIDTHS = [1280, 390] as const;

/** The two panels, by the app bar's slot and the panel's id. */
const PANELS = [
  { slot: 'bell', id: 'tm-attention-bell-panel' },
  { slot: 'tray', id: 'tm-attention-tray-panel' },
] as const;

test.describe('the attention panels', () => {
  for (const theme of THEMES) {
    test.describe(theme, () => {
      test.use({ colorScheme: theme });

      test.beforeEach(async ({ page, cast }) => {
        await signInAs(page, cast.user);
        await warmUp(page, '/1');
      });

      test('the bell and the tray open their panels at every width, pass axe and give focus back on Escape', async ({
        page,
      }) => {
        for (const width of WIDTHS) {
          await page.setViewportSize({ width, height: 800 });
          for (const { slot, id } of PANELS) {
            const button = page.locator(`tm-app-bar [data-tm-slot="${slot}"] > button`);
            await button.click();
            const panel = page.locator(`#${id}`);
            await expect(panel).toBeVisible();
            await expect(button).toHaveAttribute('aria-expanded', 'true');
            // A popover from lg, the end drawer below it, each named for the panel.
            await expect(
              page.getByRole('dialog').filter({ has: panel }).last(),
            ).toHaveAccessibleName(/.+/);
            await expect(page.locator('.tm-popover__panel').filter({ has: panel })).toHaveCount(
              width >= 992 ? 1 : 0,
            );
            await expectNoAxeViolations(page, `#${id}`);

            await page.keyboard.press('Escape');
            await expect(panel).toHaveCount(0);
            await expect(button).toBeFocused();
          }
        }
      });
    });
  }
});
