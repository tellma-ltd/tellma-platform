// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test } from '@playwright/test';

import { gotoGrid, gridScroller } from '../support/grid';
import { wheelScreensForLongTasks } from '../support/scroll-probe';
import { appStable } from '../support/settle';

// The grid's scroll budget, in the nightly tier: scrolling produces no long
// task over 50 ms (spec 0004 §16), on the flat readonly grid with its twelve
// columns and on a fully expanded tree, where most rendered rows draw an
// expander glyph (spec 0010 §C.13). Each screen-sized wheel step brings in
// a whole window of new rows.
test.describe('the grid under scroll', () => {
  test.beforeEach(({ browserName }) => {
    test.skip(browserName !== 'chromium', 'Long-task timing is a Chromium API.');
  });

  test('@perf scrolling 100,000 rows of twelve columns produces no long task over 50 ms', async ({
    page,
  }) => {
    await gotoGrid(page, 'grid-readonly');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '100001');
    await appStable(page);

    const longTasks = await wheelScreensForLongTasks(page, gridScroller(page), 40);
    expect(longTasks.filter((duration) => duration > 50)).toEqual([]);
  });

  test('@perf scrolling a 21,000-row expanded tree produces no long task over 50 ms', async ({
    page,
  }) => {
    await gotoGrid(page, 'tree-grid-large');
    await expect(gridScroller(page)).toHaveAttribute('aria-rowcount', '21001');
    await expect(page.locator('[data-tm-expander]').first()).toBeVisible();
    await appStable(page);

    const longTasks = await wheelScreensForLongTasks(page, gridScroller(page), 40);
    expect(longTasks.filter((duration) => duration > 50)).toEqual([]);
  });
});
