// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { effect, inject, InjectionToken, Injector, untracked, type EffectRef } from '@angular/core';
import {
  TM_NOW,
  TmApi,
  TmApiError,
  TmCacheRefresher,
  TmClientStore,
  TmLeader,
  TmStoreRefusedError,
  TmTabBus,
  TmTelemetry,
  type TmCacheChangedEvent,
  type TmClientEvent,
  type TmHubConnection,
  type TmHubSink,
  type TmHubState,
  type TmLeaderDocument,
  type TmPartition,
  type TmTenantFacts,
  ɵTM_API_HOST,
  ɵTM_UNIQUE_LEADER_DOCUMENTS,
  ɵTmClientMetadataStore,
  ɵTmSessionPaths,
  type ɵTmSyncDoc,
} from '@tellma/core';

// What the leader tab does while it holds a tenant's hub lock (spec 0025
// §7.2 step 2, §8.4, §8.6, §8.7): it reports its connection to the other tabs,
// revalidates the tenant and refreshes the leader documents on election,
// connects the hub when the tenant has realtime, relays every hub event and
// acts on the ones its build knows, and polls when realtime is off or the hub
// has been down for a minute. One leadership lives from the grant of the lock
// to its release; the election creates and stops it.

/** A tenant's partition of the signed-in user. */
type TmTenantPartition = Extract<TmPartition, { readonly kind: 'tenant' }>;

/**
 * Creates the realtime connection of a tenant, which reports to the sink.
 * Internal: by default it loads the realtime entry point, which only a
 * leader tab downloads.
 */
export type TmHubFactory = (tenantId: number, sink: TmHubSink, injector: Injector) => Promise<TmHubConnection>;

/**
 * The realtime connection's factory: by default, the realtime entry point's,
 * which it loads on the first call; a test provides a fake one. `null` means
 * no realtime: the leader then stays `connecting`, and polls once the hub
 * has not connected for a minute, as it would for a hub that never connects.
 * Internal.
 */
export const TM_HUB_FACTORY = new InjectionToken<TmHubFactory | null>('TM_HUB_FACTORY', {
  providedIn: 'root',
  // The SignalR client loads with the entry point, in the leader tab alone (spec 0025 §8.1).
  factory: () => (tenantId, sink, injector) => import('@tellma/core/realtime').then((realtime) => realtime.tmCreateHub(tenantId, sink, injector)),
});

/** How long the hub may be down before the leader polls. */
const DEGRADED_AFTER_MS = 60_000;

/** The shortest time between two polls of the same thing. */
const POLL_MS = 60_000;

/** How long a leader document's events coalesce into one read. */
const COALESCE_MS = 250;

/**
 * How early a probe may come for the poll it asks for: the leader then polls
 * when the poll falls due, instead of skipping a minute because the probe
 * beat the minute by a few milliseconds.
 */
const PROBE_SLACK_MS = 5_000;

/** One leader document's reads: at most one in flight and one queued. */
interface TmDocReads {
  /** The document. */
  readonly doc: TmLeaderDocument;
  /** Whether a read is in flight. */
  inFlight: boolean;
  /** Whether another read follows the one in flight. */
  queued: boolean;
  /** The timer that ends the 250 ms over which events coalesce. */
  timer: ReturnType<typeof setTimeout> | undefined;
  /** When its last read was sent, in epoch milliseconds. */
  sentAt: number;
}

/**
 * Whether this build knows a hub event: its Core chunk lists the name, so the
 * tab may deliver the event and act on it.
 *
 * @param metadata - The client metadata's holder.
 * @param name - The event's name.
 * @returns Whether the build knows it.
 */
export function tmKnowsEvent(metadata: ɵTmClientMetadataStore, name: string): boolean {
  return metadata.peek()?.clientEvents.includes(name) ?? false;
}

/**
 * Whether a hub event ends the user's access to its tenant: a
 * `session.ended` whose reason is `access-revoked` or `tenant-unavailable`.
 *
 * @param event - The event.
 * @returns Whether it does.
 */
export function tmAccessEnded(event: TmClientEvent): boolean {
  if (event.name !== 'session.ended' || typeof event.payload !== 'object' || event.payload === null) {
    return false;
  }
  const { reason } = event.payload as Readonly<Record<string, unknown>>;
  return reason === 'access-revoked' || reason === 'tenant-unavailable';
}

/** Whether a value is a `cache.changed` payload: a tag, and an optional string value. */
function isCacheChanged(payload: unknown): payload is TmCacheChangedEvent {
  if (typeof payload !== 'object' || payload === null) {
    return false;
  }
  const { tag, value } = payload as Readonly<Record<string, unknown>>;
  return typeof tag === 'string' && (value === undefined || typeof value === 'string');
}

/**
 * The leader tab's work for one tenant, from the grant of the hub lock to
 * its release. Internal: the election creates it in an injection context.
 */
export class TmLeadership {
  readonly #injector = inject(Injector);
  readonly #api = inject(TmApi);
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #bus = inject(TmTabBus);
  readonly #leader = inject(TmLeader);
  readonly #telemetry = inject(TmTelemetry);
  readonly #host = inject(ɵTM_API_HOST);
  readonly #now = inject(TM_NOW);
  readonly #metadata = inject(ɵTmClientMetadataStore);
  readonly #factory = inject(TM_HUB_FACTORY);

  /** The tenant's partition. */
  readonly #partition: TmTenantPartition;
  /** The tenant. */
  readonly #t: number;
  /** Aborts the leader's reads when the leadership ends. */
  readonly #abort = new AbortController();
  /** Each leader document's reads. */
  readonly #reads: TmDocReads[];
  /** Follows the tenant's realtime flag. */
  readonly #realtime: EffectRef;
  /** Re-arms the polls when a leader document's stored copy starts or stops asking for them. */
  readonly #asking: EffectRef;

  /** The connection's state, and when it entered it. */
  #state: TmHubState = 'connecting';
  #since: number;
  /** Since when the hub has not been connected, or `null` while it is. */
  #downSince: number | null;
  /** When the tenant was last revalidated by a poll, or on election. */
  #polledAt: number;
  /** Why the leader polls, as last reported, or `null` while it does not. */
  #polling: 'disabled' | 'hub-down' | null = null;
  /** The realtime connection, once loaded. */
  #hub: TmHubConnection | null = null;
  /** Whether the connection is loading. */
  #loading = false;
  /** Counts the connections, so a stopped one's reports are ignored. */
  #generation = 0;
  /** Restarts the connection once a sync lands after a terminal signal, or `null` while none waits. */
  #resume: EffectRef | null = null;
  /** Counts the terminal signals; only the latest one's wait restarts the connection. */
  #signals = 0;
  /** The leader's own poll timer, which runs while the leader is visible. */
  #timer: ReturnType<typeof setTimeout> | undefined;
  /** The one-shot poll a probe that came a little early asked for. */
  #probeTimer: ReturnType<typeof setTimeout> | undefined;
  #stopped = false;

  /**
   * Starts the leadership: reports `connecting`, revalidates the tenant
   * unless the page has seen a sync of it since it last came back, and
   * refreshes the leader documents, then follows the tenant's realtime flag
   * and the leader documents' stored copies.
   *
   * @param partition - The tenant's partition.
   * @param seenSince - Since when the page has run without a pause, in epoch
   *   milliseconds: its load, or its last return from the back-forward cache
   *   or from a freeze.
   */
  constructor(partition: TmTenantPartition, seenSince: number) {
    this.#partition = partition;
    this.#t = partition.tenantId;
    const now = this.#now();
    this.#since = this.#downSince = this.#polledAt = now;
    this.#reads = inject(ɵTM_UNIQUE_LEADER_DOCUMENTS).map((doc) => ({ doc, inFlight: false, queued: false, timer: undefined, sentAt: -Infinity }));

    // --- Election: report, revalidate the warm launch, refresh the leader documents (spec 0025 §7.2 step 2) ---
    this.#post();
    // The refresh revalidates a warm launch, whose stored sync is older than
    // the page, and a page back from the back-forward cache or a freeze
    // (§7.2 step 4). A sync sent since the page last came back, such as a
    // cold entry's, already did, and another tab's sync since then left the
    // documents as fresh; so the leader adds no second sync to a cold launch.
    // A stamp ahead of the clock, left by a clock set back, proves nothing.
    const syncedAt = untracked(this.#store.doc<ɵTmSyncDoc>(partition, 'sync'))?.data.at ?? -Infinity;
    if (syncedAt < seenSince || syncedAt > now) {
      void this.#refresher.refresh(this.#t, ['tenant'], 'leader');
    }
    this.#reads.forEach((reads) => this.#read(reads));

    // --- Realtime on or off: connect, or poll as `disabled` (§7.2 step 2, §8.7) ---
    // A disabled leader reads the flag after each sync, so turning realtime
    // on takes effect within one poll; one that has no tenant facts yet
    // waits for the sync that stores them.
    this.#realtime = effect(
      () => {
        const realtime = this.#store.doc<TmTenantFacts>(partition, 'tenant')()?.data.realtime;
        untracked(() => this.#follow(realtime));
      },
      { injector: this.#injector, manualCleanup: true },
    );

    // --- A leader document that starts or stops asking for polls re-arms the timer (§8.6) ---
    // Its stored copy changes on this leader's reads and on another tab's or
    // a panel's put; a copy that starts to ask while the hub is connected
    // would otherwise wait for the next change of state or visibility.
    let asked: string | null = null;
    this.#asking = effect(
      () => {
        for (const reads of this.#reads) {
          if (reads.doc.pollWhile !== undefined) {
            this.#store.doc(partition, reads.doc.name)();
          }
        }
        untracked(() => {
          const asking = this.#reads.map((reads) => this.#pollWhile(reads)).join();
          if (asked !== null && asking !== asked) {
            this.#arm();
          }
          asked = asking;
        });
      },
      { injector: this.#injector, manualCleanup: true },
    );
    this.#arm();
  }

  /** Answers a follower's probe: reports at once, cuts the hub's wait short, and polls when a poll is due (§7.4, §8.3, §8.7). */
  probe(): void {
    if (this.#stopped) {
      return;
    }
    this.#post();
    this.#hub?.nudge();
    const next = this.#poll();
    // A probe a few milliseconds early still gets its poll, when it falls due.
    const wait = next - this.#now();
    if (wait <= PROBE_SLACK_MS && this.#probeTimer === undefined) {
      this.#probeTimer = setTimeout(() => {
        this.#probeTimer = undefined;
        this.#poll();
      }, wait);
    }
  }

  /** Reports the leader's new visibility, and runs or pauses its poll timer. */
  visibility(): void {
    if (!this.#stopped) {
      this.#post();
      this.#arm();
    }
  }

  /**
   * Ends the leadership: aborts its reads, stops its timers and its
   * connection.
   *
   * @returns A promise that settles once the connection is closed.
   */
  stop(): Promise<void> {
    this.#stopped = true;
    this.#generation++;
    this.#abort.abort();
    this.#realtime.destroy();
    this.#asking.destroy();
    this.#resume?.destroy();
    clearTimeout(this.#timer);
    clearTimeout(this.#probeTimer);
    this.#reads.forEach((reads) => clearTimeout(reads.timer));
    const hub = this.#hub;
    this.#hub = null;
    return hub === null ? Promise.resolve() : hub.stop().catch((error: unknown) => this.#telemetry.error(error, { source: 'tm-leader' }));
  }

  // --- The connection ---

  /** Follows the tenant's realtime flag: off stops the hub and polls; on connects. */
  #follow(realtime: boolean | undefined): void {
    if (this.#stopped || realtime === undefined) {
      return;
    }
    if (!realtime) {
      this.#disconnect();
      this.#set('disabled');
    } else if (this.#state === 'disabled' || (this.#hub === null && !this.#loading)) {
      this.#set('connecting');
      this.#connect();
    }
  }

  /** Loads the realtime connection and starts it (§8.1). */
  #connect(): void {
    const factory = this.#factory;
    if (factory === null) {
      // No realtime entry point: the hub never connects, so the leader polls
      // once it has been down for a minute.
      return;
    }
    const generation = this.#generation;
    this.#loading = true;
    factory(this.#t, this.#sink(generation), this.#injector).then(
      (hub) => {
        this.#loading = false;
        if (generation !== this.#generation || this.#state === 'disabled') {
          // The leadership ended, or realtime went off, while it loaded.
          void hub.stop();
        } else {
          this.#hub = hub;
          hub.start();
        }
      },
      (error: unknown) => {
        this.#loading = false;
        this.#telemetry.error(error, { source: 'tm-leader' });
      },
    );
  }

  /** Stops the connection, whose reports are ignored from now on. */
  #disconnect(): void {
    this.#generation++;
    this.#loading = false;
    this.#resume?.destroy();
    this.#resume = null;
    const hub = this.#hub;
    this.#hub = null;
    void hub?.stop().catch((error: unknown) => this.#telemetry.error(error, { source: 'tm-leader' }));
  }

  /** The sink a connection reports to; a connection stopped since is ignored. */
  #sink(generation: number): TmHubSink {
    const live = (): boolean => !this.#stopped && generation === this.#generation;
    return {
      event: (event) => {
        if (live()) {
          this.#event(event);
        }
      },
      state: (state) => {
        if (live()) {
          this.#set(state);
        }
      },
      connected: () => {
        if (live()) {
          this.#catchUp();
        }
      },
      refused: () => {
        if (live()) {
          this.#terminal(generation);
        }
      },
      ended: (reason) => {
        if (live() && reason === 'session-terminated') {
          // The session ended elsewhere (spec 0024 §8.5): every tab signs out.
          this.#injector.get(ɵTmSessionPaths).ɵsignedOutElsewhere();
        } else if (live()) {
          // The server ended the user's access to the tenant: the connection
          // stops, and a sync decides what the user sees; the connection
          // starts again only once it has closed.
          const stopped = this.#hub?.stop().catch((error: unknown) => this.#telemetry.error(error, { source: 'tm-leader' }));
          this.#terminal(generation, stopped);
        }
      },
    };
  }

  /**
   * A terminal signal: a background sync decides what it means. The
   * first sync that lands after the signal, this one or any later one such
   * as a poll's, restarts the connection, once a stop in progress has
   * settled; a sync reporting realtime off stops it instead.
   */
  #terminal(generation: number, stopped: Promise<unknown> = Promise.resolve()): void {
    // Spec 0025 §8.5: the sync the signal asks for, not the signal, decides what follows.
    const requested = this.#now();
    const signal = ++this.#signals;
    void this.#refresher.refresh(this.#t, ['tenant'], 'terminal');
    this.#resume?.destroy();
    this.#resume = null;
    const sync = this.#store.doc<ɵTmSyncDoc>(this.#partition, 'sync');
    void stopped.then(() => {
      // A later signal's wait replaces this one's; a connection stopped since never restarts.
      if (this.#stopped || generation !== this.#generation || signal !== this.#signals) {
        return;
      }
      const resume = (this.#resume = effect(
        () => {
          // A stamp ahead of the clock, left by a clock set back, proves nothing.
          const at = sync()?.data.at ?? -Infinity;
          if (at >= requested && at <= this.#now()) {
            untracked(() => {
              resume.destroy();
              this.#resume = null;
              if (this.#state !== 'disabled') {
                this.#hub?.start();
              }
            });
          }
        },
        { injector: this.#injector, manualCleanup: true },
      ));
    });
  }

  /** Enters a state, reports it, and re-arms the polls. */
  #set(state: TmHubState): void {
    if (this.#stopped || state === this.#state) {
      return;
    }
    const now = this.#now();
    const was = this.#state;
    this.#state = state;
    this.#since = now;
    if (state === 'connected') {
      this.#downSince = null;
      this.#polling = null;
    } else if (this.#downSince === null || was === 'disabled') {
      // The hub's minute starts when it drops, or when realtime comes on.
      this.#downSince = now;
    }
    this.#post();
    this.#arm();
  }

  /** Reports the connection to this tab and the others (§7.4). */
  #post(): void {
    const leaderVisible = this.#host.visible();
    this.#leader.ɵhubStatus.set({ state: this.#state, since: this.#since, leaderVisible });
    this.#bus.post({ type: 'hub.status', tenantId: this.#t, status: this.#state, since: this.#since, leaderVisible });
  }

  /** After every (re)connect: catch up on what the gap missed (§8.4). */
  #catchUp(): void {
    void this.#refresher.refresh(this.#t, ['tenant'], 'leader');
    this.#reads.forEach((reads) => this.#read(reads));
  }

  // --- Events (§8.4) ---

  /** Drops another tenant's event; relays the rest; delivers and acts on the ones this build knows. */
  #event(event: TmClientEvent): void {
    if (event.tenantId !== this.#t) {
      return;
    }
    this.#bus.post({ type: 'hub.event', tenantId: this.#t, event });
    if (!tmKnowsEvent(this.#metadata, event.name)) {
      return;
    }
    this.#leader.ɵdeliver(event);
    if (event.name === 'cache.changed') {
      if (isCacheChanged(event.payload)) {
        this.#refresher.ɵcacheChanged(this.#t, event.payload);
      }
      return;
    }
    // A leader document's event re-reads it, coalesced over 250 ms. The
    // session's end reaches the leader through the connection's `ended`.
    for (const reads of this.#reads) {
      if (reads.doc.events.includes(event.name)) {
        this.#read(reads, true);
      }
    }
  }

  // --- Leader documents (§8.6) ---

  /**
   * Re-reads a leader document: at once, or, for an event, once its 250 ms
   * have passed. One read is in flight at a time, and one more queues
   * behind it however many triggers arrive meanwhile.
   */
  #read(reads: TmDocReads, coalesce = false): void {
    if (this.#stopped) {
      return;
    }
    if (coalesce) {
      reads.timer ??= setTimeout(() => {
        reads.timer = undefined;
        this.#read(reads);
      }, COALESCE_MS);
      return;
    }
    clearTimeout(reads.timer);
    reads.timer = undefined;
    if (reads.inFlight) {
      reads.queued = true;
      return;
    }
    reads.inFlight = true;
    // The stored copy is stamped with the time the read was sent, so a copy
    // read later, by this tab or one that opened a panel, is never replaced
    // by an older read that lands after it (§2.1).
    const sentAt = (reads.sentAt = this.#now());
    const { name, route } = reads.doc;
    this.#api
      .read<unknown>(route, {}, { signal: this.#abort.signal, background: true, tenantId: this.#t })
      .then((data) => this.#store.put(this.#partition, name, { tag: null, savedAt: sentAt, data }))
      .catch((error: unknown) => {
        // A failed read waits for the next trigger: the HTTP layer and the
        // store report what needs reporting.
        if (!(error instanceof TmApiError) && !(error instanceof TmStoreRefusedError)) {
          this.#telemetry.error(error, { source: 'tm-leader' });
        }
      })
      .finally(() => {
        reads.inFlight = false;
        if (reads.queued) {
          reads.queued = false;
          this.#read(reads);
        }
      });
  }

  /** Whether a leader document's stored copy asks for polling. */
  #pollWhile(reads: TmDocReads): boolean {
    const pollWhile = reads.doc.pollWhile;
    const stored = pollWhile && untracked(this.#store.doc(this.#partition, reads.doc.name));
    try {
      return stored !== undefined && pollWhile !== undefined && pollWhile(stored.data);
    } catch (error) {
      this.#telemetry.error(error, { source: 'tm-leader' });
      return false;
    }
  }

  // --- Degraded mode: polling (§8.7) ---

  /** Why the leader polls now: realtime is off, or the hub has been down for a minute; else `null`. */
  #cause(): 'disabled' | 'hub-down' | null {
    if (this.#state === 'disabled') {
      return 'disabled';
    }
    return this.#downSince !== null && this.#now() - this.#downSince >= DEGRADED_AFTER_MS ? 'hub-down' : null;
  }

  /**
   * Runs the polls that are due: in degraded mode, a sync of the tenant and
   * a read of every leader document, each at most once a minute; otherwise
   * a read of each leader document whose stored copy asks for it, as often.
   *
   * @returns When the next poll falls due, in epoch milliseconds, or `Infinity`.
   */
  #poll(): number {
    if (this.#stopped) {
      return Infinity;
    }
    const now = this.#now();
    const cause = this.#cause();
    if (cause !== this.#polling) {
      this.#polling = cause;
      if (cause !== null) {
        this.#telemetry.event('tm.realtime.polling', { cause });
      }
    }
    let next = Infinity;
    if (cause !== null) {
      if (now >= this.#polledAt + POLL_MS) {
        this.#polledAt = now;
        void this.#refresher.refresh(this.#t, ['tenant'], 'poll');
      }
      next = this.#polledAt + POLL_MS;
    }
    for (const reads of this.#reads) {
      if (cause !== null || this.#pollWhile(reads)) {
        if (now >= reads.sentAt + POLL_MS) {
          this.#read(reads);
        }
        // A read sent now is due again in a minute; so is one still in
        // flight from over a minute ago, behind which the read above queued.
        const due = reads.sentAt + POLL_MS;
        next = Math.min(next, due > now ? due : now + POLL_MS);
      }
    }
    return next;
  }

  /**
   * The leader's own poll timer, which runs while the leader is visible:
   * it polls what is due now, then waits for the next poll or for the
   * minute after which a down hub counts as degraded.
   */
  #arm(): void {
    clearTimeout(this.#timer);
    this.#timer = undefined;
    if (this.#stopped || !this.#host.visible()) {
      return;
    }
    const now = this.#now();
    let next = this.#poll();
    if (this.#state !== 'disabled' && this.#downSince !== null && this.#polling === null) {
      next = Math.min(next, this.#downSince + DEGRADED_AFTER_MS);
    }
    if (next < Infinity) {
      this.#timer = setTimeout(() => this.#arm(), Math.max(next - now, 0));
    }
  }
}
