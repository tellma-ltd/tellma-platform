// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { computed, DestroyRef, effect, inject, Injectable, Injector, runInInjectionContext, type Signal, signal, untracked } from '@angular/core';
import {
  TM_NOW,
  TmApi,
  type TmAssignmentsSummary,
  TmCacheRefresher,
  TmClientStore,
  type TmDecimal,
  TmDecimals,
  type TmInboxSummary,
  type TmInboxVerbResult,
  type TmJson,
  TmLaunchCache,
  TmLeader,
  TmLocalization,
  type TmPartition,
  type TmStoredDoc,
  TmTelemetry,
} from '@tellma/core';
import { TM_UI_TRANSLATE, TmL10n } from '@tellma/core-ui';

import type { TmAttentionCalls, tmApplyOverlays } from './tm-attention-calls';
import { tmRegisterAttentionStrings } from './tm-attention-strings';

// The attention model (spec 0028 §5.2–§5.4): the bell's and the tray's
// badges from the stored attention documents, the panel that is open, and
// the attention verbs. A verb applies in the same frame through this tab's
// overlay, then calls the server; the overlay drops when a copy arrives
// whose inbox version is at or above the one the verb returned, which
// contains the change whatever order the copies were read and saved in.
// Opening a panel quiets its badge through a seen overlay until a copy that
// contains the `seen` arrives, then reads the panel's document once. The
// server calls load as a chunk with the first one; every overlay applies
// here, in the same frame, whether or not the chunk has loaded.

/** A panel of the app bar. */
export type TmPanel = 'bell' | 'tray';

/** The bell button's badge. */
export interface TmBellBadge {
  /** The unseen notifications, capped at 100; `null` while there is no document or while the panel quiets it. */
  readonly count: number | null;
  /** `attention` while the unseen ones include a warning or an error. */
  readonly tone: 'accent' | 'attention';
  /** The user's jobs that are pending or running, which draw the ring. */
  readonly jobsRunning: number;
  /** The button's accessible name, such as "Notifications, 3 new, needs attention, 1 job running". */
  readonly label: string;
}

/** The tray button's badge. */
export interface TmTrayBadge {
  /** The open assignments to the user, capped at 100; `null` while there is no document. */
  readonly count: number | null;
  /** `accent` while some are new and the panel has not quieted them. */
  readonly tone: 'accent' | 'neutral';
  /** The button's accessible name, such as "My assignments, 4 open, 1 new". */
  readonly label: string;
}

/** A tenant's partition. */
type TenantPartition = Extract<TmPartition, { readonly kind: 'tenant' }>;

/** An inbox verb that overlays the inbox. */
type InboxVerb = 'read' | 'read-all' | 'clear' | 'clear-read';

/** One inbox verb's overlay. Internal: the server calls' chunk applies it. */
export interface TmInboxOverlay {
  /** Tells overlays apart across updates. */
  readonly id: number;
  /** The verb. */
  readonly verb: InboxVerb;
  /** The notifications it names; `null` for a verb over all of them. */
  readonly ids: ReadonlySet<number> | null;
  /** The inbox version the verb returned; `null` while it is in flight. */
  readonly version: TmDecimal | null;
}

/** What releases a sent `seen`'s overlay: a copy at or above the version (bell) or `seenAt` (tray) it returned. */
export type TmSeenRelease = { readonly kind: 'version'; readonly version: TmDecimal } | { readonly kind: 'seen-at'; readonly at: number };

/**
 * A seen overlay: while it holds, the bell hides its count or the tray turns
 * neutral. It holds while the `seen` is in flight, then until the copy that
 * releases it; with no `seen` sent, until the panel's own read lands, or the
 * panel closes before it does.
 */
type SeenHold = { readonly kind: 'pending' } | TmSeenRelease | { readonly kind: 'own'; readonly sentAt: number | null };

/** The summaries' count cap: a count at it reads "99+". */
const CAP = 100;

/** How long the hub stays neither connected nor disabled before live updates read as paused. */
const PAUSE_MS = 5000;

/** Whether a seen overlay still holds over the stored copy. */
function holds(hold: SeenHold | null, copy: TmStoredDoc<{ readonly version?: TmDecimal; readonly seenAt?: string }> | undefined): boolean {
  switch (hold?.kind) {
    case undefined:
      return false;
    case 'pending':
      return true;
    case 'version':
      return copy?.data.version === undefined || TmDecimals.compare(copy.data.version, hold.version) < 0;
    case 'seen-at':
      return !(Date.parse(copy?.data.seenAt ?? '') >= hold.at);
    case 'own':
      return hold.sentAt === null || copy === undefined || copy.savedAt < hold.sentAt;
  }
}

/** The panel-opened event's badge bucket. */
function bucket(count: number | null): string {
  return !count ? '0' : count < 10 ? '1-9' : '10+';
}

/**
 * The attention model of the routed tenant: the stored inbox and
 * assignments with this tab's overlays applied, the bell's and the tray's
 * badges, the open panel, whether live updates flow and whether the verbs
 * may run, and the verbs themselves. A tenant change drops the overlays and
 * closes the panel.
 *
 * @example
 * ```ts
 * const attention = inject(TmAttention);
 * const unseen = computed(() => attention.bell().count);
 * attention.toggle('bell', 'click');
 * await attention.read([4102]);
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmAttention {
  readonly #store = inject(TmClientStore);
  readonly #refresher = inject(TmCacheRefresher);
  readonly #launch = inject(TmLaunchCache);
  readonly #api = inject(TmApi);
  readonly #leader = inject(TmLeader);
  readonly #telemetry = inject(TmTelemetry);
  readonly #l10n = inject(TmLocalization);
  readonly #numbers = inject(TmL10n);
  readonly #translate = inject(TM_UI_TRANSLATE);
  readonly #now = inject(TM_NOW);
  readonly #injector = inject(Injector);

  /** The inbox verbs' overlays, oldest first. */
  readonly #overlays = signal<readonly TmInboxOverlay[]>([]);

  /** The jobs this tab asked to stop, until a copy shows the request. */
  readonly #stopping = signal<ReadonlySet<number>>(new Set());

  /** Each panel's seen overlay. */
  readonly #seen = { bell: signal<SeenHold | null>(null), tray: signal<SeenHold | null>(null) };

  /** The open panel. */
  readonly #open = signal<TmPanel | null>(null);

  /** Whether live updates read as paused. */
  readonly #paused = signal(false);

  /** Each open panel's read, which closing it aborts. */
  readonly #reads = new Map<TmPanel, AbortController>();

  /** The routed tenant's life: leaving it aborts the reads it started. */
  #tenantLife = new AbortController();

  /** The next overlay's id. */
  #nextOverlay = 1;

  /** The server calls, once their chunk has loaded. */
  #calls: Promise<TmAttentionCalls> | null = null;

  /**
   * Applies the overlays to a copy, once the server calls' chunk has
   * loaded: opening a panel loads it, so a panel's verbs apply in the frame
   * they are made; a verb made before it ever loaded applies as it lands,
   * with the verb's call.
   */
  readonly #apply = signal<typeof tmApplyOverlays | null>(null);

  /** The routed tenant's partition. */
  readonly #partition: Signal<TenantPartition | null> = this.#refresher.ɵpartition;

  /** The stored inbox copy. */
  readonly #inboxCopy = this.#copy<TmInboxSummary>('inbox');

  /** The stored assignments copy. */
  readonly #assignmentsCopy = this.#copy<TmAssignmentsSummary>('assignments');

  /** The stored inbox with this tab's overlays applied; `undefined` before the first copy. */
  readonly inbox: Signal<TmInboxSummary | undefined> = computed(() => {
    const copy = this.#inboxCopy()?.data;
    return copy && this.#overlaid(copy);
  });

  /** The stored assignments; `undefined` before the first copy. */
  readonly assignments: Signal<TmAssignmentsSummary | undefined> = computed(() => this.#assignmentsCopy()?.data);

  /** When the stored inbox copy was read, in epoch milliseconds; `null` before the first copy. Internal: the bell's status line reads it. */
  readonly ɵinboxSavedAt: Signal<number | null> = computed(() => this.#inboxCopy()?.savedAt ?? null);

  /** The bell's badge. */
  readonly bell: Signal<TmBellBadge> = computed(() => {
    const inbox = this.inbox();
    const count = inbox === undefined || holds(this.#seen.bell(), this.#inboxCopy()) ? null : inbox.unseen;
    const severity = inbox?.maxUnseenSeverity;
    const tone = count && (severity === 'Warning' || severity === 'Error') ? 'attention' : 'accent';
    const jobsRunning = inbox?.running.filter((job) => job.status === 'Pending' || job.status === 'Running').length ?? 0;
    const label = this.#join([
      this.#text('bell.label'),
      count ? this.#text('bell.new', { count: this.#count(count) }) : null,
      tone === 'attention' ? this.#text('bell.attention') : null,
      jobsRunning ? this.#text('bell.jobs', { count: jobsRunning }) : null,
    ]);
    return { count, tone, jobsRunning, label };
  });

  /** The tray's badge. */
  readonly tray: Signal<TmTrayBadge> = computed(() => {
    const assignments = this.assignments();
    const count = assignments?.open ?? null;
    const fresh = assignments !== undefined && assignments.new > 0 && !holds(this.#seen.tray(), this.#assignmentsCopy());
    const label = this.#join([
      this.#text('tray.label'),
      count ? this.#text('tray.open', { count: this.#count(count) }) : null,
      fresh ? this.#text('tray.new', { count: this.#count(assignments.new) }) : null,
    ]);
    return { count, tone: fresh ? 'accent' : 'neutral', label };
  });

  /** The open panel, if any. */
  readonly open: Signal<TmPanel | null> = this.#open.asReadonly();

  /** `paused` while the realtime hub has been neither connected nor disabled for 5 s. */
  readonly live: Signal<'live' | 'paused'> = computed(() => (this.#paused() ? 'paused' : 'live'));

  /** Whether the verbs may run: the tenant is not frozen, and the app is online. */
  readonly writable: Signal<boolean> = computed(() => this.#launch.docs.tenant()?.state !== 'Frozen' && this.#api.online());

  /** Creates the model, which follows the routed tenant and the hub's status. */
  constructor() {
    tmRegisterAttentionStrings(this.#l10n);
    inject(DestroyRef).onDestroy(() => this.#reset());

    // --- Live updates: paused once the hub has been down for 5 s ---
    effect((onCleanup) => {
      const { state, since } = this.#leader.hubStatus();
      const wait = state === 'connected' || state === 'disabled' ? Infinity : since + PAUSE_MS - this.#now();
      this.#paused.set(wait <= 0);
      if (wait > 0 && wait !== Infinity) {
        const timer = setTimeout(() => this.#paused.set(true), wait);
        onCleanup(() => clearTimeout(timer));
      }
    });

    // --- A tenant change: the overlays, the panel and the reads belong to the tenant left ---
    const keyOf = (partition: TenantPartition | null): string | null => partition && `${partition.sub}/${partition.tenantId}`;
    let tenant = keyOf(untracked(this.#partition));
    effect(() => {
      const key = keyOf(this.#partition());
      if (key !== tenant) {
        tenant = key;
        untracked(() => this.#reset());
      }
    });

    // --- Released overlays leave the lists, so they never grow ---
    effect(() => {
      const copy = this.#inboxCopy()?.data;
      const overlays = this.#overlays();
      const stopping = this.#stopping();
      if (copy === undefined) {
        return;
      }
      const held = overlays.filter((overlay) => overlay.version === null || TmDecimals.compare(copy.version, overlay.version) < 0);
      if (held.length !== overlays.length) {
        this.#overlays.set(held);
      }
      const asked = [...stopping].filter((id) => copy.running.some((job) => job.id === id && job.cancelRequestedAt === undefined));
      if (asked.length !== stopping.size) {
        this.#stopping.set(new Set(asked));
      }
    });
    for (const panel of ['bell', 'tray'] as const) {
      const copy = panel === 'bell' ? this.#inboxCopy : this.#assignmentsCopy;
      effect(() => {
        const hold = this.#seen[panel]();
        if (hold !== null && !holds(hold, copy())) {
          this.#seen[panel].set(null);
        }
      });
    }
  }

  /**
   * Opens a panel, or closes it when it is open. Opening shows the stored
   * document at once and quiets the badge; when the document shows
   * something new and the verbs may run, it marks it seen; then it reads
   * the document once in the background. Closing aborts that read.
   *
   * @param panel - The panel.
   * @param via - What opened it.
   */
  toggle(panel: TmPanel, via: 'click' | 'shortcut' | 'toast'): void {
    untracked(() => {
      const open = this.#open();
      if (open !== null) {
        this.#close(open);
      }
      if (open === panel) {
        return;
      }
      const badge = panel === 'bell' ? this.bell().count : this.tray().count;
      this.#open.set(panel);
      this.#telemetry.event('tm.attention.panel-opened', { panel, via, badge: bucket(badge) });
      void this.#opened(panel);
    });
  }

  /**
   * Reads an attention document once and stores it, so every tab
   * re-renders: what a panel does after it opens, and the bell's Refresh.
   * Nothing is read offline, or for a document the app does not register.
   *
   * @param document - The document.
   * @returns A promise that settles once the copy is stored, or the read failed.
   */
  refresh(document: 'inbox' | 'assignments'): Promise<void> {
    const partition = untracked(this.#partition);
    const life = this.#tenantLife.signal;
    return partition === null ? Promise.resolve() : this.#call((calls) => calls.read(partition, document, life)).then(() => undefined);
  }

  /**
   * Marks notifications read: at once in this tab, then on the server.
   *
   * @param ids - The notifications.
   * @returns A promise that settles once the server answered; a failure toasts.
   */
  read(ids: readonly number[]): Promise<void> {
    return ids.length === 0 ? Promise.resolve() : this.#verb('read', new Set(ids), 'inbox/read', { ids: [...ids] }, () => this.read(ids));
  }

  /**
   * Marks every notification read.
   *
   * @returns A promise that settles once the server answered; a failure toasts.
   */
  readAll(): Promise<void> {
    return this.#verb('read-all', null, 'inbox/read-all', {}, () => this.readAll());
  }

  /**
   * Clears notifications for the user: at once in this tab, then on the server.
   *
   * @param ids - The notifications.
   * @returns A promise that settles once the server answered; a failure toasts.
   */
  clear(ids: readonly number[]): Promise<void> {
    return ids.length === 0 ? Promise.resolve() : this.#verb('clear', new Set(ids), 'inbox/clear', { ids: [...ids] }, () => this.clear(ids));
  }

  /**
   * Clears every read notification for the user.
   *
   * @returns A promise that settles once the server answered; a failure toasts.
   */
  clearRead(): Promise<void> {
    return this.#verb('clear-read', null, 'inbox/clear-read', {}, () => this.clearRead());
  }

  /**
   * Asks one of the user's jobs to stop: the job reads as stopping at once,
   * until a copy shows the request or no longer lists the job. The caller
   * confirms first where the job's handler asks for it.
   *
   * @param id - The job.
   * @returns A promise that settles once the server answered; a failure toasts.
   */
  async stopJob(id: number): Promise<void> {
    const partition = untracked(this.#partition);
    if (partition === null || !untracked(this.writable)) {
      return;
    }
    this.#stopping.update((ids) => new Set([...ids, id]));
    const result = await this.#call((calls) => calls.verb('stop-job', 'jobs/cancel', { ids: [id] }, partition.tenantId, () => void this.stopJob(id)));
    if (result === null) {
      // A failed stop drops its overlay at once.
      this.#stopping.update((ids) => new Set([...ids].filter((candidate) => candidate !== id)));
    }
  }

  // --- The overlays ---

  /** A stored document of the routed tenant, with its `savedAt`. */
  #copy<T>(name: string): Signal<TmStoredDoc<T> | undefined> {
    return computed(() => {
      const partition = this.#partition();
      return partition === null ? undefined : this.#store.doc<T>(partition, name)();
    });
  }

  /** The inbox copy with the overlays that still hold applied. */
  #overlaid(copy: TmInboxSummary): TmInboxSummary {
    const overlays = this.#overlays().filter((overlay) => overlay.version === null || TmDecimals.compare(copy.version, overlay.version) < 0);
    const stopping = this.#stopping();
    const apply = this.#apply();
    return (overlays.length === 0 && stopping.size === 0) || apply === null ? copy : apply(copy, overlays, stopping, this.#now());
  }

  /** Runs an inbox verb: its overlay at once, then the server, whose version releases the overlay. */
  async #verb(verb: InboxVerb, ids: ReadonlySet<number> | null, route: string, body: TmJson, retry: () => Promise<void>): Promise<void> {
    const partition = untracked(this.#partition);
    if (partition === null || !untracked(this.writable)) {
      return;
    }
    const id = this.#nextOverlay++;
    this.#overlays.update((overlays) => [...overlays, { id, verb, ids, version: null }]);
    const result = await this.#call((calls) => calls.verb<TmInboxVerbResult>(verb, route, body, partition.tenantId, () => void retry()));
    // The answer's version releases the overlay; a failed verb drops it at once.
    this.#overlays.update((overlays) =>
      result === null
        ? overlays.filter((overlay) => overlay.id !== id)
        : overlays.map((overlay) => (overlay.id === id ? { ...overlay, version: result.version } : overlay)),
    );
  }

  /** Runs a server call once the calls' chunk has loaded; a chunk that cannot load is reported, answers `null` and loads again next time. */
  #call<T>(run: (calls: TmAttentionCalls) => Promise<T>): Promise<T | null> {
    this.#calls ??= import('./tm-attention-calls').then(({ TmAttentionCalls, tmApplyOverlays: apply }) => {
      this.#apply.set(apply);
      return runInInjectionContext(this.#injector, () => new TmAttentionCalls());
    });
    return this.#calls.then(run, (error: unknown) => {
      this.#calls = null;
      this.#telemetry.error(error, { source: 'tm-attention' });
      return null;
    });
  }

  // --- The panels ---

  /** After a panel opened: the seen, then the panel's one read. */
  async #opened(panel: TmPanel): Promise<void> {
    const partition = this.#partition();
    if (partition === null) {
      return;
    }
    // Closing the panel or leaving the tenant aborts its read.
    const reads = new AbortController();
    this.#reads.set(panel, reads);
    const abort = AbortSignal.any([reads.signal, this.#tenantLife.signal]);
    const copy = panel === 'bell' ? this.#inboxCopy()?.data : this.#assignmentsCopy()?.data;
    const fresh = copy !== undefined && ('unseen' in copy ? copy.unseen : copy.new) > 0;

    // --- Same frame: the seen overlay; then the seen, when the document shows something new ---
    if (fresh && this.writable()) {
      const pending: SeenHold = { kind: 'pending' };
      this.#seen[panel].set(pending);
      const release = await this.#call((calls) => calls.seen(panel, partition.tenantId));
      // A failed seen marks nothing: the badge comes back with the panel's own
      // copy, or at once when the panel closed meanwhile, since it reads none.
      if (this.#seen[panel]() === pending) {
        this.#seen[panel].set(release ?? (abort.aborted ? null : { kind: 'own', sentAt: null }));
      }
    } else if (fresh) {
      this.#seen[panel].set({ kind: 'own', sentAt: null });
    }

    // --- Then the panel's read, unless it closed meanwhile; an overlay waiting on it lifts with the copy it stores ---
    if (!abort.aborted) {
      await this.#call((calls) =>
        calls.read(partition, panel, abort, (sentAt) => {
          if (this.#seen[panel]()?.kind === 'own') {
            this.#seen[panel].set({ kind: 'own', sentAt });
          }
        }),
      );
    }
  }

  /** Closes a panel: its read aborts, and a seen overlay waiting on that read lifts. */
  #close(panel: TmPanel): void {
    this.#open.set(null);
    this.#reads.get(panel)?.abort();
    this.#reads.delete(panel);
    if (this.#seen[panel]()?.kind === 'own') {
      this.#seen[panel].set(null);
    }
  }

  /** Drops everything that belongs to the tenant left. */
  #reset(): void {
    this.#tenantLife.abort();
    this.#tenantLife = new AbortController();
    this.#reads.clear();
    this.#open.set(null);
    this.#overlays.set([]);
    this.#stopping.set(new Set());
    this.#seen.bell.set(null);
    this.#seen.tray.set(null);
  }

  // --- Text ---

  /** An attention string. */
  #text(key: string, params?: Readonly<Record<string, unknown>>): string {
    return this.#l10n.ɵtext(`tm.attention.${key}`, params)();
  }

  /** A badge count as the counter draws it: "99+" at the cap. */
  #count(count: number): string {
    return count >= CAP
      ? this.#translate('counter.overflow', { max: this.#numbers.formatNumber(CAP - 1) })()
      : this.#numbers.formatNumber(count);
  }

  /** An accessible name's parts, joined with the language's separator. */
  #join(parts: readonly (string | null)[]): string {
    return parts.filter((part) => part !== null).join(this.#text('separator'));
  }
}
