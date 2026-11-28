// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';
import {
  SHELL_TEXT,
  closeHeld,
  launcherLanded,
  pinnedBar,
  servePins,
  sessionButton,
  shownFrame,
  watchFrame,
} from '../support/shell';

// The shell's instant feedback (spec 0027 §14): the session menu, the
// launcher, the sandbox marker's popover, a new pin and a theme change each
// show in the first frame painted after the click or the key that asked for
// them, with nothing awaited from the server. A frame probe per input
// records the frame, which is a timing gate parallel workers on one machine
// cannot hold, so the flow runs in the nightly tier's performance gates;
// the report records each frame. The sync serves no pins, and the pin's and
// the theme's writes are held, so the user's stored preferences never change.

/** The latest frame an overlay, a pin or a theme may first show in: the first painted after the input. */
const FEEDBACK_FRAME = 1;

test.describe('shell feedback', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    // No pins, on the device and from the server, so the customers' page has none and the control pins it.
    await servePins(page, []);
    await warmUp(page, '/2');
    await warmUp(page, '/1');
  });

  // The page closes while its writes are still held, so the writes it flushes
  // as it goes never reach the user's stored preferences.
  test.afterEach(async ({ page }) => {
    await closeHeld(page);
  });

  test('the overlays, a new pin and a theme change show in the frame after their input @perf', async ({
    page,
    lang,
  }) => {
    const text = SHELL_TEXT[lang];
    const frames: Record<string, number> = {};
    await page.goto('/1/customers');
    await expect(sessionButton(page, lang)).toBeVisible();

    // --- The session menu, from a click on the session button ---
    await watchFrame(page, 'click', '[role="menu"]');
    await sessionButton(page, lang).click();
    frames['session menu'] = await shownFrame(page, 'the session menu');

    // --- A theme change, from a click on the menu's Dark row, which keeps the menu open ---
    await watchFrame(page, 'click', 'html[data-theme="dark"]');
    await page.getByRole('menuitemradio', { name: text.dark }).click();
    frames['theme'] = await shownFrame(page, 'the dark theme');
    await page.keyboard.press('Escape');

    // --- The launcher, from Mod+K, once its prefetched panel has landed ---
    await launcherLanded(page);
    await watchFrame(page, 'keydown', '[role="listbox"]');
    await page.keyboard.press('ControlOrMeta+K');
    frames['launcher'] = await shownFrame(page, 'the launcher');
    await page.keyboard.press('Escape');

    // --- A new pin, from a click on the pin control ---
    await watchFrame(page, 'click', 'nav.tm-pinned-bar .tm-pinned-bar__chip--current');
    await pinnedBar(page).getByRole('button', { name: text.pinThisPage }).click();
    frames['pin'] = await shownFrame(page, 'the new pin');
    await page.keyboard.press('Escape');

    // --- The sandbox marker's popover, from a click on the marker ---
    await page.goto('/2');
    const marker = page.locator('tm-app-bar .tm-app-bar__marker[data-tm-marker="sandbox"]');
    await expect(marker).toBeVisible();
    await watchFrame(page, 'click', '.tm-popover__panel');
    await marker.click();
    frames['marker popover'] = await shownFrame(page, "the marker's popover");

    test.info().annotations.push({ type: 'feedback frames', description: JSON.stringify(frames) });
    for (const [what, frame] of Object.entries(frames)) {
      expect(frame, what).toBeLessThanOrEqual(FEEDBACK_FRAME);
    }
  });
});
