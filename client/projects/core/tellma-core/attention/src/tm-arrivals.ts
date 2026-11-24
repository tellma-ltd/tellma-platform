// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, effect, inject, Injectable, Injector, runInInjectionContext, type Signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import {
  TM_NOW,
  type TmAssignmentsSummary,
  TmCacheRefresher,
  TmClientStore,
  type TmInboxSummary,
  TmLocalization,
  type TmPartition,
  tmPreferenceKey,
  type TmPreferenceKey,
  TmPreferences,
  type TmStoredDoc,
  TmTelemetry,
  type TmTenantFacts,
  TmUrls,
} from '@tellma/core';
import { TmToasts } from '@tellma/core-ui/toast';

import type { TmArrivalToasts } from './tm-arrival-toasts';
import { tmRegisterAttentionStrings } from './tm-attention-strings';

// The arrival engine (spec 0028 §8.2–§8.3): every tab watches the stored
// inbox, assignments and tenant documents, and the visible, focused tab
// toasts what arrived. Each entry into a tenant starts its own clock, and
// the first copy it observes, whatever its age, is the previous copy; each
// later copy's arrivals are the rows newer than the previous copy's newest.
// So that nothing which arrived before the entry pops up, a previous copy
// saved before the entry admits only rows created or assigned since it, and
// a change of the tenant's state toasts only from a copy saved since it.
// (Spec 0028 §8.2 makes the first copy saved after the start a silent
// baseline instead, which in a follower tab is the leader's first real
// refresh, so its arrivals never toasted.) Every tab moves its previous copy
// on with every copy, so a tab that gains focus later replays nothing.
// Toasts come from document contents, never from hub events. What arrived
// and how it toasts load as a chunk with the first copy handed on.

/**
 * Whether arrivals toast: the user's `notifications.toasts` preference, on
 * by default. Off, notifications and assignments arrive in the badges alone;
 * outcomes, failures and a tenant's change of state still toast.
 */
export const tmNotificationToastsKey: TmPreferenceKey<boolean> = /* @__PURE__ */ tmPreferenceKey<boolean>(
  'notifications.toasts',
  'curated',
  true,
  (value): value is boolean => typeof value === 'boolean',
);

/** A tenant's partition. */
type TenantPartition = Extract<TmPartition, { readonly kind: 'tenant' }>;

/**
 * Turns arrivals in the stored attention documents into toasts in the
 * visible, focused tab, and installs the toasts' overflow pill, which opens
 * the notifications. The tenant shell creates it.
 *
 * @example
 * ```ts
 * export class TmTenantShell {
 *   constructor() {
 *     inject(TmArrivals);
 *   }
 * }
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmArrivals {
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #preferences = inject(TmPreferences);
  readonly #toasts = inject(TmToasts);
  readonly #urls = inject(TmUrls);
  readonly #router = inject(Router);
  readonly #telemetry = inject(TmTelemetry);
  readonly #l10n = inject(TmLocalization);
  readonly #document = inject(DOCUMENT);
  readonly #injector = inject(Injector);
  readonly #now = inject(TM_NOW);

  /** Whether arrivals toast. */
  readonly #toastsOn: Signal<boolean> = this.#preferences.get(tmNotificationToastsKey);

  /** The arrival toasts, once their chunk has loaded. */
  #arrivals: Promise<TmArrivalToasts> | null = null;

  /** Creates the engine, which watches the routed tenant's attention documents. */
  constructor() {
    tmRegisterAttentionStrings(this.#l10n);

    // --- The overflow pill: "{n} more · Open notifications" inside a tenant, else the default ---
    this.#toasts.ɵsetOverflow((hidden) =>
      this.#urls.tenantId() === null
        ? null
        : {
            label: this.#l10n.ɵtext('tm.attention.toasts.more', { count: hidden })(),
            run: () => void this.#router.navigateByUrl(this.#urls.search('core.Notification')),
          },
    );
    inject(DestroyRef).onDestroy(() => this.#toasts.ɵsetOverflow(null));

    // --- The three documents, each with its arrival rule; arrival toasts are switched off by the preference, a tenant's state is not ---
    this.#watch<TmInboxSummary>('inbox', (copy, previous, tenantId, start) => {
      if (this.#toastsOn()) {
        const since = previous.savedAt < start ? start : -Infinity;
        this.#arrive((toasts) => toasts.notifications(copy.data, previous.data, tenantId, since));
      }
    });
    this.#watch<TmAssignmentsSummary>('assignments', (copy, previous, tenantId, start) => {
      if (this.#toastsOn()) {
        const since = previous.savedAt < start ? start : -Infinity;
        this.#arrive((toasts) => toasts.assignments(copy.data, previous.data, tenantId, since));
      }
    });
    this.#watch<TmTenantFacts>('tenant', (copy, previous, _tenantId, start) => {
      // Compared whatever the copies' age, but a change only a copy saved before the entry shows is old news.
      const { state } = copy.data;
      if (state !== previous.data.state && state !== 'Active' && copy.savedAt >= start) {
        this.#arrive((toasts) => toasts.tenant(state));
      }
    });
  }

  /**
   * Watches one stored document of the routed tenant, and hands each copy
   * after the first it observes to its rule, with the copy before it and the
   * time the tab entered the tenant.
   */
  #watch<T>(name: string, arrive: (copy: TmStoredDoc<T>, previous: TmStoredDoc<T>, tenantId: number, start: number) => void): void {
    let tenant: string | null = null;
    let start = 0;
    let previous: TmStoredDoc<T> | undefined;
    effect(() => {
      const partition: TenantPartition | null = this.#refresher.ɵpartition();
      const copy = partition === null ? undefined : this.#store.doc<T>(partition, name)();
      untracked(() => {
        // Each entry into a tenant starts over: its own clock, its own previous copy.
        const key = partition && `${partition.sub}/${partition.tenantId}`;
        if (key !== tenant) {
          tenant = key;
          start = this.#now();
          previous = undefined;
        }
        if (partition === null || copy === undefined || copy === previous) {
          return;
        }
        const last = previous;
        previous = copy;
        // The first copy observed only becomes the previous one.
        if (last !== undefined && this.#focused()) {
          arrive(copy, last, partition.tenantId, start);
        }
      });
    });
  }

  /** Whether this tab shows toasts: it is visible and focused. */
  #focused(): boolean {
    return this.#document.visibilityState === 'visible' && this.#document.hasFocus();
  }

  /** Hands a copy to the arrival toasts, in order, once their chunk has loaded; a chunk that cannot load toasts nothing and loads again next time. */
  #arrive(show: (toasts: TmArrivalToasts) => void): void {
    this.#arrivals ??= import('./tm-arrival-toasts').then(({ TmArrivalToasts }) =>
      runInInjectionContext(this.#injector, () => new TmArrivalToasts()),
    );
    this.#arrivals.then(show, (error: unknown) => {
      this.#arrivals = null;
      this.#telemetry.error(error, { source: 'tm-arrivals' });
    });
  }
}
