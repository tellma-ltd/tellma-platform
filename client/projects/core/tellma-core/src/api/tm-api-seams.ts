// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, InjectionToken, Injector, type Signal, type Type, type ViewContainerRef } from '@angular/core';

import type { TmApiError } from './tm-api-error';

// The services the HTTP layer feeds or asks, each behind an internal seam
// with a default that is correct until its owner provides the real one:
//
// - the request locale (spec 0024 §7.1, §7.7): `TmLocalization` provides the
//   `Accept-Language` string and the calendar; until then the document's
//   language goes out, and no calendar, so the server falls back to the
//   user's preference;
// - the cache refresher (spec 0025 §5.3): `provideTellma` provides
//   `TmCacheRefresher`'s `observe` and `refresh`; without it, as in an HTTP
//   test, no document is cached, so there is nothing to revalidate;
// - the worker's update check (spec 0024 §5.5): `TmUpdates`, in the
//   lazily loaded `@tellma/core/updates`, answers `check()`; the seam loads
//   it on the first call. An app without it, as a test app, has no worker,
//   which is exactly what `disabled` says; an entry point that fails to
//   load leaves the worker unasked, which reads as `unreachable`. The error
//   views, in their own lazy entry point, load it for Reload themselves.

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

/** The update service as its lazy callers use it: the check, and the update and reload it allows. Internal. */
export interface TmWorkerUpdates extends TmApiUpdates {
  /** Checks, and reloads into the new version on `ready`, or on `disabled` when no worker is in the way. */
  update(): Promise<TmApiUpdateCheck>;
  /** Reloads as the browser would, which a degraded worker sends to the network. */
  reload(): void;
}

/** The update service's entry point, as far as its loaders read it. Internal. */
export interface TmUpdatesModule {
  /** The update service. */
  readonly TmUpdates: Type<TmWorkerUpdates>;
}

/**
 * Loads the update service's entry point, or nothing, as in a test app.
 * Internal: the startup's, the seam's and the error views' loader, which a
 * test replaces. The specifier is cast so that the package build does not
 * report the import back as a cycle.
 */
export const TM_UPDATES_LOAD = new InjectionToken<() => Promise<TmUpdatesModule | null>>('TM_UPDATES_LOAD', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/updates' as string) as Promise<TmUpdatesModule>,
});

/** The worker's update check the HTTP layer and the route asks. */
export const TM_API_UPDATES = new InjectionToken<TmApiUpdates>('TM_API_UPDATES', {
  providedIn: 'root',
  factory: (): TmApiUpdates => {
    const injector = inject(Injector);
    const load = inject(TM_UPDATES_LOAD);
    return {
      check: () =>
        load().then(
          (loaded) => (loaded === null ? 'disabled' : injector.get(loaded.TmUpdates).check()),
          (): TmApiUpdateCheck => 'unreachable',
        ),
    };
  },
});

/** The error views' entry point, as far as the primary reads it. Internal. */
export interface TmErrorsModule {
  /** The error presenter. */
  readonly TmErrorPresenter: Type<{
    present(error: TmApiError, context: { kind: 'read'; tenantId: number }): Promise<void>;
    describe(
      error: TmApiError,
      context: { kind: 'save'; tenantId?: number },
    ): { readonly title: Signal<string>; readonly message: Signal<string> };
  }>;
  /** Renders the client-outdated view's inline state into a slot; its Reload gets the new version first. */
  readonly ɵtmClientOutdatedState: (slot: ViewContainerRef) => { ɵact(action: 'reload'): Promise<void> };
}

/**
 * Loads the error views' entry point. Internal: the loader of the cache
 * refresher's tenant refusals, of the route's Reload and of the zone
 * prompt's inline failure, which a test replaces. The specifier is cast so that the package build does not report
 * the import back as a cycle.
 */
export const TM_ERRORS_LOAD = new InjectionToken<() => Promise<TmErrorsModule>>('TM_ERRORS_LOAD', {
  providedIn: 'root',
  factory: () => () => import('@tellma/core/errors' as string) as Promise<TmErrorsModule>,
});
