// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signInAs } from '@tellma/core-testing/e2e';

import { ACME_PRINCIPALS, expect, headings, recordHeadings, test } from '../support/acme';

// Stable layout on a cold deep link (spec 0024 §14): the route skeleton holds
// until the launch documents arrive, so the link renders its own page and
// never flashes another one, a forbidden state or the picker first.

test.describe('cold deep link', () => {
  test('a cold deep link renders its page and never another first', async ({ page }) => {
    await recordHeadings(page.context());
    await signInAs(page, ACME_PRINCIPALS.admin);

    await page.goto('/1/list/customers');

    await expect(page.getByRole('heading', { level: 1, name: 'Customers' })).toBeVisible();
    expect(await headings(page)).toEqual(['Customers']);
    await expect(page).toHaveURL('/1/list/customers');
  });

  // A details page with its record needs the record pages (spec 0031).
  test.fixme('a cold deep link to a details page renders the record, never a forbidden flash', async () => {});
});
