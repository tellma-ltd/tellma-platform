// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, effect, inject, Injectable, signal, untracked, type Signal } from '@angular/core';

import { TmApi } from '../api/tm-api';
import { TmApiError } from '../api/tm-api-error';
import { TM_API_HOST } from '../api/tm-api-host';
import type { TmMeLocaleResult } from '../state/launch/tm-launch-docs';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';

// The coalesced locale write (spec 0024 §7.2): the localization service's
// setters queue the members they change, and one `users/me/locale` write
// sends a tenant's queue 600 ms after its last change. Each tenant has its own
// queue, since each is a user row of its own: a change in one tenant never
// merges into, or drops, another's. One write per tenant is in flight at a
// time; changes made meanwhile queue behind it. Each result is adopted, so
// every tab's launch documents follow without a sync. An offline write keeps
// its queue and retries once the server is reachable again; any other failure
// is reported, and the applied change stands either way. A queue left when the
// page hides goes out at once to its tenant, as a send whose answer is never
// read, and a queue left when the tab enters another tenant goes out at once.

/** A member of the locale write. */
export type TmLocaleMember = 'language' | 'calendar' | 'timeZone' | 'region' | 'digits';

/** The locale write's state, as the Language & region section shows it. */
export type TmLocaleSave = 'idle' | 'saving' | 'saved' | 'failed';

/** Queued members: a value to save, or `null` to return the member to the tenant's default. */
export type TmLocaleQueue = Partial<Record<TmLocaleMember, string | null>>;

/** How long the write waits after the last change, in milliseconds. */
const DELAY_MS = 600;

/** The route of the write. */
const ROUTE = 'users/me/locale';

/** The server's names of the members, which a reset lists. */
const RESET_NAMES: Readonly<Record<TmLocaleMember, string>> = {
  language: 'Language',
  calendar: 'Calendar',
  timeZone: 'TimeZone',
  region: 'Region',
  digits: 'Digits',
};

/**
 * The request body of a queue: each member set to a value, and the members
 * set to `null` in `reset`.
 *
 * @param queue - The queued members.
 * @returns The body.
 */
export function tmLocaleWriteBody(queue: TmLocaleQueue): Readonly<Record<string, unknown>> {
  const body: Record<string, unknown> = {};
  const reset: string[] = [];
  for (const [member, value] of Object.entries(queue) as [TmLocaleMember, string | null][]) {
    if (value === null) {
      reset.push(RESET_NAMES[member]);
    } else {
      body[member] = value;
    }
  }
  if (reset.length > 0) {
    body['reset'] = reset;
  }
  return body;
}

/** One tenant's side of the write. */
interface TmTenantQueue {
  /** The members waiting to be sent. */
  members: TmLocaleQueue;
  /** The timer that sends the members, while one is set. */
  timer: ReturnType<typeof setTimeout> | undefined;
  /** Whether a write is in flight. */
  inFlight: boolean;
  /** Whether an offline write waits for the server to be reachable again. */
  awaitingOnline: boolean;
}

/** The coalesced write of the user's locale preferences, one queue per tenant. */
@Injectable({ providedIn: 'root' })
export class TmLocaleWrite {
  readonly #api = inject(TmApi);
  readonly #refresher = inject(TmCacheRefresher);

  /** Each tenant's queue, while it has members, a timer or a write. */
  readonly #queues = new Map<number, TmTenantQueue>();

  /** The write's state. */
  readonly #state = signal<TmLocaleSave>('idle');

  /** The write's state: `saving` from the first queued change, `saved` once it lands, `failed` after a refusal. */
  readonly state: Signal<TmLocaleSave> = this.#state.asReadonly();

  /** Creates the writer, which sends its queues when the page hides and retries once the server is reachable. */
  constructor() {
    // --- Leaving the page: every queue goes out at once, its answer unread ---
    const stop = inject(TM_API_HOST).listen('window', 'pagehide', () => this.#sendNow());
    inject(DestroyRef).onDestroy(() => {
      stop();
      for (const entry of this.#queues.values()) {
        clearTimeout(entry.timer);
      }
    });

    // --- Back online: each offline write is tried once more ---
    effect(() => {
      const online = this.#api.online();
      untracked(() => {
        if (!online) {
          return;
        }
        for (const [tenantId, entry] of [...this.#queues]) {
          if (entry.awaitingOnline) {
            entry.awaitingOnline = false;
            this.#fire(tenantId);
          }
        }
      });
    });

    // --- Another tenant: the queues of the tenants left go out at once ---
    effect(() => {
      const tenantId = this.#refresher.ɵpartition()?.tenantId ?? null;
      untracked(() => this.#fireOthers(tenantId));
    });
  }

  /**
   * Queues members for a tenant: they go out 600 ms after the tenant's last
   * change. Members queued for another tenant go out at once.
   *
   * @param tenantId - The tenant whose user row the members change.
   * @param members - The members: a value, or `null` to return one to the tenant's default.
   */
  queue(tenantId: number, members: TmLocaleQueue): void {
    this.#fireOthers(tenantId);
    const entry = this.#entry(tenantId);
    entry.members = { ...entry.members, ...members };
    this.#state.set('saving');
    clearTimeout(entry.timer);
    entry.timer = setTimeout(() => this.#fire(tenantId), DELAY_MS);
  }

  /**
   * A tenant's queue, created when it has none.
   *
   * @param tenantId - The tenant.
   * @returns Its queue.
   */
  #entry(tenantId: number): TmTenantQueue {
    let entry = this.#queues.get(tenantId);
    if (entry === undefined) {
      entry = { members: {}, timer: undefined, inFlight: false, awaitingOnline: false };
      this.#queues.set(tenantId, entry);
    }
    return entry;
  }

  /**
   * Sends at once every waiting queue but one tenant's.
   *
   * @param tenantId - The tenant whose queue keeps its timer, or `null` for none.
   */
  #fireOthers(tenantId: number | null): void {
    for (const [other, entry] of [...this.#queues]) {
      if (other !== tenantId && entry.timer !== undefined) {
        this.#fire(other);
      }
    }
  }

  /**
   * Sends a tenant's queue now, unless its write is in flight, in which case
   * the queue follows that write.
   *
   * @param tenantId - The tenant.
   */
  #fire(tenantId: number): void {
    const entry = this.#queues.get(tenantId);
    if (entry === undefined) {
      return;
    }
    clearTimeout(entry.timer);
    entry.timer = undefined;
    if (entry.inFlight || Object.keys(entry.members).length === 0) {
      this.#prune(tenantId, entry);
      return;
    }
    const members = entry.members;
    entry.members = {};
    entry.inFlight = true;
    // A write that starts supersedes an earlier offline wait.
    entry.awaitingOnline = false;
    this.#api
      .write<TmMeLocaleResult>(ROUTE, tmLocaleWriteBody(members), { tenantId })
      .then(
        (result) => {
          this.#refresher.adopt(tenantId, { locale: result });
        },
        (error: unknown) => {
          if (error instanceof TmApiError && error.kind === 'offline') {
            // The queue keeps the members, under any change made since.
            entry.members = { ...members, ...entry.members };
            entry.awaitingOnline = true;
          } else {
            this.#state.set('failed');
          }
        },
      )
      .finally(() => {
        entry.inFlight = false;
        // Changes made during the write follow it, unless their own timer is still running.
        if (entry.timer === undefined && !entry.awaitingOnline) {
          this.#fire(tenantId);
        }
        this.#prune(tenantId, entry);
        if (this.#state() === 'saving' && !this.#pending()) {
          this.#state.set('saved');
        }
      });
  }

  /** Whether any tenant has members queued, a write in flight, or a write waiting to go online. */
  #pending(): boolean {
    return [...this.#queues.values()].some(
      (entry) => entry.inFlight || entry.awaitingOnline || Object.keys(entry.members).length > 0,
    );
  }

  /**
   * Forgets a tenant's queue once it holds nothing.
   *
   * @param tenantId - The tenant.
   * @param entry - Its queue.
   */
  #prune(tenantId: number, entry: TmTenantQueue): void {
    const empty = !entry.inFlight && !entry.awaitingOnline && entry.timer === undefined;
    if (empty && Object.keys(entry.members).length === 0 && this.#queues.get(tenantId) === entry) {
      this.#queues.delete(tenantId);
    }
  }

  /** Sends every queue now, each to its own tenant, as a send that outlives the page, and drops them. */
  #sendNow(): void {
    for (const [tenantId, entry] of [...this.#queues]) {
      clearTimeout(entry.timer);
      entry.timer = undefined;
      entry.awaitingOnline = false;
      if (Object.keys(entry.members).length > 0) {
        this.#api.send(ROUTE, tmLocaleWriteBody(entry.members), { tenantId });
        entry.members = {};
      }
      this.#prune(tenantId, entry);
    }
    // The sends' answers are never read: with no write left in flight, nothing is saving.
    if (this.#state() === 'saving' && !this.#pending()) {
      this.#state.set('idle');
    }
  }
}
