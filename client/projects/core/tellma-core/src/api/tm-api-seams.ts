// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, InjectionToken } from '@angular/core';

// The services the HTTP layer feeds or asks, each behind an internal seam
// with a default that is correct until its owner provides the real one:
//
// - the request locale (spec 0024 §7.1, §7.7): `TmLocalization` provides the
//   `Accept-Language` string and the calendar; until then the document's
//   language goes out, and no calendar, so the server falls back to the
//   user's preference;
// - the cache refresher (spec 0025 §5.3): `TmCacheRefresher` provides
//   `observe` and `refresh`; until then no document is cached, so there is
//   nothing to revalidate;
// - the worker's update check (spec 0024 §5.5): `TmUpdates` provides
//   `check()`; until then no worker controls the page, which is exactly what
//   `disabled` says.

/** The language and calendar every request states. */
export interface TmApiLocale {
  /** The `Accept-Language` value, such as `ar-SA-u-nu-latn`; empty to send none. */
  acceptLanguage(): string;
  /** The `Tellma-Calendar` value of tenant calls, such as `uq`, or `null` to send none. */
  calendarCode(): string | null;
}

/** The language and calendar of the app's requests. */
export const TM_API_LOCALE = new InjectionToken<TmApiLocale>('TM_API_LOCALE', {
  providedIn: 'root',
  factory: (): TmApiLocale => {
    const document = inject(DOCUMENT);
    return { acceptLanguage: () => document.documentElement.lang, calendarCode: () => null };
  },
});

/** The cache refresher's members that the HTTP layer calls. */
export interface TmApiCache {
  /**
   * Compares the version tags a tenant response reported with the held ones.
   *
   * @param tenantId - The tenant.
   * @param tags - The reported tags by name.
   * @param trigger - What reported them: a response header.
   */
  observe(tenantId: number, tags: Readonly<Record<string, string>>, trigger: 'header'): void;
  /**
   * Revalidates a tenant's cached documents.
   *
   * @param tenantId - The tenant.
   * @param docs - The documents to revalidate.
   * @param trigger - Why: a response header.
   */
  refresh(tenantId: number, docs: readonly string[], trigger: 'header'): Promise<void>;
}

/** The cache refresher the HTTP layer reports version tags and tenant-state refusals to. */
export const TM_API_CACHE = new InjectionToken<TmApiCache>('TM_API_CACHE', {
  providedIn: 'root',
  factory: (): TmApiCache => ({
    observe: () => undefined,
    refresh: () => Promise.resolve(),
  }),
});

/** What the service worker's update check found. */
export type TmApiUpdateCheck = 'ready' | 'none' | 'failed' | 'unreachable' | 'disabled';

/** The worker's update check, which settles an outdated-client verdict the builds cannot. */
export interface TmApiUpdates {
  /** Asks the worker for a newer version; settles when its check does. */
  check(): Promise<TmApiUpdateCheck>;
}

/** The worker's update check the HTTP layer asks. */
export const TM_API_UPDATES = new InjectionToken<TmApiUpdates>('TM_API_UPDATES', {
  providedIn: 'root',
  factory: (): TmApiUpdates => ({ check: () => Promise.resolve('disabled') }),
});
