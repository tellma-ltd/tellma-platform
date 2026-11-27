// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs } from '@tellma/core-testing/e2e';
import type { Locator, Page } from '@playwright/test';

import { expect, holdSelectChunks, test } from '../support/acme';

// The search page over the sample customers (spec 0030 §3, §7, §14): it
// lists the real rows the server reads, a header click orders them and a
// second one reverses the order, each in place of the address, and the
// pager reads the next page as a history entry of its own.

/**
 * The rendered data rows of the page's grid.
 *
 * @param page - The page.
 * @returns The rows.
 */
function rows(page: Page): Locator {
  return page.getByRole('grid').locator('[role="rowgroup"] [role="row"]');
}

/**
 * The first rendered row's code: the text of its cell in the Code column.
 *
 * @param page - The page.
 * @returns The code.
 */
async function firstCode(page: Page): Promise<string> {
  const column = await page
    .getByRole('grid')
    .locator('[role="columnheader"][data-key="Code"]')
    .getAttribute('data-col');
  return (
    await rows(page).first().locator(`[role="gridcell"][data-col="${column}"]`).innerText()
  ).trim();
}

test.describe('search page', () => {
  test('lists the customers, orders them by a header and pages through them', async ({
    page,
    text,
    cast,
  }) => {
    await signInAs(page, cast.user);
    await page.goto('/1/customers');
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    await expect(rows(page).first()).toBeVisible();

    // --- A header click orders by the column, then reverses it, replacing the address ---
    const code = page.getByRole('grid').locator('[role="columnheader"][data-key="Code"]');
    await code.click();
    await expect(page).toHaveURL('/1/customers?order=Code');
    await expect.poll(() => firstCode(page)).toBe('C-001');
    await code.click();
    await expect(page).toHaveURL('/1/customers?order=Code:desc');
    await expect.poll(() => firstCode(page)).toBe('C-060');

    // --- The next page is a history entry of its own, and Back returns to the first ---
    await page.locator('tm-pager .tm-pager__next').click();
    await expect(page).toHaveURL('/1/customers?order=Code:desc&page=2');
    await expect.poll(() => rows(page).count()).toBeLessThan(50);
    await page.goBack();
    await expect(page).toHaveURL('/1/customers?order=Code:desc');
    await expect.poll(() => firstCode(page)).toBe('C-060');
  });

  test.describe('with the worker blocked', () => {
    // The worker claims the page once it activates and answers the lazy files
    // from its own cache, past the page's routes: the hold needs it blocked.
    test.use({ serviceWorkers: 'block' });

    // The pager's page-size select loads on demand: a click on its stand-in
    // before it lands opens it when it lands, and the size it picks pages.
    test('a click on the page size before its select loads opens it when it loads', async ({
      page,
      text,
      cast,
    }) => {
      await signInAs(page, cast.user);
      const select = await holdSelectChunks(page);
      await page.goto('/1/customers');
      await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
      await expect(rows(page).first()).toBeVisible();

      const standIn = page.locator('tm-pager .tm-pager__proxy');
      await standIn.click();
      await expect(standIn).toBeFocused();
      await expect(page.locator('tm-pager tm-select')).toHaveCount(0);

      await select.release();
      await expect(standIn).toHaveCount(0);
      const trigger = page.locator('tm-pager tm-select [role="combobox"]');
      await expect(trigger).toBeFocused();
      await expect(trigger).toHaveAttribute('aria-expanded', 'true');
      await page.getByRole('option').first().click();
      await expect(page).toHaveURL('/1/customers?size=20');
      await expect.poll(() => rows(page).count()).toBeLessThanOrEqual(20);
    });
  });
});
