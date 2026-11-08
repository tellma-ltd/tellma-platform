// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-icon: the axe floor in both themes and directions,
 * mirroring under a cold and a live right-to-left switch, the glyph present
 * in its control's first frame, the size ladder, the stroke token, and an
 * unknown name's empty box.
 */

const MIRRORED = 'matrix(-1, 0, 0, 1, 0, 0)';

/** The computed transform of an icon's glyph. */
function glyphTransform(icon: Locator): Promise<string> {
  return icon.locator(':scope > svg').evaluate((svg) => getComputedStyle(svg).transform);
}

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    test(`icon story is axe-clean (${theme})`, async ({ page }) => {
      await page.goto(storyUrl('icon', { theme }));
      await expect(page.getByTestId('gallery')).toBeVisible();
      await expectNoAxeViolations(page);
    });
  }

  test('icon story is axe-clean in RTL', async ({ page }) => {
    await page.goto(storyUrl('icon', { dir: 'rtl' }));
    await expect(page.getByTestId('gallery')).toBeVisible();
    await expectNoAxeViolations(page);
  });
});

test.describe('mirroring', () => {
  test('a directional glyph mirrors on a cold RTL load; others and a dir="ltr" island do not', async ({
    page,
  }) => {
    await page.goto(storyUrl('icon', { dir: 'rtl' }));
    await expect(page.getByTestId('mirrored').locator('svg')).toBeAttached();
    expect(await glyphTransform(page.getByTestId('mirrored'))).toBe(MIRRORED);
    expect(await glyphTransform(page.getByTestId('unmirrored'))).toBe('none');
    expect(await glyphTransform(page.getByTestId('island'))).toBe('none');
  });

  test('a live English → Arabic switch flips the glyph in place, and back', async ({ page }) => {
    await page.goto(storyUrl('icon'));
    const mirrored = page.getByTestId('mirrored');
    await expect(mirrored.locator('svg')).toBeAttached();
    expect(await glyphTransform(mirrored)).toBe('none');
    // The same element throughout: nothing re-renders, the direction changes.
    const before = await mirrored.locator('svg').elementHandle();

    await page.getByTestId('lang-ar').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect.poll(() => glyphTransform(mirrored)).toBe(MIRRORED);
    expect(await glyphTransform(page.getByTestId('unmirrored'))).toBe('none');
    expect(await glyphTransform(page.getByTestId('island'))).toBe('none');
    expect(await before!.evaluate((svg) => svg.isConnected)).toBe(true);

    await page.getByTestId('lang-en').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect.poll(() => glyphTransform(mirrored)).toBe('none');
  });
});

test.describe('first frame', () => {
  /**
   * Records, for every tm-icon the page inserts, whether its glyph is there
   * when the insertion is observed (before the task ends) and again in the
   * next animation frame (before that frame paints). An icon expected to be
   * empty (an unknown name) is skipped.
   */
  async function recordInsertions(page: Page): Promise<void> {
    await page.addInitScript(() => {
      const record = { checked: 0, missing: [] as string[] };
      (window as unknown as { __tmIconFrames: typeof record }).__tmIconFrames = record;
      const check = (icon: Element, when: string) => {
        if (icon.getAttribute('data-testid') === 'unknown') {
          return;
        }
        record.checked++;
        if (icon.querySelector(':scope > svg') === null) {
          record.missing.push(`${icon.outerHTML.slice(0, 120)} (${when})`);
        }
      };
      new MutationObserver((mutations) => {
        for (const mutation of mutations) {
          for (const node of Array.from(mutation.addedNodes)) {
            if (!(node instanceof Element)) {
              continue;
            }
            const icons = [
              ...(node.matches('tm-icon') ? [node] : []),
              ...Array.from(node.querySelectorAll('tm-icon')),
            ];
            for (const icon of icons) {
              check(icon, 'insertion');
              requestAnimationFrame(() => check(icon, 'next frame'));
            }
          }
        }
      }).observe(document, { childList: true, subtree: true });
    });
  }

  /** The record, after two frames so every scheduled frame check has run. */
  async function frames(page: Page): Promise<{ checked: number; missing: string[] }> {
    return page.evaluate(
      () =>
        new Promise((resolve) =>
          requestAnimationFrame(() =>
            requestAnimationFrame(() =>
              resolve(
                (window as unknown as { __tmIconFrames: { checked: number; missing: string[] } })
                  .__tmIconFrames,
              ),
            ),
          ),
        ),
    );
  }

  test('every icon on the page carries its glyph from the frame it first appears in', async ({
    page,
  }) => {
    await recordInsertions(page);
    await page.goto(storyUrl('icon'));
    await expect(page.getByTestId('gallery')).toBeVisible();
    const onLoad = await frames(page);
    expect(onLoad.missing).toEqual([]);
    expect(onLoad.checked).toBeGreaterThan(20);

    // Controls created on demand: twenty rows, one chevron each.
    await page.getByTestId('insert-icons').click();
    await expect(page.getByTestId('inserted').locator('tm-icon')).toHaveCount(20);
    const afterInsert = await frames(page);
    expect(afterInsert.missing).toEqual([]);
    // Each inserted icon is checked twice: at insertion and in the next frame.
    expect(afterInsert.checked).toBeGreaterThanOrEqual(onLoad.checked + 40);
  });
});

test.describe('geometry', () => {
  test('the size ladder, a container-set size and an unknown name keep their boxes', async ({
    page,
  }) => {
    await page.goto(storyUrl('icon'));
    await expect(page.getByTestId('sizes')).toBeVisible();
    for (const [size, px] of [
      ['xs', 12],
      ['sm', 14],
      ['md', 16],
      ['lg', 20],
      ['xl', 24],
    ] as const) {
      const box = (await page.getByTestId(`size-${size}`).boundingBox())!;
      expect(box.width, size).toBe(px);
      expect(box.height, size).toBe(px);
    }
    const containerSized = page.getByTestId('container-sized').locator('tm-icon');
    expect((await containerSized.boundingBox())!.width).toBe(28);

    const unknown = (await page.getByTestId('unknown').boundingBox())!;
    const known = (await page.getByTestId('known').boundingBox())!;
    expect(await page.getByTestId('unknown').locator('svg').count()).toBe(0);
    expect(unknown.width).toBe(known.width);
    expect(unknown.height).toBe(known.height);
  });

  test('glyphs stroke at 1.75 by default, and a host may raise it', async ({ page }) => {
    await page.goto(storyUrl('icon'));
    const stroke = (testId: string) =>
      page
        .getByTestId(testId)
        .locator(':scope > svg')
        .evaluate((svg) => getComputedStyle(svg).strokeWidth);
    expect(await stroke('stroke-default')).toBe('1.75px');
    expect(await stroke('stroke-heavy')).toBe('2.25px');
  });

  test('a labelled icon is an image named by its label; the rest are hidden', async ({ page }) => {
    await page.goto(storyUrl('icon'));
    await expect(page.getByRole('img', { name: 'Has errors' })).toBeVisible();
    await expect(page.getByTestId('mirrored')).toHaveAttribute('aria-hidden', 'true');
  });
});
