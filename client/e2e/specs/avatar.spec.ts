// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * Browser battery for tm-avatar: the axe floor with every tone in both
 * themes and directions, the diameters of the size ladder, the initials per
 * script, the failed photo falling back to the initials in the same box,
 * and the tinted circle's edge under forced colours.
 */

test.describe('axe floor', () => {
  for (const theme of ['light', 'dark'] as const) {
    for (const dir of ['ltr', 'rtl'] as const) {
      test(`avatar story is axe-clean in every tone (${theme}, ${dir})`, async ({ page }) => {
        await page.goto(storyUrl('avatar', { theme, dir }));
        await expect(page.getByTestId('tone-8')).toBeVisible();
        await expect(page.getByTestId('photo-failed').locator('.tm-image__fallback')).toBeVisible();
        await expectNoAxeViolations(page);
      });
    }
  }
});

test.describe('geometry and initials', () => {
  test('each size is a circle of its diameter', async ({ page }) => {
    await page.goto(storyUrl('avatar'));
    for (const [size, diameter] of [
      ['xs', 20],
      ['sm', 24],
      ['md', 28],
      ['lg', 40],
      ['xl', 72],
    ] as const) {
      const box = (await page.getByTestId(`size-${size}`).boundingBox())!;
      expect(box.width).toBe(diameter);
      expect(box.height).toBe(diameter);
    }
  });

  test('two initials for a cased name, one for an uncased script, the glyph for none', async ({
    page,
  }) => {
    await page.goto(storyUrl('avatar'));
    await expect(page.getByTestId('latin')).toHaveText('FN');
    await expect(page.getByTestId('arabic')).toHaveText('ف');
    await expect(page.getByTestId('ethiopic')).toHaveText('ተ');
    await expect(page.getByTestId('cjk')).toHaveText('王');
    await expect(page.getByTestId('empty-name').locator('svg')).toBeVisible();
    await expect(page.getByTestId('latin')).toHaveAttribute('role', 'img');
    await expect(page.getByTestId('latin')).toHaveAttribute('aria-label', 'Farah Nasser');
    await expect(page.getByTestId('decorative-row').locator('tm-avatar')).toHaveAttribute(
      'aria-hidden',
      'true',
    );
  });

  test('a failed photo shows the initials in the same circle', async ({ page }) => {
    await page.goto(storyUrl('avatar'));
    const failed = page.getByTestId('photo-failed');
    await expect(failed.locator('.tm-image__fallback')).toHaveText('LH');
    const box = (await failed.boundingBox())!;
    expect(box.width).toBe(72);
    expect(box.height).toBe(72);
    await expect(page.getByTestId('photo').locator('img')).toBeVisible();
  });
});

test.describe('forced colours', () => {
  test('a tinted circle keeps a visible edge when the tint is forced away', async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' });
    await page.goto(storyUrl('avatar'));
    const edge = await page.getByTestId('tone-4').evaluate((el) => {
      const ring = getComputedStyle(el, '::after');
      return { style: ring.borderTopStyle, width: ring.borderTopWidth, color: ring.borderTopColor };
    });
    expect(edge.style).toBe('solid');
    expect(edge.width).toBe('1px');
    expect(edge.color).not.toBe('rgba(0, 0, 0, 0)');
  });
});
