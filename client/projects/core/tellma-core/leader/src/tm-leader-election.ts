// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  DestroyRef,
  DOCUMENT,
  effect,
  inject,
  Injectable,
  InjectionToken,
  Injector,
  runInInjectionContext,
  untracked,
} from '@angular/core';
import {
  TM_NOW,
  TmCacheRefresher,
  TmClientStore,
  TmLeader,
  TmSession,
  TmTabBus,
  TmTelemetry,
  type TmBusMessage,
  type TmHubStatusMessage,
  type TmLeaderDocument,
  type TmPartition,
  ɵTM_API_HOST,
  ɵTM_UNIQUE_LEADER_DOCUMENTS,
  ɵTmClientMetadataStore,
} from '@tellma/core';

import { tmAccessEnded, tmKnowsEvent, TmLeadership } from './tm-leadership';

// Leader election (spec 0025 §7). While a tab shows tenant t to a signed-in
// user, it queues on the exclusive Web Lock tm:<slug>:<sub>:<t>:hub; the
// holder is the tenant's leader, and the other tabs follow it. The election
// needs no timer in the healthy case: the browser hands the lock to the next
// queued tab when the leader closes, crashes or is discarded. A follower
// checks on the leader only when it starts or becomes visible, and every
// minute while visible if the leader's hub is down or a leader document asks
// for polling; it steals the lock only while visible, from a leader that does
// not answer within 5 s, or from a hidden one whose hub has been down for
// 30 s. Leaving the tenant, `pagehide`, `freeze` and a session that is not
// active release the lock, or abort the queued request; `resume` and a
// `pageshow` from the back-forward cache queue again.

/** Why a tab became the leader, as the telemetry reports it. */
type TmHow = 'granted' | 'stolen-hung' | 'stolen-disconnected';

/** Why a leader let the lock go, as the telemetry reports it. */
type TmWhy = 'tenant' | 'pagehide' | 'freeze' | 'session' | 'stolen';

/** A tenant's partition of the signed-in user. */
type TmTenantPartition = Extract<TmPartition, { readonly kind: 'tenant' }>;

/**
 * The browser's Web Locks, or `null` where the page has none, such as one
 * served over plain HTTP from another machine: each tab then leads its
 * tenant alone. Internal: a test gives each of its tabs a lock manager of
 * its own.
 */
export const TM_LEADER_LOCKS = new InjectionToken<LockManager | null>('TM_LEADER_LOCKS', {
  providedIn: 'root',
  factory: () => inject(DOCUMENT).defaultView?.navigator.locks ?? null,
});

/** How long a follower waits for the leader's answer before it counts as hung. */
const HUNG_MS = 5_000;

/** How long a hidden leader's hub may be down before a visible follower takes over. */
const DISCONNECTED_MS = 30_000;

/** The longest random wait before a steal, so two followers rarely steal at once. */
const STEAL_JITTER_MS = 2_000;

/** How often a visible follower checks on a leader whose hub is down. */
const PROBE_MS = 60_000;

/** One probe of the leader: who held the lock then, and whether the leader has answered since. */
interface TmProbe {
  /** The holder's client id, once queried; `null` when the lock was free. */
  holder: string | null | undefined;
  /** Whether a `hub.status` arrived since the probe was posted. */
  answered: boolean;
}

/** One tab's part in the election of one tenant, from entering the tenant to leaving it. */
interface TmTerm {
  /** The tenant's partition. */
  readonly partition: TmTenantPartition;
  /** The hub lock's name. */
  readonly lock: string;
  /** Whether the term ended. */
  ended: boolean;
  /** Aborts the queued request, while one is queued. */
  request: AbortController | null;
  /** Releases the lock, while the tab holds it. */
  release: (() => void) | null;
  /** The leader's work, while the tab holds the lock. */
  leadership: TmLeadership | null;
  /** The leader's last report, as a follower received it. */
  status: TmHubStatusMessage | null;
  /** The follower's last probe. */
  probe: TmProbe | null;
  /** Whether a steal is waiting out its jitter. */
  stealing: boolean;
  /** The follower's timers: the hung check, the steal's jitter, the disconnected check. */
  readonly timers: Set<ReturnType<typeof setTimeout>>;
  /** The follower's minute between probes. */
  tick: ReturnType<typeof setTimeout> | undefined;
}

/**
 * Runs the leader election of the routed tenant in this tab, and the leader's
 * work while this tab leads. Internal: the startup creates it after the first
 * render, and `TmLeader` reports what it decides.
 */
@Injectable({ providedIn: 'root' })
export class TmLeaderElection {
  readonly #injector = inject(Injector);
  readonly #leader = inject(TmLeader);
  readonly #bus = inject(TmTabBus);
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #session = inject(TmSession);
  readonly #host = inject(ɵTM_API_HOST);
  readonly #now = inject(TM_NOW);
  readonly #telemetry = inject(TmTelemetry);
  readonly #locks = inject(TM_LEADER_LOCKS);
  readonly #metadata = inject(ɵTmClientMetadataStore);
  readonly #docs = inject(ɵTM_UNIQUE_LEADER_DOCUMENTS);

  /** This tab's term, while it shows a tenant to an active session. */
  #term: TmTerm | null = null;
  /** Settles once the last leadership's connection closed; the next request waits for it. */
  #stopping: Promise<void> = Promise.resolve();
  /** The page events that keep the tab out of the election until they are undone. */
  readonly #suspended = new Set<'pagehide' | 'freeze'>();
  /** When the page last came back from the back-forward cache or a freeze, in epoch milliseconds. */
  #returnedAt = -Infinity;
  #destroyed = false;

  /** Starts the election, which follows the routed tenant, the session and the page's life. */
  constructor() {
    // --- The page's life: pagehide and freeze release; pageshow and resume rejoin (spec 0025 §7.2 steps 3-4) ---
    const stops = [
      this.#host.listen('window', 'pagehide', () => this.#suspend('pagehide', true)),
      this.#host.listen('window', 'pageshow', () => this.#suspend('pagehide', false)),
      this.#host.listen('document', 'freeze', () => this.#suspend('freeze', true)),
      this.#host.listen('document', 'resume', () => this.#suspend('freeze', false)),
      this.#host.listen('document', 'visibilitychange', () => this.#visibility()),
    ];
    const subscription = this.#bus.messages.subscribe((message) => this.#received(message));

    // --- The routed tenant and the session: enter, switch or leave ---
    effect(() => {
      this.#refresher.ɵpartition();
      this.#session.state();
      untracked(() => this.#reconcile());
    });

    inject(DestroyRef).onDestroy(() => {
      this.#destroyed = true;
      stops.forEach((stop) => stop());
      subscription.unsubscribe();
      if (this.#term !== null) {
        this.#end(this.#term, null);
      }
    });
  }

  // --- The term: enter, leave ---

  /** Marks a page event, or its undoing, and joins or leaves the election accordingly. */
  #suspend(kind: 'pagehide' | 'freeze', on: boolean): void {
    if (on) {
      this.#suspended.add(kind);
    } else if (this.#suspended.delete(kind)) {
      // Back: a leadership it begins revalidates what the pause missed.
      this.#returnedAt = this.#now();
    } else {
      return;
    }
    this.#reconcile();
  }

  /** Ends the term the tab should no longer hold, and begins the one it should. */
  #reconcile(): void {
    const partition = untracked(this.#refresher.ɵpartition);
    const active = untracked(this.#session.state) === 'active';
    const want = !this.#destroyed && this.#suspended.size === 0 && active && partition !== null;
    const term = this.#term;
    if (term !== null && (!want || term.partition.tenantId !== partition.tenantId || term.partition.sub !== partition.sub)) {
      const why: TmWhy | null = this.#destroyed
        ? null
        : this.#suspended.has('freeze')
          ? 'freeze'
          : this.#suspended.has('pagehide')
            ? 'pagehide'
            : !active || (partition !== null && partition.sub !== term.partition.sub)
              ? 'session'
              : 'tenant';
      this.#end(term, why);
    }
    if (want && this.#term === null) {
      this.#begin(partition);
    }
  }

  /** Enters a tenant: queue on its hub lock, probe the leader (spec 0025 §7.2 step 1). */
  #begin(partition: TmTenantPartition): void {
    const slug = this.#metadata.peek()?.distribution.slug ?? '';
    const term: TmTerm = {
      partition,
      lock: `tm:${slug}:${partition.sub}:${partition.tenantId}:hub`,
      ended: false,
      request: null,
      release: null,
      leadership: null,
      status: null,
      probe: null,
      stealing: false,
      timers: new Set(),
      tick: undefined,
    };
    this.#term = term;
    this.#leader.ɵisLeader.set(false);
    this.#leader.ɵhubStatus.set({ state: 'connecting', since: this.#now(), leaderVisible: true });
    // A tab whose leadership was stolen queues again once its connection closed.
    void this.#stopping.then(() => this.#request(term, null));
    this.#probe(term);
    this.#tick(term);
  }

  /**
   * Ends a term: aborts its queued request, or, for the leader, stops the
   * leader's work and releases the lock.
   */
  #end(term: TmTerm, why: TmWhy | null): void {
    term.ended = true;
    this.#calm(term);
    term.request?.abort();
    term.request = null;
    const leadership = term.leadership;
    term.leadership = null;
    if (leadership !== null) {
      this.#stopping = leadership.stop();
      this.#leader.ɵisLeader.set(false);
      if (why !== null) {
        this.#telemetry.event('tm.realtime.leader-released', { why });
      }
    }
    term.release?.();
    term.release = null;
    if (this.#term === term) {
      this.#term = null;
    }
  }

  /** Stops a term's follower timers. */
  #calm(term: TmTerm): void {
    term.timers.forEach((timer) => clearTimeout(timer));
    term.timers.clear();
    clearTimeout(term.tick);
    term.tick = undefined;
  }

  // --- The lock ---

  /**
   * Requests the hub lock: queued and abortable, or, for a steal, at once.
   * The lock stays held until the term releases it, or another tab steals it.
   */
  #request(term: TmTerm, steal: Exclude<TmHow, 'granted'> | null): void {
    if (term.ended || term !== this.#term || term.leadership !== null) {
      return;
    }
    const locks = this.#locks;
    if (locks === null) {
      // No Web Locks: the tab shares no lock with its other tabs, so it leads alone.
      this.#granted(term, 'granted');
      return;
    }
    let release!: () => void;
    const held = new Promise<void>((resolve) => (release = resolve));
    const controller = steal === null ? new AbortController() : null;
    term.request = controller;
    const options: LockOptions = controller === null ? { steal: true } : { signal: controller.signal };
    locks
      .request(term.lock, options, () => {
        if (term.ended || term !== this.#term || term.leadership !== null) {
          // Granted after the tab left, or a steal granted after the queued
          // request it raced with already led: the lock goes at once, and the
          // leadership it was taken from ends as stolen.
          return undefined;
        }
        if (term.request === controller) {
          term.request = null;
        }
        term.release = release;
        this.#granted(term, steal ?? 'granted');
        return held;
      })
      .catch((error: unknown) => {
        if (term.release === release && !term.ended) {
          // Another tab stole the lock this tab held.
          this.#end(term, 'stolen');
          this.#reconcile();
        } else if ((error as { readonly name?: unknown } | null)?.name !== 'AbortError') {
          this.#telemetry.error(error, { source: 'tm-leader' });
        }
      });
  }

  /** The lock is this tab's: it leads (§7.2 step 2). */
  #granted(term: TmTerm, how: TmHow): void {
    this.#calm(term);
    this.#telemetry.event('tm.realtime.leader-acquired', { how });
    this.#leader.ɵisLeader.set(true);
    try {
      const seenSince = Math.max(performance.timeOrigin, this.#returnedAt);
      term.leadership = runInInjectionContext(this.#injector, () => new TmLeadership(term.partition, seenSince));
    } catch (error) {
      // A leader that cannot start lets the lock go, so another tab may lead.
      this.#telemetry.error(error, { source: 'tm-leader' });
      this.#leader.ɵisLeader.set(false);
      term.ended = true;
      term.release?.();
      if (this.#term === term) {
        this.#term = null;
      }
    }
  }

  /** The client id of the lock's holder, or `null` when it is free or unknown. */
  async #holder(term: TmTerm): Promise<string | null> {
    try {
      const snapshot = await this.#locks?.query();
      return snapshot?.held?.find((lock) => lock.name === term.lock)?.clientId ?? null;
    } catch {
      return null;
    }
  }

  // --- The follower: probing and stealing (§7.3, §7.4) ---

  /** Asks the leader to report, and notes who holds the lock. */
  #probe(term: TmTerm): void {
    if (term.ended || term.leadership !== null) {
      return;
    }
    const probe: TmProbe = { holder: undefined, answered: false };
    term.probe = probe;
    this.#bus.post({ type: 'leader.probe', tenantId: term.partition.tenantId });
    void this.#holder(term).then((holder) => (probe.holder = holder));
    this.#after(term, HUNG_MS, () => {
      // Hung: no report within 5 s while the lock is still held.
      if (this.#holds(term, 'stolen-hung', probe)) {
        void this.#holder(term).then((holder) => holder !== null && this.#candidate(term, 'stolen-hung', probe));
      }
    });
  }

  /** The follower's minute between probes, while it is visible; it probes when the leader's hub is down or a document asks for polling. */
  #tick(term: TmTerm): void {
    clearTimeout(term.tick);
    term.tick = undefined;
    if (term.ended || term.leadership !== null || !this.#host.visible()) {
      return;
    }
    term.tick = setTimeout(() => {
      term.tick = undefined;
      if (untracked(this.#leader.hubStatus).state !== 'connected' || this.#docs.some((doc) => this.#pollWhile(term, doc))) {
        this.#probe(term);
      }
      this.#tick(term);
    }, PROBE_MS);
  }

  /** Whether a leader document's stored copy asks for polling, by the document's own rule. */
  #pollWhile(term: TmTerm, doc: TmLeaderDocument): boolean {
    const pollWhile = doc.pollWhile;
    const stored = pollWhile && untracked(this.#store.doc(term.partition, doc.name));
    try {
      return stored !== undefined && pollWhile !== undefined && pollWhile(stored.data);
    } catch {
      return false;
    }
  }

  /** Whether the follower's last report shows a hidden leader whose hub has been down for over 30 s. */
  #disconnected(term: TmTerm): boolean {
    const status = term.status;
    return (
      status !== null &&
      !status.leaderVisible &&
      (status.status === 'connecting' || status.status === 'reconnecting' || status.status === 'offline') &&
      this.#now() - status.since > DISCONNECTED_MS
    );
  }

  /** Whether a steal's condition still holds: a visible follower, the same probe, and a hung or hidden disconnected leader. */
  #holds(term: TmTerm, how: Exclude<TmHow, 'granted'>, probe: TmProbe): boolean {
    return (
      !term.ended &&
      term === this.#term &&
      term.leadership === null &&
      term.probe === probe &&
      this.#host.visible() &&
      (how === 'stolen-hung' ? !probe.answered : this.#disconnected(term))
    );
  }

  /**
   * Judges the leader's last report: a hidden leader whose hub has been down
   * for over 30 s is a candidate now, and one down for less is judged again
   * once the 30 s are up. Never a visible leader, nor a disabled one.
   */
  #judge(term: TmTerm): void {
    const { status, probe } = term;
    if (status === null || probe === null || term.leadership !== null || !this.#host.visible()) {
      return;
    }
    if (this.#disconnected(term)) {
      this.#candidate(term, 'stolen-disconnected', probe);
    } else if (!status.leaderVisible && status.status !== 'connected' && status.status !== 'disabled') {
      this.#after(term, status.since + DISCONNECTED_MS + 1 - this.#now(), () => term.status === status && this.#judge(term));
    }
  }

  /**
   * Steals the lock after a 0-2 s jitter, when the condition still holds and
   * the holder is the one the probe noted; a new holder means another
   * follower stole first, so the follower probes again instead.
   */
  #candidate(term: TmTerm, how: Exclude<TmHow, 'granted'>, probe: TmProbe): void {
    if (term.stealing) {
      return;
    }
    term.stealing = true;
    this.#after(term, Math.random() * STEAL_JITTER_MS, () => {
      term.stealing = false;
      if (!this.#holds(term, how, probe)) {
        return;
      }
      void this.#holder(term).then((holder) => {
        if (!this.#holds(term, how, probe) || holder === null) {
          // The condition passed, or the lock is free, and the queue grants it.
          return;
        }
        if (holder !== probe.holder) {
          this.#probe(term);
          return;
        }
        // The queued request goes first, so the lock is never granted to it after the steal.
        term.request?.abort();
        term.request = null;
        this.#request(term, how);
      });
    });
  }

  /** Runs work after a delay, unless the term's follower timers are stopped first. */
  #after(term: TmTerm, ms: number, work: () => void): void {
    const timer = setTimeout(() => {
      term.timers.delete(timer);
      work();
    }, Math.max(ms, 0));
    term.timers.add(timer);
  }

  // --- The bus and the page's visibility ---

  /** A message from another tab about the routed tenant. */
  #received(message: TmBusMessage): void {
    const term = this.#term;
    if (term === null || !('tenantId' in message) || message.tenantId !== term.partition.tenantId) {
      return;
    }
    if (message.type === 'leader.probe') {
      // The leader answers at once, even while hidden (§7.4).
      term.leadership?.probe();
    } else if (term.leadership === null) {
      if (message.type === 'hub.status') {
        // A follower mirrors the leader's report, and judges it.
        term.status = message;
        if (term.probe !== null) {
          term.probe.answered = true;
        }
        this.#leader.ɵhubStatus.set({ state: message.status, since: message.since, leaderVisible: message.leaderVisible });
        this.#judge(term);
      } else if (message.type === 'hub.event' && message.event.tenantId === term.partition.tenantId) {
        // The server ended the user's access to the tenant: every follower
        // syncs too, the one hub event on which a follower fetches (§8.5).
        // The session's own end reaches it as the leader's sign-out message.
        if (tmAccessEnded(message.event)) {
          void this.#refresher.refresh(term.partition.tenantId, ['tenant'], 'terminal');
        }
        // A relayed event reaches this tab's subscribers when its build knows the name (§8.4).
        if (tmKnowsEvent(this.#metadata, message.event.name)) {
          this.#leader.ɵdeliver(message.event);
        }
      }
    }
  }

  /** The page was shown or hidden: the leader reports it; a follower probes when shown, and pauses when hidden. */
  #visibility(): void {
    const term = this.#term;
    if (term === null) {
      return;
    }
    if (term.leadership !== null) {
      term.leadership.visibility();
    } else if (this.#host.visible()) {
      this.#probe(term);
      this.#tick(term);
    } else {
      this.#calm(term);
      term.stealing = false;
    }
  }
}
