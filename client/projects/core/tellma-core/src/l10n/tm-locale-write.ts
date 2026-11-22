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
// sends the queue 600 ms after the last change. One write is in flight at a
// time; changes made meanwhile queue behind it. Each result is adopted, so
// every tab's launch documents follow without a sync. An offline write keeps
// the queue and retries once the server is reachable again; any other
// failure is reported, and the applied change stands either way. A queue
// left when the page hides goes out at once, as a send whose answer is never
// read, and a queue left when the tab enters another tenant goes out at once
// to the tenant it belongs to.

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

/** The coalesced write of the user's locale preferences to one tenant. */
@Injectable({ providedIn: 'root' })
export class TmLocaleWrite {
  readonly #api = inject(TmApi);
  readonly #refresher = inject(TmCacheRefresher);

  /** The tenant the queue belongs to, or `null` with nothing queued. */
  #tenantId: number | null = null;

  /** The members waiting to be sent. */
  #queue: TmLocaleQueue = {};

  /** The timer that sends the queue, while one is set. */
  #timer: ReturnType<typeof setTimeout> | undefined;

  /** Whether a write is in flight. */
  #inFlight = false;

  /** Whether an offline write waits for the server to be reachable again. */
  #awaitingOnline = false;

  /** The write's state. */
  readonly #state = signal<TmLocaleSave>('idle');

  /** The write's state: `saving` from the first queued change, `saved` once it lands, `failed` after a refusal. */
  readonly state: Signal<TmLocaleSave> = this.#state.asReadonly();

  /** Creates the writer, which sends its queue when the page hides and retries once the server is reachable. */
  constructor() {
    // --- Leaving the page: the queue goes out at once, its answer unread ---
    const stop = inject(TM_API_HOST).listen('window', 'pagehide', () => this.#sendNow());
    inject(DestroyRef).onDestroy(() => {
      stop();
      clearTimeout(this.#timer);
    });

    // --- Back online: an offline write is tried once more ---
    effect(() => {
      const online = this.#api.online();
      untracked(() => {
        if (online && this.#awaitingOnline) {
          this.#awaitingOnline = false;
          this.#fire();
        }
      });
    });

    // --- Another tenant: the queue goes to the tenant it belongs to, at once ---
    effect(() => {
      const tenantId = this.#refresher.ɵpartition()?.tenantId ?? null;
      untracked(() => {
        if (this.#tenantId !== null && tenantId !== this.#tenantId && this.#hasQueue()) {
          this.#fire();
        }
      });
    });
  }

  /**
   * Queues members for a tenant: they go out 600 ms after the last change.
   * Members queued for another tenant go out at once.
   *
   * @param tenantId - The tenant whose user row the members change.
   * @param members - The members: a value, or `null` to return one to the tenant's default.
   */
  queue(tenantId: number, members: TmLocaleQueue): void {
    if (this.#tenantId !== null && this.#tenantId !== tenantId && this.#hasQueue()) {
      this.#fire();
    }
    this.#tenantId = tenantId;
    this.#queue = { ...this.#queue, ...members };
    this.#state.set('saving');
    clearTimeout(this.#timer);
    this.#timer = setTimeout(() => this.#fire(), DELAY_MS);
  }

  /** Whether anything is queued. */
  #hasQueue(): boolean {
    return Object.keys(this.#queue).length > 0;
  }

  /** Sends the queue now, unless a write is in flight, in which case it follows that write. */
  #fire(): void {
    clearTimeout(this.#timer);
    this.#timer = undefined;
    const tenantId = this.#tenantId;
    if (this.#inFlight || tenantId === null || !this.#hasQueue()) {
      return;
    }
    const members = this.#queue;
    this.#queue = {};
    this.#inFlight = true;
    this.#api
      .write<TmMeLocaleResult>(ROUTE, tmLocaleWriteBody(members), { tenantId })
      .then(
        (result) => {
          this.#refresher.adopt(tenantId, { locale: result });
          if (!this.#hasQueue()) {
            this.#state.set('saved');
          }
        },
        (error: unknown) => {
          if (error instanceof TmApiError && error.kind === 'offline') {
            // The queue keeps the members, under any change made since.
            if (this.#tenantId === tenantId || !this.#hasQueue()) {
              this.#tenantId = tenantId;
              this.#queue = { ...members, ...this.#queue };
              this.#awaitingOnline = true;
            }
          } else {
            this.#state.set('failed');
          }
        },
      )
      .finally(() => {
        this.#inFlight = false;
        // Changes made during the write follow it, unless their own timer is still running.
        if (this.#timer === undefined && !this.#awaitingOnline) {
          this.#fire();
        }
      });
  }

  /** Sends the queue now as a send that outlives the page, and drops it. */
  #sendNow(): void {
    if (this.#tenantId !== null && this.#hasQueue()) {
      clearTimeout(this.#timer);
      this.#timer = undefined;
      this.#api.send(ROUTE, tmLocaleWriteBody(this.#queue));
      this.#queue = {};
      this.#awaitingOnline = false;
    }
  }
}
