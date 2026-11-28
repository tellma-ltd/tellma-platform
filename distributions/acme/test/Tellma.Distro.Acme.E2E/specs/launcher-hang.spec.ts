// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Request } from '@playwright/test';
import { signInAs, warmUp } from '@tellma/core-testing/e2e';

import { expect, test } from '../support/acme';
import { activeOption, launcherField, sessionButton } from '../support/shell';

// A hung provider delays nothing (spec 0027 §9.6, §14): with the sales
// invoices' reads never answered, every key still lists the matching pages
// at once, a superseded read is aborted rather than left to finish, and
// Enter opens the active page while the last read still hangs.

/** The sales invoices' read, which the Acme provider sends for each query. */
const INVOICES = '/1/api/web/sales-invoices/query';

test.describe('a hung launcher provider', () => {
  test.beforeEach(async ({ page, cast }) => {
    await signInAs(page, cast.user);
    await warmUp(page, '/1');
  });

  test('lists the pages at once, aborts its superseded reads, and Enter still opens the page', async ({
    page,
    lang,
    text,
  }) => {
    // --- Every read of the invoices hangs; the flow watches which the page gives up ---
    const sent: Request[] = [];
    const aborted: Request[] = [];
    await page.context().route(
      (url) => url.pathname === INVOICES,
      (route) => {
        sent.push(route.request());
      },
    );
    page.on('requestfailed', (request) => {
      if (new URL(request.url()).pathname === INVOICES) {
        aborted.push(request);
      }
    });
    await page.goto('/1');
    await expect(sessionButton(page, lang)).toBeVisible();
    await page.keyboard.press('ControlOrMeta+K');
    await expect(launcherField(page)).toBeFocused();

    // --- Each key lists the pages while the reads hang ---
    const query = lang === 'ar' ? 'عمل' : 'cust';
    for (let length = 1; length <= query.length; length++) {
      await page.keyboard.type(query[length - 1]!);
      // The pages that match so far are listed while every read hangs.
      await expect(
        page.getByRole('listbox').locator('.tm-launcher__group--pages [role="option"]').first(),
      ).toBeVisible();
      // The provider's read of this query went out before the next key.
      await expect.poll(() => sent.length).toBeGreaterThanOrEqual(length);
    }
    await expect(activeOption(page)).toHaveAttribute('href', '/1/customers');

    // --- Every read but the last was aborted; the last still hangs ---
    await expect.poll(() => aborted.length).toBe(sent.length - 1);
    expect(aborted).not.toContain(sent.at(-1));

    // --- Enter opens the active page at once ---
    await page.keyboard.press('Enter');
    await expect(page).toHaveURL(/\/1\/customers$/);
    await expect(page.getByRole('heading', { level: 1, name: text.customers })).toBeVisible();
    await page.context().unrouteAll({ behavior: 'ignoreErrors' });
  });
});
