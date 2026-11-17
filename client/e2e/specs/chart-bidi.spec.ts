// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * The chart's canvas text in each engine (`@cross-engine`): a string built
 * from runs, as the chart hands it to the engine, draws on a canvas that
 * takes its direction from CSS exactly as its runs drawn apart do, each in
 * its own direction and placed in the base direction's order — in both
 * page directions, for display names by parts, for negative values and
 * shares in Arabic-Indic digits, and for a tooltip line. Canvas pixels are
 * the only place the marks live, so they are compared as pixels.
 */

/** The story's cases, by id. */
const CASES = [
  'code-name',
  'name-code',
  'code-arabic',
  'arabic-latin',
  'negative-arabic',
  'percent-arabic',
  'tooltip-line',
] as const;

/** How many pixels of two of the story's canvases differ, and how many either paints. */
async function compare(
  page: Page,
  id: string,
  direction: 'ltr' | 'rtl',
  modes: readonly ['together' | 'plain', 'apart'],
): Promise<{ differing: number; painted: number }> {
  return page.evaluate(
    ([id, direction, modes]) => {
      const read = (mode: string): Uint8ClampedArray => {
        const canvas = document.querySelector<HTMLCanvasElement>(
          `[data-testid="bidi-${id}-${direction}-${mode}"]`,
        )!;
        return canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height).data;
      };
      const [a, b] = modes.map(read);
      let differing = 0;
      let painted = 0;
      for (let offset = 3; offset < a!.length; offset += 4) {
        if (a![offset] !== 0 || b![offset] !== 0) {
          painted += 1;
        }
        if (
          a![offset] !== b![offset] ||
          a![offset - 1] !== b![offset - 1] ||
          a![offset - 2] !== b![offset - 2] ||
          a![offset - 3] !== b![offset - 3]
        ) {
          differing += 1;
        }
      }
      return { differing, painted };
    },
    [id, direction, modes] as const,
  );
}

/** Opens the story and waits until every canvas has drawn. */
async function open(page: Page): Promise<void> {
  await page.goto(storyUrl('chart-bidi'));
  await expect(page.getByTestId('bidi-drawn')).toHaveText('drawn');
}

test('@cross-engine every canvas string draws as its runs drawn apart, in both directions', async ({ page }) => {
  await open(page);
  for (const id of CASES) {
    for (const direction of ['ltr', 'rtl'] as const) {
      const { differing, painted } = await compare(page, id, direction, ['together', 'apart']);
      expect(painted, `${id} ${direction} paints`).toBeGreaterThan(50);
      expect(differing, `${id} ${direction}`).toBe(0);
    }
  }
});

test('@cross-engine the marks are what keep the runs apart', async ({ page }) => {
  // Without them, a Latin name and the code after it read as one
  // left-to-right run in a right-to-left page (the digits join the letters
  // before them), and a negative Arabic-Indic value's sign moves to the
  // number's other side: the comparison above would catch a mark that
  // stopped working.
  await open(page);
  for (const id of ['name-code', 'negative-arabic'] as const) {
    const { differing } = await compare(page, id, 'rtl', ['plain', 'apart']);
    expect(differing, id).toBeGreaterThan(0);
  }
});
