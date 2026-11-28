// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';
import { signInAs } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';

// Saving a customer on its details page (spec 0031 §8, §9.1) against the
// real server: an edit's Save writes the record and shows the saved copy in
// view mode with focus on Edit; and two contexts editing the same customer
// meet the concurrency check, where Overwrite writes the second edit over
// the first. Each flow edits a customer of its own, so the flows run in
// parallel and again on a database a previous run kept.

/** A customer as the tenant's query returns it: the id and the credit limit. */
interface AcmeCustomerLimit {
  /** The id. */
  readonly id: number;
  /** The credit limit, as a number. */
  readonly creditLimit: number;
}

/**
 * Signs in and opens tenant 1, capturing the `Tellma-Client` header the app
 * sends, which a direct call to the API needs.
 *
 * @param page - A fresh page.
 * @param user - The principal.
 * @returns The header's value.
 */
async function openTenant(page: Page, user: string): Promise<string> {
  await signInAs(page, user);
  const call = page.waitForRequest((request) => request.url().includes('/1/api/web/'));
  await page.goto('/1');
  return (await call).headers()['tellma-client']!;
}

/**
 * Reads a customer's id and credit limit by its code through the tenant's API, as the app calls it.
 *
 * @param page - A page of the app, signed in.
 * @param client - The `Tellma-Client` header the app sends.
 * @param code - The customer's code.
 * @returns The customer.
 */
async function customerByCode(
  page: Page,
  client: string,
  code: string,
): Promise<AcmeCustomerLimit> {
  return page.evaluate(
    async ({ client, code }) => {
      const response = await fetch('/1/api/web/customers/query', {
        method: 'POST',
        headers: { 'Tellma-Client': client, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          select: 'Id,CreditLimit',
          filters: [`Code = '${code}'`],
          take: 1,
          includeInactive: true,
        }),
      });
      const text = await response.text();
      const page = JSON.parse(text) as { rows?: { rows: [number, string | number | null][] } };
      const row = page.rows?.rows[0];
      if (row === undefined) {
        throw new Error(`The customers query answered ${response.status}: ${text.slice(0, 600)}`);
      }
      return { id: row[0], creditLimit: Number(row[1] ?? 0) };
    },
    { client, code },
  );
}

/**
 * A credit limit no earlier run left on the customer: the clock's
 * milliseconds within a day, with two decimals.
 *
 * @param offset - Added, so two edits in one flow differ.
 * @returns The limit.
 */
function freshLimit(offset = 0): number {
  return ((Date.now() + offset) % 86_400_000) / 100;
}

/**
 * Opens a customer's page, enters edit mode and types a credit limit.
 *
 * @param page - The page, signed in.
 * @param id - The customer's id.
 * @param limit - The credit limit typed.
 * @param text - The project's Edit button text.
 */
async function editLimit(
  page: Page,
  id: number,
  limit: number,
  text: { readonly edit: string; readonly save: string },
): Promise<void> {
  await page.goto(`/1/customers/${id}`);
  await page.getByRole('button', { name: text.edit, exact: true }).click();
  await expect(page.getByRole('button', { name: text.save, exact: true })).toBeVisible();
  const input = page.locator('[data-tm-field="CreditLimit"] input');
  await input.fill(String(limit));
  await input.blur();
}

test.describe('details save', () => {
  test.beforeEach(({ lang }) => {
    // The Arabic project's user, the sales representative, only reads customers (spec 0018 §6.3's
    // `Sales` grants): its Edit is disabled, as the cold deep link's flow checks.
    test.skip(lang === 'ar', "The Arabic project's user only reads customers.");
  });

  test('saves an edit, showing the saved record in view mode with focus on Edit', async ({
    page,
    text,
    cast,
  }) => {
    const client = await openTenant(page, cast.user);
    const { id } = await customerByCode(page, client, 'C-011');
    const limit = freshLimit();

    await editLimit(page, id, limit, text);
    const saved = page.waitForResponse((response) =>
      new URL(response.url()).pathname.endsWith('/customers/save'),
    );
    await page.getByRole('button', { name: text.save, exact: true }).click();
    expect((await saved).status()).toBe(200);

    const edit = page.getByRole('button', { name: text.edit, exact: true });
    await expect(edit).toBeFocused();
    await expect(page.locator('.tm-details__live')).toHaveText('Saved');
    expect((await customerByCode(page, client, 'C-011')).creditLimit).toBe(limit);
  });

  test('meets a newer version saved in another context, and Overwrite writes over it', async ({
    page,
    text,
    cast,
    newContext,
  }) => {
    const client = await openTenant(page, cast.user);
    const { id } = await customerByCode(page, client, 'C-012');
    const other = await (await newContext()).newPage();
    await openTenant(other, cast.user);
    const first = freshLimit();
    const second = freshLimit(1_000);

    // --- Both contexts edit the record; the other saves first ---
    await editLimit(page, id, second, text);
    await editLimit(other, id, first, text);
    await other.getByRole('button', { name: text.save, exact: true }).click();
    await expect(other.getByRole('button', { name: text.edit, exact: true })).toBeFocused();

    // --- This context's Save meets the newer version: the dialog asks, its Reload focused ---
    await page.getByRole('button', { name: text.save, exact: true }).click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toContainText('This record changed while you were editing');
    // The same user saved it in the other context, which the dialog says.
    await expect(dialog).toContainText('You saved this record in another tab or window');
    await expect(dialog.getByRole('button', { name: 'Reload saved version' })).toBeFocused();

    // --- Overwrite writes this context's edit over the other's ---
    const overwrite = page.waitForResponse((response) =>
      new URL(response.url()).pathname.endsWith('/customers/save'),
    );
    await dialog.getByRole('button', { name: 'Overwrite' }).click();
    expect((await overwrite).status()).toBe(200);
    await expect(page.getByRole('button', { name: text.edit, exact: true })).toBeFocused();
    expect((await customerByCode(page, client, 'C-012')).creditLimit).toBe(second);
  });
});
