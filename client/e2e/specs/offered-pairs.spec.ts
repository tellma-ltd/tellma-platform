// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

import { expect, test } from '@playwright/test';

/**
 * The offered (language, region) pairs in each engine (`@cross-engine`):
 * every pair the server's language catalogue offers resolves exactly in
 * `Intl.DateTimeFormat` and `Intl.NumberFormat`, as the localization
 * service's browser check compares them, so its fallback to the language's
 * default region fires only on the known gaps. The pairs file is the
 * catalogue's own list, pinned against it by the server's tests.
 */

/** The catalogue's pairs, in catalogue order. */
const PAIRS: readonly string[] = (
  JSON.parse(readFileSync(fileURLToPath(new URL('../fixtures/offered-pairs.json', import.meta.url)), 'utf8')) as { readonly pairs: readonly string[] }
).pairs;

/** The languages an engine is known to lack, by engine: Chromium ships no Oromo, Tigrinya or Somali data. */
const KNOWN_GAPS: Readonly<Record<string, readonly string[]>> = { chromium: ['om', 'ti', 'so'] };

test('every offered pair resolves exactly, apart from the known gaps @cross-engine', async ({ page, browserName }) => {
  // The check runs on the blank page: Intl needs no app.
  const misses = await page.evaluate(
    (pairs) =>
      pairs.filter((tag) => {
        try {
          return (
            new Intl.DateTimeFormat(tag).resolvedOptions().locale !== tag ||
            new Intl.NumberFormat(tag).resolvedOptions().locale !== tag
          );
        } catch {
          return true;
        }
      }),
    PAIRS,
  );

  const gaps = KNOWN_GAPS[browserName] ?? [];
  expect(misses.filter((tag) => !gaps.includes(tag.split('-')[0]))).toEqual([]);
});
