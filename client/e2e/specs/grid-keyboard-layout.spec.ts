// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import {
  activateCell,
  cell,
  cellText,
  findBar,
  findInput,
  gotoGrid,
  liveRegion,
  modelJson,
} from '../support/grid';
import { ARABIC_KEYS, pressModOnLayout } from '../support/layout-keys';

/**
 * The grid's Mod shortcuts on a non-Latin layout: an Arabic layout sends
 * `'ي'` for the D key, so a shortcut that compared `event.key` alone would
 * never fire there. The grid matches letters through the physical key, and
 * Mod+A, Mod+D, Mod+Z, Mod+Y and Mod+F act as they do on a US layout.
 */

interface Line {
  readonly id: number;
  readonly description: string | null;
}

/** The editable story's model line at a row index. */
async function lineAt(page: Page, index: number): Promise<Line> {
  return (await modelJson<Line[]>(page))[index];
}

test('Mod+A selects every cell from an Arabic layout', async ({ page }) => {
  await gotoGrid(page, 'grid-readonly');
  await cell(page, 1, 1).click();

  await pressModOnLayout(page, ARABIC_KEYS.A);

  await expect(liveRegion(page)).toContainText('All cells selected');
});

test('Mod+D fills down, Mod+Z undoes it and Mod+Y redoes it, from an Arabic layout', async ({
  page,
}) => {
  await gotoGrid(page, 'grid-editable');
  const top = await cellText(page, 1, 0);
  const before = (await lineAt(page, 3)).description;
  expect(before).not.toBe(top);

  await activateCell(page, 1, 0);
  await cell(page, 3, 0).click({ modifiers: ['Shift'] });
  await pressModOnLayout(page, ARABIC_KEYS.D);
  await expect.poll(async () => (await lineAt(page, 3)).description).toBe(top);

  await pressModOnLayout(page, ARABIC_KEYS.Z);
  await expect.poll(async () => (await lineAt(page, 3)).description).toBe(before);

  await pressModOnLayout(page, ARABIC_KEYS.Y);
  await expect.poll(async () => (await lineAt(page, 3)).description).toBe(top);
});

test('Mod+F opens the find bar from an Arabic layout, and re-selects its query there', async ({
  page,
}) => {
  await gotoGrid(page, 'grid-list-screen');
  await activateCell(page, 0, 0);

  await pressModOnLayout(page, ARABIC_KEYS.F);
  await expect(findBar(page)).toBeVisible();
  await expect(findInput(page)).toBeFocused();

  // Inside the bar, Mod+F selects the whole query instead of the browser's find.
  await findInput(page).fill('Item 9');
  await findInput(page).evaluate((input: HTMLInputElement) => input.setSelectionRange(6, 6));
  await pressModOnLayout(page, ARABIC_KEYS.F);
  await expect
    .poll(() =>
      findInput(page).evaluate((input: HTMLInputElement) => [
        input.selectionStart,
        input.selectionEnd,
      ]),
    )
    .toEqual([0, 6]);
});
