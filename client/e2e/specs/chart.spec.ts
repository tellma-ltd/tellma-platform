// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { useExclusiveClipboard } from '../support/clipboard-lock';
import { near, pixelsOf, resolvedColour, type Rgb, type TmPixels } from '../support/pixels';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-chart: the axe floor on the plots, the card and
 * the table view in both themes and directions; each type drawn in its
 * token colours in light, dark and right-to-left, the plot never
 * mirroring; drilling from a slice and from a table row; no bidi
 * character in the page's text, names, titles or the clipboard; and the
 * engine loading only with the first plot.
 */

/** The story's charts drawn by the engine. */
const PLOTS = ['column', 'bar', 'line', 'area', 'pie', 'doughnut'] as const;

/** A string only the engine's code holds: the attribute ECharts marks its root element with. */
const ENGINE_MARK = '_echarts_instance_';

/** Any bidi control character. */
const BIDI = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/;

/** A chart of the story by its test id's suffix. */
function chart(page: Page, name: string): Locator {
  return page.getByTestId(`chart-${name}`);
}

/** Opens the chart story and waits until every plot has drawn. */
async function open(page: Page, options: Parameters<typeof storyUrl>[1] = {}): Promise<void> {
  await page.goto(storyUrl('chart', options));
  for (const name of [...PLOTS, 'arabic']) {
    await expect(chart(page, name).locator('canvas').first()).toBeVisible();
  }
  await appStable(page);
}

/** A chart's canvas, scrolled into view: a screenshot or a click only reaches the viewport. */
async function canvasOf(page: Page, name: string): Promise<Locator> {
  const canvas = chart(page, name).locator('canvas').first();
  await canvas.scrollIntoViewIfNeeded();
  return canvas;
}

/** The pixels' positions near a colour, as x coordinates, in the rows above `below`. */
function columnsOf(pixels: TmPixels, colour: Rgb, tolerance = 12, below = pixels.height): number[] {
  const xs: number[] = [];
  for (let y = 0; y < below; y++) {
    for (let x = 0; x < pixels.width; x++) {
      if (near(pixels.at(x, y), colour, tolerance)) {
        xs.push(x);
      }
    }
  }
  return xs;
}

test.describe('accessibility', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`the plots, the card and the table views pass axe (${theme}, ${dir})`, async ({ page }) => {
        await open(page, { theme, dir });
        await expectNoAxeViolations(page);
        await page.getByTestId('chart-toggle-table').click();
        await page.getByTestId('chart-arabic-table').click();
        await expect(chart(page, 'column').getByRole('grid')).toBeVisible();
        await expect(chart(page, 'arabic').getByRole('grid')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }

  test('names and describes the plot, out of the tab order', async ({ page }) => {
    await open(page);
    const image = chart(page, 'column').getByRole('img');
    await expect(image).toHaveAccessibleName('Sales by month, column chart');
    await expect(image).toHaveAccessibleDescription(
      '18 points, from 40,000 to 89,053. Use Show as table to read every value.',
    );
    await expect(image).not.toHaveAttribute('tabindex');
    await expect(chart(page, 'pie').getByRole('img')).toHaveAccessibleName('Sales by customer type, pie chart');
  });
});

test.describe('drawing', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`draws every type in the chart tokens’ colours (${theme})`, async ({ page }) => {
      await open(page, { theme });
      const series1 = await resolvedColour(page, 'var(--chart-series-1)');
      const series2 = await resolvedColour(page, 'var(--chart-series-2)');
      const ramp7 = await resolvedColour(page, 'var(--chart-sequential-7)');
      const expected: Record<(typeof PLOTS)[number], readonly Rgb[]> = {
        column: [series1, series2],
        // The diverging palette: positive balances in series 1, the negative one in series 2.
        bar: [series1, series2],
        line: [series1, series2],
        area: [series1],
        pie: [series1, series2],
        doughnut: [ramp7],
      };
      for (const name of PLOTS) {
        const pixels = await pixelsOf(page, await canvasOf(page, name));
        for (const colour of expected[name]) {
          expect(columnsOf(pixels, colour).length, `${name} in ${JSON.stringify(colour)}`).toBeGreaterThan(40);
        }
      }
    });
  }

  test('redraws in the dark colours when the theme switches live', async ({ page }) => {
    await open(page, { theme: 'light' });
    const light = await resolvedColour(page, 'var(--chart-series-1)');
    const before = await pixelsOf(page, await canvasOf(page, 'column'));
    expect(columnsOf(before, light).length).toBeGreaterThan(40);
    // The shell's toggle repaints the page in place: no reload.
    await page.getByTestId('toggle-theme').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
    const dark = await resolvedColour(page, 'var(--chart-series-1)');
    expect(near(light, dark, 4)).toBe(false);
    await expect
      .poll(async () => columnsOf(await pixelsOf(page, await canvasOf(page, 'column')), dark).length)
      .toBeGreaterThan(40);
    const after = await pixelsOf(page, await canvasOf(page, 'column'));
    expect(columnsOf(after, light, 4).length).toBe(0);
  });

  for (const [setting, media] of [
    ['forced colours', { forcedColors: 'active' }],
    ['more contrast', { contrast: 'more' }],
  ] as const) {
    test(`patterns one series' marks when the page asks for ${setting}, live`, async ({ page }) => {
      // One series needs no pattern until the reader's settings ask for one:
      // the pattern then covers part of every column's fill.
      await page.goto(storyUrl('chart-lazy'));
      await page.getByTestId('chart-lazy-add').click();
      const canvas = await canvasOf(page, 'lazy-column');
      await expect(canvas).toBeVisible();
      await appStable(page);
      const colour = await resolvedColour(page, 'var(--chart-series-1)');
      const plain = columnsOf(await pixelsOf(page, canvas), colour, 4).length;
      expect(plain).toBeGreaterThan(200);
      await page.emulateMedia(media);
      await expect
        .poll(async () => columnsOf(await pixelsOf(page, canvas), colour, 4).length)
        .toBeLessThan(plain * 0.8);
    });
  }

  test('does not mirror the plot in right-to-left', async ({ page }) => {
    // The first month's columns stay at the left: the categories run left
    // to right in both directions. Only the rows above the bottom quarter
    // count: the legend's swatches below the axis take the series' colours
    // and do move, to the right-hand side in right-to-left.
    const firstColumn: number[] = [];
    for (const dir of ['ltr', 'rtl'] as const) {
      await open(page, { dir });
      const colour = await resolvedColour(page, 'var(--chart-series-1)');
      const pixels = await pixelsOf(page, await canvasOf(page, 'column'));
      firstColumn.push(Math.min(...columnsOf(pixels, colour, 12, Math.round(pixels.height * 0.75))));
    }
    expect(Math.abs(firstColumn[0]! - firstColumn[1]!)).toBeLessThanOrEqual(2);
  });

  test('gives the canvas the page’s direction inside a left-to-right figure', async ({ page }) => {
    await open(page, { dir: 'rtl' });
    const directions = await chart(page, 'column').evaluate((host) => ({
      figure: getComputedStyle(host.querySelector('figure')!).direction,
      canvas: getComputedStyle(host.querySelector('canvas')!).direction,
    }));
    expect(directions).toEqual({ figure: 'ltr', canvas: 'rtl' });
  });
});

test.describe('drilling', () => {
  test('a slice drills to its member, Mod to a new tab', async ({ page }) => {
    await open(page);
    const box = (await (await canvasOf(page, 'pie')).boundingBox())!;
    // The first slice starts at 12 o'clock and runs clockwise; the second
    // starts past a third of the disc, so a point just right of the top
    // half's middle is in the first.
    const x = box.x + box.width / 2 + 8;
    const y = box.y + box.height / 2 - box.height / 5;
    await page.mouse.click(x, y);
    await expect(page.getByTestId('chart-activation')).toHaveText('0/-');
    await page.keyboard.down('ControlOrMeta');
    await page.mouse.click(x, y);
    await page.keyboard.up('ControlOrMeta');
    await expect(page.getByTestId('chart-activation')).toHaveText('0/- (new tab)');
  });

  test('a table row drills to its point on Enter', async ({ page }) => {
    await open(page);
    await page.getByTestId('chart-toggle-table').click();
    const grid = chart(page, 'column').getByRole('grid');
    await expect(grid).toBeVisible();
    await grid.getByRole('gridcell').first().click();
    await page.keyboard.press('ArrowDown');
    await page.keyboard.press('Enter');
    await expect(page.getByTestId('chart-activation')).toHaveText('0/1');
  });

  test('the card drills from its button', async ({ page }) => {
    await open(page);
    await chart(page, 'card').getByRole('button').click();
    await expect(page.getByTestId('chart-activation')).toHaveText('-/-');
  });
});

test.describe('text', () => {
  test('no bidi character reaches the page’s text, names or titles', async ({ page }) => {
    await open(page, { dir: 'rtl' });
    for (const view of ['plot', 'table'] as const) {
      if (view === 'table') {
        await page.getByTestId('chart-toggle-table').click();
        await page.getByTestId('chart-arabic-table').click();
        await expect(chart(page, 'arabic').getByRole('grid')).toBeVisible();
      }
      const found = await page.locator('tm-chart').evaluateAll((hosts) =>
        hosts.flatMap((host) => [
          host.textContent ?? '',
          ...[host, ...host.querySelectorAll('*')].flatMap((node) =>
            ['aria-label', 'title', 'aria-description'].map((name) => node.getAttribute(name) ?? ''),
          ),
        ]),
      );
      expect(found.filter((text) => BIDI.test(text)), view).toEqual([]);
    }
  });

  test.describe('the clipboard', () => {
    test.use({ permissions: ['clipboard-read', 'clipboard-write'] });
    useExclusiveClipboard();

    test('a copied Arabic value carries no bidi character', async ({ page, browserName }) => {
      test.skip(browserName !== 'chromium', 'The real clipboard is read through Chromium permissions.');
      await open(page, { dir: 'rtl' });
      await page.getByTestId('chart-arabic-table').click();
      const grid = chart(page, 'arabic').getByRole('grid');
      await expect(grid).toBeVisible();
      // The second point: a negative value in Arabic-Indic digits.
      await grid.getByRole('gridcell').first().click();
      await page.keyboard.press('ArrowDown');
      await page.keyboard.press('End');
      await page.keyboard.press('ControlOrMeta+C');
      await expect
        .poll(() => page.evaluate(() => navigator.clipboard.readText()))
        .toMatch(/[٠-٩]/);
      const text = await page.evaluate(() => navigator.clipboard.readText());
      expect(BIDI.test(text), JSON.stringify(text)).toBe(false);
    });
  });
});

test.describe('the engine', () => {
  test('loads with the first plot, not with a card', async ({ page }) => {
    // Built chunks carry no module names, so a script is the engine's when
    // its code holds the attribute ECharts marks its root with.
    const scripts: Promise<boolean>[] = [];
    page.on('response', (response) => {
      if (response.request().resourceType() === 'script') {
        scripts.push(response.text().then((code) => code.includes(ENGINE_MARK), () => false));
      }
    });
    await page.goto(storyUrl('chart-lazy'));
    await expect(chart(page, 'lazy-card').getByText('42')).toBeVisible();
    await appStable(page);
    const before = scripts.length;
    expect(before).toBeGreaterThan(0);
    expect(await Promise.all(scripts), 'the engine loaded with the card').not.toContain(true);
    await page.getByTestId('chart-lazy-add').click();
    await expect(chart(page, 'lazy-column').locator('canvas').first()).toBeVisible();
    // The plot fetched the engine's chunk, which the card had not.
    expect(await Promise.all(scripts.slice(before)), 'the engine loaded with the plot').toContain(true);
  });
});
