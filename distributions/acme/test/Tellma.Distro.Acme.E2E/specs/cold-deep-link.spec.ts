// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';
import { signInAs, tmIsServerPath } from '@tellma/core-testing/e2e';

import { expect, headings, recordHeadings, test } from '../support/acme';

// Stable layout on a cold deep link (spec 0024 §14): the route skeleton holds
// until the launch documents arrive, so the link renders its own page and
// never flashes another one, a forbidden state or the picker first. A details
// link renders its record (spec 0031 §3.3), and its edit mode is the client's
// alone: Edit, then Cancel, calls no server.

/** A customer as the tenant's query returns it: the id and the names in each of its languages. */
interface AcmeCustomer {
  /** The id. */
  readonly id: number;
  /** The names in the tenant's two languages, the absent one left out. */
  readonly names: readonly string[];
}

/**
 * A pattern that matches the text exactly.
 *
 * @param text - The text.
 * @returns The text with every pattern character escaped.
 */
function literal(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Finds a customer by its code through the tenant's API, as the app calls it.
 *
 * @param page - A page of the app, signed in, whose own calls give the client header.
 * @param client - The `Tellma-Client` header the app sends.
 * @param code - The customer's code.
 * @returns The customer.
 */
async function customerByCode(page: Page, client: string, code: string): Promise<AcmeCustomer> {
  return page.evaluate(
    async ({ client, code }) => {
      const response = await fetch('/1/api/web/customers/query', {
        method: 'POST',
        headers: { 'Tellma-Client': client, 'Content-Type': 'application/json' },
        body: JSON.stringify({
          select: 'Id,Name,Name2',
          filters: [`Code = '${code}'`],
          take: 1,
          includeInactive: true,
        }),
      });
      const text = await response.text();
      const page = JSON.parse(text) as { rows?: { rows: [number, ...(string | null)[]][] } };
      if (page.rows?.rows[0] === undefined) {
        throw new Error(`The customers query answered ${response.status}: ${text.slice(0, 600)}`);
      }
      const [id, ...names] = page.rows.rows[0];
      return { id, names: names.filter((name): name is string => name !== null) };
    },
    { client, code },
  );
}

test.describe('cold deep link', () => {
  test('a cold deep link renders its page and never another first', async ({
    page,
    text,
    cast,
  }) => {
    await recordHeadings(page.context());
    await signInAs(page, cast.user);

    await page.goto('/1/list/customers');

    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    expect(await headings(page)).toEqual([text.customers]);
    await expect(page).toHaveURL('/1/list/customers');
  });

  test('a cold deep link to a details page renders the record, and Edit then Cancel calls no server', async ({
    page,
    text,
    cast,
    lang,
    newContext,
  }) => {
    // --- The record's id, found in another context so the test's own stays cold ---
    const finder = await (await newContext()).newPage();
    await signInAs(finder, cast.user);
    const call = finder.waitForRequest((request) => request.url().includes('/1/api/web/'));
    await finder.goto('/1');
    const customer = await customerByCode(
      finder,
      (await call).headers()['tellma-client']!,
      'C-001',
    );

    await recordHeadings(page.context());
    await signInAs(page, cast.user);
    await page.goto(`/1/customers/${customer.id}`);

    // The title is the record's display name: its code, then its name in the user's language.
    const title = page.getByRole('heading', { level: 1 });
    await expect(title).toHaveText(
      new RegExp(`^C-001 - (?:${customer.names.map(literal).join('|')})$`),
    );
    await expect(page.getByRole('link', { name: text.customers })).toBeVisible();
    expect(await headings(page)).toEqual([(await title.textContent())!.trim()]);
    await expect(page).toHaveURL(`/1/customers/${customer.id}`);

    // --- Edit, then Cancel: the record's working copy is the client's own ---
    const edit = page.getByRole('button', { name: text.edit, exact: true });
    if (lang === 'ar') {
      // The Arabic project's user, the sales representative, only reads customers.
      await expect(edit).toBeDisabled();
      return;
    }
    // Every request for data counts, whatever its route: the round trip sends
    // none. The launch documents' sync is left out: the cache refresher runs
    // it on its own schedule (a response header, the leader, a poll), not for
    // the page.
    const calls: string[] = [];
    page.on('request', (request) => {
      const path = new URL(request.url()).pathname;
      if (tmIsServerPath(path) && !path.endsWith('/client/sync')) {
        calls.push(request.url());
      }
    });
    await edit.click();
    await expect(page.getByRole('button', { name: text.save, exact: true })).toBeVisible();
    await page.getByRole('button', { name: text.cancel, exact: true }).click();
    await expect(edit).toBeFocused();
    expect(calls).toEqual([]);
  });
});
