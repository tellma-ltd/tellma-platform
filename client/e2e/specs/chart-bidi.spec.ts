// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * The chart's canvas text in each engine (`@cross-engine`): a string built
 * from runs, as the chart hands it to the engine, draws on a canvas that
 * takes its direction from CSS exactly as its runs drawn apart do, each in
 * its own direction and placed in the base direction's order — in both
 * page directions, for display names by parts, for negative values and
 * shares in Arabic-Indic digits, and for a tooltip line. Then the same
 * through the chart's engine: every label charts in Arabic and in Egyptian
 * Arabic paint, data labels of negative values included. Canvas pixels are
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

test('@cross-engine every canvas string draws as its runs drawn apart, in both directions', async ({
  page,
}) => {
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

/** One label the chart's engine drew, as the story's probe compared it. */
interface ProbedLabel {
  /** The label without its marks. */
  readonly text: string;
  /** How many runs the story expected the label to draw; 0 for a label it does not know. */
  readonly runs: number;
  /** How many bidi marks the engine's string carries. */
  readonly marks: number;
  /** Pixels that differ between the engine's drawing and the runs drawn apart. */
  readonly differing: number;
  /** Pixels the label itself paints. */
  readonly painted: number;
  /** Pixels elements painted after the label cover, left out of both counts. */
  readonly covered: number;
}

for (const dir of ['ltr', 'rtl'] as const) {
  test(`@cross-engine every label the chart's engine draws equals its runs drawn apart (${dir})`, async ({
    page,
  }) => {
    // Through the facade: the engine lays each marked label out, cuts it into
    // spans and paints it where its component puts it. The probe repaints the
    // scene without each label, draws the runs the story expects of it (from
    // the charts' data, not the drawn string) apart where the engine
    // anchored it, and compares the canvas pixel for pixel: a label that
    // lost its marks draws its runs in another order and differs.
    await page.goto(storyUrl('chart-bidi', { dir }));
    for (const id of [
      'facade-column-ar',
      'facade-pie-ar',
      'facade-column-ar-eg',
      'facade-pie-ar-eg',
    ]) {
      await expect(page.getByTestId(id).locator('canvas').first()).toBeVisible();
    }
    await appStable(page);
    await page.getByTestId('bidi-probe').click();
    await expect(page.getByTestId('bidi-facade')).not.toBeEmpty();
    const results = JSON.parse(
      (await page.getByTestId('bidi-facade').textContent()) ?? '{}',
    ) as Record<string, ProbedLabel[]>;
    for (const [chart, labels] of Object.entries(results)) {
      const texts = labels.map((label) => label.text);
      // The mixed labels are among those compared.
      if (chart.startsWith('facade-column')) {
        expect(texts, `${chart} category by parts`).toContain('1100 - النقدية');
        expect(texts, `${chart} category, a name then a code`).toContain('Cash - 1100');
        expect(texts, `${chart} legend entry`).toContain('B2C - أفراد');
        const negative = chart.endsWith('-ar-eg') ? /^-[٠-٩٬]+$/ : /^-[0-9,]+$/;
        const negatives = labels.filter((label) => negative.test(label.text));
        expect(negatives.length, `${chart} negative data labels`).toBe(2);
        // In a right-to-left page a number is a run against the base, so
        // its string carries marks.
        if (dir === 'rtl') {
          for (const label of negatives) {
            expect(label.marks, `${chart} "${label.text}" marks`).toBeGreaterThan(0);
          }
        }
      } else {
        expect(
          texts.some((text) => text.startsWith('B2C - أفراد')),
          `${chart} slice label`,
        ).toBe(true);
      }
      // Every span is one the story expects, and compares equal; one the
      // engine laid out past the canvas' edge paints nothing in either
      // drawing. The mixed labels do paint.
      for (const label of labels) {
        expect(label.runs, `${chart} "${label.text}" expected`).toBeGreaterThan(0);
        expect(label.differing, `${chart} "${label.text}"`).toBe(0);
      }
      const painted = labels.filter((label) => label.painted > 0);
      expect(painted.length, `${chart} painted labels`).toBeGreaterThan(labels.length / 2);
      for (const text of ['1100 - النقدية', 'Cash - 1100', 'B2C - أفراد']) {
        for (const label of labels.filter((candidate) => candidate.text === text)) {
          expect(label.painted, `${chart} "${text}" paints`).toBeGreaterThan(0);
        }
      }
    }
  });
}
