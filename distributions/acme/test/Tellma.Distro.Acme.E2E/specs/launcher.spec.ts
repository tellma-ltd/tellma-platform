// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Page } from '@playwright/test';
import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';
import { SHELL_TEXT, activeOption, launcherField } from '../support/shell';

// The launcher (spec 0027 §9, §13) against the real distribution: Mod+K
// opens it on the app bar's field, from the page, a grid cell or a text
// field; typing "cent" lists Centers first (in Arabic, by its English
// label), and Enter opens it; a synonym in the project's language finds
// Customers; the arrows move across the sections, mapped to the reading
// direction; Escape closes it and keeps the text; and Acme's own entries,
// the Sales and Financials sections, the credit-review page and the
// sales-invoice bucket, are where Acme declares them. The launcher's panel
// is a chunk of its own, which the shell loads once the browser is idle, so
// the first open finds it in memory.

/**
 * Opens the launcher with Mod+K and checks that its field took the focus.
 *
 * @param page - The page.
 */
async function openLauncher(page: Page): Promise<void> {
  await page.keyboard.press('ControlOrMeta+K');
  await expect(launcherField(page)).toBeFocused();
  await expect(launcherField(page)).toHaveAttribute('aria-expanded', 'true');
  await expect(page.getByRole('listbox')).toBeVisible();
}

/**
 * The browse state's section groups, in the panel's order.
 *
 * @param page - The page.
 * @returns Their ids, each its heading's id.
 */
function sectionIds(page: Page): Promise<string[]> {
  return page
    .getByRole('listbox')
    .locator('[role="group"].tm-launcher__group--section')
    .evaluateAll((groups) => groups.map((group) => group.getAttribute('aria-labelledby')!));
}

/**
 * The group the active option is in, or an empty string with none active.
 *
 * @param page - The page.
 * @returns Its heading's id.
 */
async function activeGroup(page: Page): Promise<string> {
  if ((await activeOption(page).count()) === 0) {
    return '';
  }
  return activeOption(page).evaluate((option) =>
    option.closest('[role="group"]')!.getAttribute('aria-labelledby')!,
  );
}

test.describe('the launcher', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');
  });

  test('Mod+K, then "cent" and Enter, opens Centers', async ({ page }) => {
    await page.goto('/1');
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toBeVisible();
    const field = page.locator('tm-app-bar .tm-app-bar__field input');

    await page.keyboard.press('ControlOrMeta+K');
    await expect(field).toBeFocused();
    await expect(field).toHaveAttribute('aria-expanded', 'true');
    await expect(page.getByRole('listbox')).toBeVisible();

    await page.keyboard.type('cent');
    // The first row is active, and the input points at it.
    const active = page.getByRole('listbox').locator('[role="option"][aria-selected="true"]');
    await expect(active).toHaveAttribute('href', '/1/centers');
    await expect(field).toHaveAttribute(
      'aria-activedescendant',
      (await active.getAttribute('id'))!,
    );

    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/1\/centers$/);
    await expect(page.getByRole('listbox')).toHaveCount(0);
    // The text stays for the next open.
    await expect(field).toHaveValue('cent');
  });

  test('Mod+K opens it from a grid cell and from a text field', async ({ page, text }) => {
    await page.goto('/1/customers');
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();

    // A grid cell holds the focus: Mod+K is the shell's, not the grid's.
    await page.getByRole('grid').locator('[role="gridcell"]').first().click();
    await openLauncher(page);
    await page.keyboard.press('Escape');
    await expect(page.getByRole('listbox')).toHaveCount(0);

    // A text field holds the focus and its text: Mod+K still opens the launcher.
    const search = page.locator('tm-search-page-field input');
    await search.click();
    await search.pressSequentially('ac');
    await openLauncher(page);
    await expect(search).toHaveValue('ac');
  });

  test("a synonym in the project's language finds Customers", async ({ page, lang }) => {
    await page.goto('/1');
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toBeVisible();
    await openLauncher(page);

    await page.keyboard.type(SHELL_TEXT[lang].clients);
    await expect(activeOption(page)).toHaveAttribute('href', '/1/customers');
  });

  test('the arrows move across the sections in the reading direction', async ({ page }) => {
    await page.goto('/1');
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toBeVisible();
    await openLauncher(page);
    const sections = await sectionIds(page);
    expect(sections.length, 'the browse state shows two sections or more').toBeGreaterThan(1);

    // PageDown steps a group at a time, onto the first section's first row.
    for (let step = 0; step < 6 && (await activeGroup(page)) !== sections[0]; step++) {
      await page.keyboard.press('PageDown');
    }
    expect(await activeGroup(page)).toBe(sections[0]);

    // The key toward the reading direction's end moves to the next section, the other one back.
    const rtl = (await page.evaluate(() => document.documentElement.dir)) === 'rtl';
    const [toEnd, toStart] = rtl ? ['ArrowLeft', 'ArrowRight'] : ['ArrowRight', 'ArrowLeft'];
    await page.keyboard.press(toEnd);
    expect(await activeGroup(page)).toBe(sections[1]);
    await page.keyboard.press(toStart);
    expect(await activeGroup(page)).toBe(sections[0]);
  });

  test('Escape closes it and keeps the text for the next open', async ({ page }) => {
    await page.goto('/1');
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toBeVisible();
    await openLauncher(page);
    await page.keyboard.type('cust');
    await expect(activeOption(page)).toHaveAttribute('href', '/1/customers');

    await page.keyboard.press('Escape');
    await expect(page.getByRole('listbox')).toHaveCount(0);
    await expect(launcherField(page)).toHaveValue('cust');

    await openLauncher(page);
    await expect(activeOption(page)).toHaveAttribute('href', '/1/customers');
  });

  test("Acme's sections hold its pages, an invoice's number finds it, and the credit review opens", async ({
    page,
    lang,
  }) => {
    const shell = SHELL_TEXT[lang];
    await page.goto('/1');
    await expect(page.locator('tm-app-bar .tm-app-bar__tenant')).toBeVisible();
    await openLauncher(page);

    // --- Browse: Sales holds the customers and the invoices, Financials the credit review ---
    const list = page.getByRole('listbox');
    const sales = list.getByRole('group', { name: shell.sales, exact: true });
    await expect(sales.locator('[role="option"][href="/1/customers"]')).toHaveCount(1);
    await expect(sales.locator('[role="option"][href="/1/sales-invoices"]')).toHaveCount(1);
    const financials = list.getByRole('group', { name: shell.financials, exact: true });
    await expect(financials.locator('[role="option"][href="/1/credit-review"]')).toHaveCount(1);

    // --- Typed: an invoice's serial number fills the invoices' bucket from the server ---
    await page.keyboard.type('1');
    const bucket = list.getByRole('group', { name: shell.salesInvoices, exact: true });
    await expect(bucket.getByRole('option').first()).toBeVisible();

    // --- The page's synonym finds it, and Enter opens it under its title ---
    await launcherField(page).fill('');
    await page.keyboard.type(shell.creditSynonym);
    await expect(activeOption(page)).toHaveAttribute('href', '/1/credit-review');
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/1\/credit-review$/);
    await expect(page.getByRole('heading', { level: 1, name: shell.creditReview })).toBeVisible();
  });
});
