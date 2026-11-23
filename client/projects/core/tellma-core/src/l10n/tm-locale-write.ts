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
import { TM_LOCALE_FIELDS } from './tm-locale-resolution';

// The coalesced locale write (spec 0024 §7.2): the localization service's
// setters queue the members they change, and one `users/me/locale` write
// sends a tenant's queue 600 ms after its last change. Each tenant has its own
// queue, since each is a user row of its own: a change in one tenant never
// merges into, or drops, another's. One write per tenant is in flight at a
// time; changes made meanwhile queue behind it. Each result is adopted, so
// every tab's launch documents follow without a sync. An offline write keeps
// its queue and retries once the server is reachable again, or, when the
// failure left the server counted reachable, after a backoff; any other
// failure is reported. A queue left when the page hides goes out at once to
// its tenant, as a send whose answer is never read, and a queue left when
// the tab enters another tenant goes out at once.
//
// The applied change stands either way: the writer keeps each member's
// latest choice, which stands over the stored preference while a write of
// that member is queued or in flight, and after that for as long as the
// stored value is the one this tab's own writes left. An earlier write that
// lands therefore never undoes a later change, and a refused or offline
// later write leaves the latest choice applied. The localization service
// loads the writer with the first change it saves, so the first frame never
// carries it.

/** A member of the locale write. */
export type TmLocaleMember = 'language' | 'calendar' | 'timeZone' | 'region' | 'digits';

/** The locale write's state, as the Language & region section shows it. */
export type TmLocaleSave = 'idle' | 'saving' | 'saved' | 'failed';

/** Queued members: a value to save, or `null` to return the member to the tenant's default. */
export type TmLocaleQueue = Partial<Record<TmLocaleMember, string | null>>;

/** The stored members of the user's preferences: each one set, by member. */
export type TmLocaleStored = Readonly<Partial<Record<TmLocaleMember, string>>>;

/** A member's latest choice, standing over the stored preference. */
interface TmLocaleChoice {
  /** The member's new value; `null` for the tenant's default. */
  readonly value: string | null;
  /** The stored value the choice stands over. */
  readonly base: string | undefined;
}

/** A tenant's latest choices, by member. */
type TmLocaleChoices = Readonly<Partial<Record<TmLocaleMember, TmLocaleChoice>>>;

/** How long the write waits after the last change, in milliseconds. */
const DELAY_MS = 600;

/** The first wait before an offline write is tried again while the server counts as reachable, in milliseconds. */
const RETRY_FIRST_MS = 5_000;

/** The longest wait between those tries, in milliseconds. */
const RETRY_LAST_MS = 30_000;

/** How many times such a write is tried again before the write reports a failure. */
const RETRY_LIMIT = 4;

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
  /** The members of the write in flight, while one is. */
  sending: TmLocaleQueue | undefined;
  /** The timer that sends the members, while one is set. */
  timer: ReturnType<typeof setTimeout> | undefined;
  /** Whether an offline write waits for the server to be reachable again. */
  awaitingOnline: boolean;
  /** How many times in a row an offline write was tried again on a timer. */
  retries: number;
}

/** The coalesced write of the user's locale preferences, one queue per tenant, and the latest choices it saves. */
@Injectable({ providedIn: 'root' })
export class TmLocaleWrite {
  readonly #api = inject(TmApi);
  readonly #refresher = inject(TmCacheRefresher);

  /** The app's lifetime: once it ends, nothing more is sent, adopted or armed. */
  readonly #app = inject(DestroyRef);

  /** Each tenant's queue, while it has members, a timer or a write. */
  readonly #queues = new Map<number, TmTenantQueue>();

  /** The write's state. */
  readonly #state = signal<TmLocaleSave>('idle');

  /** Each tenant's latest choices. */
  readonly #choices = signal<ReadonlyMap<number, TmLocaleChoices>>(new Map());

  /** The members each tenant has queued or in flight, as the queues last published them. */
  readonly #pending = signal<ReadonlyMap<number, ReadonlySet<TmLocaleMember>>>(new Map());

  /** The write's state: `saving` from the first queued change, `saved` once it lands, `failed` after a refusal. */
  readonly state: Signal<TmLocaleSave> = this.#state.asReadonly();

  /** Creates the writer, which sends its queues when the page hides and retries once the server is reachable. */
  constructor() {
    // --- Leaving the page: every queue goes out at once, its answer unread ---
    const stop = inject(TM_API_HOST).listen('window', 'pagehide', () => this.#sendNow());
    this.#app.onDestroy(() => {
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
            entry.retries = 0;
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
   * Queues members for a tenant, and makes each the member's latest choice:
   * they go out 600 ms after the tenant's last change. Members queued for
   * another tenant go out at once.
   *
   * @param tenantId - The tenant whose user row the members change.
   * @param members - The members: a value, or `null` to return one to the tenant's default.
   * @param stored - The tenant's stored members now, which the choices stand over.
   */
  queue(tenantId: number, members: TmLocaleQueue, stored: TmLocaleStored): void {
    this.#fireOthers(tenantId);
    const entry = this.#entry(tenantId);
    entry.members = { ...entry.members, ...members };
    entry.retries = 0;
    const choices: Partial<Record<TmLocaleMember, TmLocaleChoice>> = { ...untracked(this.#choices).get(tenantId) };
    for (const [member, value] of Object.entries(members) as [TmLocaleMember, string | null][]) {
      choices[member] = { value, base: stored[member] };
    }
    this.#choices.update((all) => new Map(all).set(tenantId, choices));
    this.#publish();
    this.#state.set('saving');
    clearTimeout(entry.timer);
    entry.timer = setTimeout(() => this.#fire(tenantId), DELAY_MS);
  }

  /**
   * A tenant's stored members under the latest choices that stand: a
   * choice stands while a write of its member is queued or in flight, and
   * after that while the stored value is the one it stands over. Reads
   * signals, so a computed that calls it follows both.
   *
   * @param tenantId - The tenant.
   * @param stored - The tenant's stored members.
   * @returns The members in effect; a member returned to the tenant's default is absent.
   */
  overlay(tenantId: number, stored: TmLocaleStored): TmLocaleStored {
    const choices = this.#choices().get(tenantId);
    if (choices === undefined) {
      return stored;
    }
    const pending = this.#pending().get(tenantId);
    const effective: Partial<Record<TmLocaleMember, string>> = { ...stored };
    for (const [member, choice] of Object.entries(choices) as [TmLocaleMember, TmLocaleChoice][]) {
      if (pending?.has(member) || choice.base === stored[member]) {
        if (choice.value === null) {
          delete effective[member];
        } else {
          effective[member] = choice.value;
        }
      }
    }
    return effective;
  }

  /**
   * Forgets the choices that no longer stand, because the stored value moved
   * from under them through another tab's or device's change, so a later
   * move back never revives them.
   *
   * @param tenantId - The tenant.
   * @param stored - The tenant's stored members.
   */
  release(tenantId: number, stored: TmLocaleStored): void {
    const choices = untracked(this.#choices).get(tenantId);
    if (choices === undefined) {
      return;
    }
    const pending = untracked(this.#pending).get(tenantId);
    const kept = (Object.entries(choices) as [TmLocaleMember, TmLocaleChoice][]).filter(
      ([member, choice]) => pending?.has(member) || choice.base === stored[member],
    );
    if (kept.length === Object.keys(choices).length) {
      return;
    }
    this.#choices.update((all) => {
      const next = new Map(all);
      if (kept.length === 0) {
        next.delete(tenantId);
      } else {
        next.set(tenantId, Object.fromEntries(kept));
      }
      return next;
    });
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
      entry = { members: {}, sending: undefined, timer: undefined, awaitingOnline: false, retries: 0 };
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
    // After the app's end nothing is sent, not even the members queued behind a write.
    if (entry === undefined || this.#app.destroyed) {
      return;
    }
    clearTimeout(entry.timer);
    entry.timer = undefined;
    if (entry.sending !== undefined || Object.keys(entry.members).length === 0) {
      this.#prune(tenantId, entry);
      return;
    }
    const members = entry.members;
    entry.members = {};
    entry.sending = members;
    // A write that starts supersedes an earlier offline wait.
    entry.awaitingOnline = false;
    this.#api
      .write<TmMeLocaleResult>(ROUTE, tmLocaleWriteBody(members), { tenantId })
      .then(
        (result) => {
          // A write that lands after the app's end is not adopted: the refresher has ended with it.
          if (this.#app.destroyed) {
            return;
          }
          entry.retries = 0;
          // The choices move onto the result before it shows, in the same pass.
          this.#rebase(tenantId, members, result);
          this.#refresher.adopt(tenantId, { locale: result });
        },
        (error: unknown) => {
          if (error instanceof TmApiError && error.kind === 'offline') {
            // The queue keeps the members, under any change made since.
            entry.members = { ...members, ...entry.members };
            this.#retry(tenantId, entry);
          } else {
            this.#state.set('failed');
          }
        },
      )
      .finally(() => {
        entry.sending = undefined;
        // Changes made during the write follow it, unless their own timer is still running.
        if (entry.timer === undefined && !entry.awaitingOnline) {
          this.#fire(tenantId);
        }
        this.#prune(tenantId, entry);
        this.#publish();
        if (this.#state() === 'saving' && !this.#busy()) {
          this.#state.set('saved');
        }
      });
  }

  /**
   * Plans the next try of an offline write: once the server is reachable
   * again, or, when the failure left it counted reachable (a call that timed
   * out with its outcome unknown, a request that never reached the network),
   * on a timer that backs off. Once those tries are spent the write reports
   * a failure, and its members wait for the next change, the server's
   * return or the page's end.
   *
   * @param tenantId - The tenant.
   * @param entry - Its queue.
   */
  #retry(tenantId: number, entry: TmTenantQueue): void {
    // A failure that lands after the app's end plans no further try.
    if (this.#app.destroyed) {
      return;
    }
    entry.awaitingOnline = true;
    if (!untracked(this.#api.online)) {
      return;
    }
    if (entry.retries >= RETRY_LIMIT) {
      this.#state.set('failed');
      return;
    }
    // No online transition will come; the write is idempotent, so trying it again is safe.
    const wait = Math.min(RETRY_FIRST_MS * 2 ** entry.retries, RETRY_LAST_MS);
    entry.retries++;
    entry.timer = setTimeout(() => this.#fire(tenantId), wait);
  }

  /**
   * Moves the choices of a write's members onto the stored values its result
   * leaves, so a later choice of the same member keeps standing over them
   * when its own write fails.
   *
   * @param tenantId - The tenant.
   * @param members - The members the write carried.
   * @param result - The write's result.
   */
  #rebase(tenantId: number, members: TmLocaleQueue, result: TmMeLocaleResult): void {
    const choices = untracked(this.#choices).get(tenantId);
    if (choices === undefined) {
      return;
    }
    const rebased: Partial<Record<TmLocaleMember, TmLocaleChoice>> = { ...choices };
    for (const member of Object.keys(members) as TmLocaleMember[]) {
      const choice = choices[member];
      if (choice !== undefined) {
        rebased[member] = { value: choice.value, base: result[TM_LOCALE_FIELDS[member]] };
      }
    }
    this.#choices.update((all) => new Map(all).set(tenantId, rebased));
  }

  /** Whether any tenant has members queued, a write in flight, or a write waiting to go online. */
  #busy(): boolean {
    return [...this.#queues.values()].some(
      (entry) => entry.sending !== undefined || entry.awaitingOnline || Object.keys(entry.members).length > 0,
    );
  }

  /** Publishes the members each tenant has queued or in flight, which keep their choices standing. */
  #publish(): void {
    const pending = new Map<number, ReadonlySet<TmLocaleMember>>();
    for (const [tenantId, entry] of this.#queues) {
      const members = new Set([...Object.keys(entry.members), ...Object.keys(entry.sending ?? {})] as TmLocaleMember[]);
      if (members.size > 0) {
        pending.set(tenantId, members);
      }
    }
    this.#pending.set(pending);
  }

  /**
   * Forgets a tenant's queue once it holds nothing.
   *
   * @param tenantId - The tenant.
   * @param entry - Its queue.
   */
  #prune(tenantId: number, entry: TmTenantQueue): void {
    const empty = entry.sending === undefined && !entry.awaitingOnline && entry.timer === undefined;
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
    this.#publish();
    // The sends' answers are never read: with no write left in flight, nothing is saving.
    if (this.#state() === 'saving' && !this.#busy()) {
      this.#state.set('idle');
    }
  }
}
