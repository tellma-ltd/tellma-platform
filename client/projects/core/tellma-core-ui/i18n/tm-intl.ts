// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { isDevMode } from '@angular/core';

/*
 * Small helpers shared by the ICU interpreter and the default argument
 * formatter: one cache of `Intl` objects and one once-per-problem warning.
 */

/** `Intl` formatters and rules, by locale and key. */
const cache = new Map<string, Intl.NumberFormat | Intl.PluralRules>();
/** What already warned in development, so each problem warns once. */
const warned = new Set<string>();

/**
 * The cached `Intl` object for `locale` and `key`, created by `create` —
 * falling back to the runtime's default locale for an invalid tag, so a
 * bad locale never throws out of a message. Internal to the library.
 */
export function ɵtmIntl<T extends Intl.NumberFormat | Intl.PluralRules>(
  key: string,
  locale: string,
  create: (locale: string | undefined) => T,
): T {
  const cacheKey = `${locale}\u0000${key}`;
  let value = cache.get(cacheKey) as T | undefined;
  if (value === undefined) {
    try {
      value = create(locale);
    } catch {
      value = create(undefined);
    }
    cache.set(cacheKey, value);
  }
  return value;
}

/** Warns once per distinct problem, in development only. Internal to the library. */
export function ɵtmWarnOnce(message: string): void {
  if (isDevMode() && !warned.has(message)) {
    warned.add(message);
    console.warn(`[tellma-ui] ${message}`);
  }
}
