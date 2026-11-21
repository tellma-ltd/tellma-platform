// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, type Locator, type Page } from '@playwright/test';

/**
 * Drivers for a story's held-response gate (the "Hold … responses" toolbar
 * controls). While a gate holds, every response of that data source waits
 * for an explicit release, so a state that lives only while a request is in
 * flight — a spinner, a pending cell, a panel measured while loading — stays
 * on screen until the test has asserted it. A fixed delay would make each
 * such assertion a race against the runner's speed.
 *
 * `name` is the data source the gate belongs to (`search`, `resolver`).
 */

/** The live count of responses waiting on the gate. */
export function heldResponses(page: Page, name: string): Locator {
  return page.getByTestId(`held-${name}`);
}

/**
 * Starts holding the data source's responses. A real click on the toolbar
 * checkbox: call it before focusing the control under test.
 */
export async function holdResponses(page: Page, name: string): Promise<void> {
  await page.getByTestId(`hold-${name}`).check();
}

/**
 * Releases every response the gate holds, after checking that at least one
 * is waiting (a release that ran ahead of its request would release nothing
 * and leave that request held). Callers that know the exact number assert
 * it with {@link heldResponses} first.
 *
 * The release is a custom `release` event dispatched on the release
 * button, not a click: a click outside the control under test, even a
 * synthetic one, is itself an interaction (an outside press closes an open
 * dropdown, a departure starts a blur resolution) and would change what the
 * test observes.
 */
export async function releaseResponses(page: Page, name: string): Promise<void> {
  await expect(heldResponses(page, name)).not.toHaveText('0');
  await page.getByTestId(`release-${name}`).dispatchEvent('release');
  await expect(heldResponses(page, name)).toHaveText('0');
}
