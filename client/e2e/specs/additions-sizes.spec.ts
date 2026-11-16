// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Locator, type Page } from '@playwright/test';

import { storyUrl } from '../support/story-map';

/**
 * Resolved sizes of the library additions: every sized control left
 * without a size takes the large touch size on a 390 px phone and the
 * workspace's small default from 576 px up, in both what it reports and
 * the geometry it lays out — field heights of 46 and 30 px, option rows of
 * 34 and 26 px, chips of 28 and 20 px.
 */

/** One unsized control, how it reports its resolved size, and the box that size sets. */
interface SizedControl {
  /** The control, for the test title. */
  readonly name: string;
  /** The story that shows it. */
  readonly story: string;
  /** The control's host. */
  readonly host: (page: Page) => Locator;
  /** How the host reports its size: a class prefix (`<prefix>--lg`) or `data-size`. */
  readonly reports: { readonly classPrefix: string } | 'data-size';
  /** The element whose height the size sets. */
  readonly box: (host: Locator) => Locator;
  /** That height at the large and the small size. */
  readonly heights: { readonly lg: number; readonly sm: number };
}

/** Field heights: the large touch size and the small workspace default. */
const FIELD = { lg: 46, sm: 30 } as const;

/** Option-row heights at the same two sizes. */
const OPTION_ROW = { lg: 34, sm: 26 } as const;

/** Chip heights at the same two sizes. */
const CHIP = { lg: 28, sm: 20 } as const;

/** Every additions control whose size follows the viewport when it is left unsized. */
const CONTROLS: readonly SizedControl[] = [
  {
    name: 'a segmented control',
    story: 'segmented',
    host: (page) => page.getByTestId('seg-period'),
    reports: { classPrefix: 'tm-segmented' },
    box: (host) => host.locator('.tm-segmented__track'),
    heights: FIELD,
  },
  {
    name: 'a radio group',
    story: 'radio',
    host: (page) => page.getByTestId('radio-terms'),
    reports: { classPrefix: 'tm-radio-group' },
    box: (host) => host.locator('.tm-radio-group__row').first(),
    heights: OPTION_ROW,
  },
  {
    name: 'a tree',
    story: 'tree',
    host: (page) => page.getByTestId('tree-fields'),
    reports: 'data-size',
    box: (host) => host.locator('.tm-tree__row').first(),
    heights: OPTION_ROW,
  },
  {
    name: 'a reorder list',
    story: 'reorder-list',
    host: (page) => page.getByTestId('reorder-columns'),
    reports: 'data-size',
    box: (host) => host.locator('.tm-reorder-list__row').first(),
    heights: OPTION_ROW,
  },
  {
    name: 'a chip',
    story: 'chip',
    host: (page) => page.getByTestId('set-wrap').locator('tm-chip').first(),
    reports: 'data-size',
    box: (host) => host,
    heights: CHIP,
  },
  {
    name: 'a pager',
    story: 'pager',
    host: (page) => page.getByTestId('pager-range'),
    reports: 'data-size',
    box: (host) => host.locator('.tm-pager__button').first(),
    heights: FIELD,
  },
  {
    name: 'a date range',
    story: 'date-range',
    host: (page) => page.getByTestId('range-period').locator('tm-form-field').first(),
    reports: { classPrefix: 'tm-form-field' },
    box: (host) => host.locator('.tm-form-field__box'),
    heights: FIELD,
  },
  {
    name: 'a date-time field',
    story: 'date-time',
    host: (page) => page.getByTestId('dt-beirut'),
    reports: { classPrefix: 'tm-date-time-field' },
    box: (host) => host.locator('.tm-form-field__box').first(),
    heights: FIELD,
  },
  {
    name: 'a search field',
    story: 'search-field',
    host: (page) => page.getByTestId('search-main').locator('tm-form-field').first(),
    reports: { classPrefix: 'tm-form-field' },
    box: (host) => host.locator('.tm-form-field__box'),
    heights: FIELD,
  },
  {
    name: 'a copy button',
    story: 'copy',
    // The labelled button: an icon-only one is square at the same height.
    host: (page) => page.getByTestId('copy-label').getByRole('button'),
    reports: { classPrefix: 'tm-button' },
    box: (host) => host,
    heights: FIELD,
  },
];

/** The two viewports either side of the sm breakpoint, and the size each resolves to. */
const VIEWPORTS = [
  { width: 390, height: 844, size: 'lg' },
  { width: 768, height: 1024, size: 'sm' },
] as const;

for (const control of CONTROLS) {
  for (const viewport of VIEWPORTS) {
    test(`${control.name} left unsized is ${viewport.size} at ${viewport.width} px`, async ({
      page,
    }) => {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.goto(storyUrl(control.story));
      const host = control.host(page);
      await expect(host).toBeVisible();
      if (control.reports === 'data-size') {
        await expect(host).toHaveAttribute('data-size', viewport.size);
      } else {
        await expect(host).toHaveClass(
          new RegExp(`(^|\\s)${control.reports.classPrefix}--${viewport.size}(\\s|$)`),
        );
      }
      const box = control.box(host);
      await expect
        .poll(async () => (await box.boundingBox())?.height)
        .toBe(control.heights[viewport.size]);
    });
  }
}
