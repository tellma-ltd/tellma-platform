// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  DestroyRef,
  effect,
  inject,
  Injectable,
  InjectionToken,
  Injector,
  signal,
  untracked,
  type Signal,
} from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

import { TmApi } from '../../api/tm-api';
import { TmApiError } from '../../api/tm-api-error';
import { TM_API_HOST } from '../../api/tm-api-host';
import { TM_NOW } from '../../app/tm-now';
import { tmRoutedTenant } from '../../launch/tm-routed-tenant';
import { TmClientMetadataStore } from '../../metadata/tm-client-metadata';
import { TmTelemetry } from '../../observe/tm-telemetry';
import { TmSession } from '../../session/tm-session';
import type {
  TmMeLocaleResult,
  TmMeResult,
  TmPreferencesResult,
  TmSyncDoc,
  TmTenantFacts,
  TmTenantSettingsForClient,
} from '../launch/tm-launch-docs';
import { TmClientStore, TmStoreRefusedError, type TmDocUpdate, type TmStoredDoc } from '../store/tm-client-store';
import type { TmTenantPartition } from '../store/tm-store-layout';
import { TmLocks } from './tm-locks';

// The cache refresher (spec 0025 §5.3): the one client path to `client/sync`.
// Response headers, hub events, the cold entry, the leader and the membership
// boundary all go through it, so one browser runs at most one sync per tenant
// at a time: every sync and every write of `settings`, `me`, `tenant` or
// `sync` runs under the tenant's sync lock, and a sync that another tab made
// unnecessary while this one queued ends at the check inside the lock. A
// write's own result is adopted before its response header is compared, so
// the write costs no sync. The documents a call names only label it: the
// server answers by the held tags.

/** What made a sync run; it labels the sync's telemetry. */
export type TmSyncTrigger = 'cold' | 'header' | 'event' | 'leader' | 'poll' | 'boundary' | 'terminal' | 'list';

/** A write's own results, which become server truth without a sync. */
export interface TmAdoptedDocs {
  /** A settings save's client view and its tag: the whole `settings` document. */
  readonly settings?: { readonly data: TmTenantSettingsForClient; readonly tag: string };
  /** A profile save's envelope: the whole `me` document. */
  readonly me?: TmMeResult;
  /** A preferences write's bag and tag. */
  readonly preferences?: TmPreferencesResult;
  /** A language, calendar, time zone, region or digits write's result. */
  readonly locale?: TmMeLocaleResult;
}

/** One `cache.changed` event: the tag that moved, and its new value when the event carries one. */
export interface TmCacheChangedEvent {
  /** The tag's name, such as `settings` or `entity:acme.Currency`. */
  readonly tag: string;
  /** The tag's new wire value, when the event carries it. */
  readonly value?: string;
}

/** Where the refresher hands the refusals that end a tenant's use. Internal. */
export interface TmSyncErrors {
  /**
   * Shows a `tenant-not-found` or `tenant-unavailable` refusal of a sync.
   *
   * @param error - The refusal.
   */
  present(error: TmApiError): void;
}

/**
 * Where the refresher hands a sync's `tenant-not-found` and
 * `tenant-unavailable` refusals, even from a background sync: the app's
 * error presenter. Until one is provided, they are dropped. Internal.
 */
export const TM_SYNC_ERRORS = new InjectionToken<TmSyncErrors>('TM_SYNC_ERRORS', {
  providedIn: 'root',
  factory: (): TmSyncErrors => ({ present: () => undefined }),
});

/** The answer of `client/sync`. */
interface TmSyncResult {
  /** The tenant's facts. */
  readonly tenant: TmTenantFacts;
  /** The current tags: the four client tags, and every list's when the held composite differed. */
  readonly tags: Readonly<Record<string, string>>;
  /** The settings, when the held tag differed. */
  readonly settings?: TmTenantSettingsForClient;
  /** The envelope, when a held caller tag differed. */
  readonly me?: TmMeResult;
  /** The held lists whose tags moved, by entity name. */
  readonly staleEntities: readonly string[];
}

/** One pending sync of a tenant: what would make it unnecessary, and its outcome. */
interface TmRun {
  /** The run's trigger: `cold` once any of its calls is, else its first call's. */
  trigger: TmSyncTrigger;
  /**
   * The held tags the call leaves out: `permissions` for an `access`
   * refresh, so the envelope is decided afresh, and `entities` for an
   * `entities` refresh, so every list's tag is reported.
   */
  readonly omit: Set<string>;
  /** Each call's test that a sync is no longer needed; the run ends at the check when all pass. */
  readonly needless: (() => boolean)[];
  /** Settles once the run has synced, or ended without a sync. */
  done: Promise<void>;
}

/** The membership boundary a tab waits for: the routed tenant's `validUntil`. */
interface TmBoundary {
  /** The tenant. */
  readonly t: number;
  /** The boundary, in epoch milliseconds. */
  readonly until: number;
  /** When the timer runs next; `Infinity` while its refresh runs. */
  due: number;
  /** The next delay when the boundary stays unchanged. */
  delay: number;
}

/** The four client tags: `settings` covers the settings, `preferences` and `permissions` the envelope, `entities` the lists. */
const CLIENT_TAGS = ['settings', 'preferences', 'permissions', 'entities'] as const;

/** The prefix of a list's tag and document. */
const LIST = 'entity:';

/** The tenant documents a cold entry needs. */
const LAUNCH = ['settings', 'me', 'tenant'] as const;

/** The shortest time between two syncs of a tenant in one browser. */
const THROTTLE_MS = 2_000;

/** The first delay of an unchanged boundary, doubled up to {@link BACKOFF_MAX_MS}. */
const BACKOFF_MS = 30_000;

/** The longest delay of an unchanged boundary. */
const BACKOFF_MAX_MS = 600_000;

/** The longest delay a timer takes; a later boundary is re-armed when it fires. */
const MAX_TIMER_MS = 2_000_000_000;

/**
 * The cache refresher: compares the version tags the server reports with the
 * ones the tab holds, syncs the tenant's cached documents when they differ,
 * and stores a write's own results as server truth.
 *
 * @example
 * ```ts
 * const refresher = inject(TmCacheRefresher);
 * const result = await api.write<TmPreferencesResult>('users/me/preferences/set', { values });
 * refresher.adopt(17, { preferences: result });
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmCacheRefresher {
  readonly #injector = inject(Injector);
  readonly #api = inject(TmApi);
  readonly #store = inject(TmClientStore);
  readonly #locks = inject(TmLocks);
  readonly #host = inject(TM_API_HOST);
  readonly #now = inject(TM_NOW);
  readonly #telemetry = inject(TmTelemetry);
  readonly #errors = inject(TM_SYNC_ERRORS);

  /** Each tenant's run that has not sent its call yet, which a new call joins. */
  readonly #pending = new Map<number, TmRun>();
  /** Each tenant's signal, aborted when the tab leaves the tenant or the page. */
  readonly #aborts = new Map<number, AbortController>();
  /** The jitter timers of hub events. */
  readonly #timers = new Set<ReturnType<typeof setTimeout>>();
  /** The routed tenant. */
  readonly #tenant = signal<number | null>(null);
  #boundary: TmBoundary | null = null;
  #timer: ReturnType<typeof setTimeout> | undefined;

  /** The routed tenant's partition for the signed-in user, or `null`. Internal. */
  readonly ɵpartition: Signal<TmTenantPartition | null> = computed(() => {
    const tenantId = this.#tenant();
    const sub = this.#session.profile()?.sub;
    return tenantId === null || sub === undefined ? null : { kind: 'tenant', sub, tenantId };
  });

  /** When the routed tenant last synced, in any tab, in epoch milliseconds; `null` before its first sync. */
  readonly lastSyncAt: Signal<number | null> = computed(() => {
    const partition = this.ɵpartition();
    return (partition && this.#store.doc<TmSyncDoc>(partition, 'sync')()?.data.at) ?? null;
  });

  /** Creates the refresher, which follows the routed tenant and the page's life. */
  constructor() {
    // --- The routed tenant: leaving a tenant aborts its syncs and list loads ---
    // An aborted run is no longer joinable: a call after the abort starts a new one.
    const router = inject(Router);
    const route = (url: string): void => {
      const tenantId = tmRoutedTenant(url);
      this.#tenant.set(tenantId);
      for (const [other, controller] of this.#aborts) {
        if (other !== tenantId) {
          controller.abort();
          this.#aborts.delete(other);
          this.#pending.delete(other);
        }
      }
    };
    const subscription = router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        route(event.urlAfterRedirects);
      }
    });
    if (router.navigated) {
      route(router.url);
    }

    // --- The page: leaving it aborts every sync; showing it again runs a boundary that passed ---
    const stops = [
      this.#host.listen('window', 'pagehide', () => {
        this.#aborts.forEach((controller) => controller.abort());
        this.#aborts.clear();
        this.#pending.clear();
      }),
      this.#host.listen('document', 'visibilitychange', () => this.#arm()),
    ];

    // --- The membership boundary: each new envelope reschedules it ---
    effect(() => {
      const partition = this.ɵpartition();
      const until = partition && this.#store.doc<TmMeResult>(partition, 'me')()?.data.access.validUntil;
      untracked(() => {
        const at = Date.parse(until ?? '');
        const boundary = this.#boundary;
        if (partition === null || Number.isNaN(at)) {
          this.#boundary = null;
        } else if (boundary === null || boundary.t !== partition.tenantId || boundary.until !== at) {
          // A new boundary, with a 0-2 s jitter, so a browser's tabs and the
          // tenant's browsers do not sync in the same instant.
          this.#boundary = { t: partition.tenantId, until: at, due: at + Math.random() * 2_000, delay: BACKOFF_MS };
        }
        this.#arm();
      });
    });

    inject(DestroyRef).onDestroy(() => {
      subscription.unsubscribe();
      stops.forEach((stop) => stop());
      clearTimeout(this.#timer);
      this.#timers.forEach((timer) => clearTimeout(timer));
    });
  }

  /**
   * Compares the version tags a tenant reported with the held ones. Each of
   * `settings`, `preferences`, `permissions` and `entities` that differs
   * from the held tag starts a sync; an absent tag says nothing. A list's
   * tag, which only hub events carry, marks the held list stale.
   *
   * @param tenantId - The tenant.
   * @param tags - The reported tags by name.
   * @param trigger - What reported them: a response header or a hub event.
   */
  observe(tenantId: number, tags: Readonly<Record<string, string>>, trigger: 'header' | 'event'): void {
    const partition = this.#partitionOf(tenantId);
    if (partition === null) {
      return;
    }
    // A list's new tag only replaces the tag the list is current under (spec 0025 §4.4).
    const lists = Object.entries(tags).filter(([name]) => name.startsWith(LIST));
    if (lists.length > 0) {
      this.#locked(partition, () =>
        this.#store.ɵwrite(partition, {
          sync: (current) => {
            const sync = current?.data as TmSyncDoc | undefined;
            const entityTags = { ...sync?.entityTags };
            for (const [name, value] of lists) {
              entityTags[name.slice(LIST.length)] = value;
            }
            return sync && this.#doc({ ...sync, entityTags });
          },
        }),
      );
    }
    const wanted = this.#wanted(partition, tags);
    if (wanted.length > 0) {
      const reported = Object.fromEntries(wanted);
      void this.#join(partition, trigger, () => this.#wanted(partition, reported).length === 0);
    }
  }

  /**
   * Syncs a tenant's cached documents, unless a sync made it unnecessary
   * meanwhile. A call while another is pending joins it. `access` re-decides
   * the caller's grants at the server's clock, and ends early while the held
   * envelope's boundary is still ahead. `entities` asks for every cacheable
   * list's current tag; so does a list's name, which ends early once the
   * list's tag is known. A `cold` call ends early once the tenant's
   * `settings`, `me` and `tenant` are all present, and any other once a sync
   * was sent after it.
   *
   * @param tenantId - The tenant.
   * @param docs - What the call is for, such as `['me']`, `['access']` or
   *   `['entity:acme.Currency']`; the server answers by the held tags.
   * @param trigger - Why it syncs.
   * @returns A promise that settles once the sync is stored, or once none was needed. It
   *   rejects only for `cold`, with the sync's error; every other failure is reported and
   *   left to the next trigger.
   */
  refresh(tenantId: number, docs: readonly string[], trigger: TmSyncTrigger): Promise<void> {
    const partition = this.#partitionOf(tenantId);
    if (partition === null) {
      return Promise.resolve();
    }
    const requested = this.#now();
    const access = docs.includes('access');
    const lists = docs.filter((name) => name.startsWith(LIST)).map((name) => name.slice(LIST.length));
    // What makes the call needless, tested inside the lock. A cold call
    // tests the documents themselves, since a sync another tab sent may hold
    // tags this tab's build cannot read (spec 0025 §2.4); a list's call
    // tests its tag, which a sync already in flight reports.
    const needless = access
      ? () => this.#current(partition)
      : trigger === 'cold'
        ? () => LAUNCH.every((name) => this.#read(partition, name) !== undefined)
        : () =>
            this.#sentSince(partition, requested) ||
            (lists.length > 0 && lists.every((name) => this.#syncDoc(partition)?.entityTags[name] !== undefined));
    return this.#join(
      partition,
      trigger,
      needless,
      access ? 'permissions' : lists.length > 0 || docs.includes('entities') ? 'entities' : undefined,
    );
  }

  /**
   * Stores a write's own results as server truth: this tab's documents show
   * them at once, so the write's response header finds its tags held, and
   * the store writes them under the tenant's sync lock.
   *
   * @param tenantId - The tenant.
   * @param docs - The write's results.
   */
  adopt(tenantId: number, docs: TmAdoptedDocs): void {
    const partition = this.#partitionOf(tenantId);
    if (partition === null) {
      return;
    }
    const { settings, me, preferences, locale } = docs;
    const updates: Record<string, TmDocUpdate> = {};
    if (settings) {
      updates['settings'] = () => this.#doc(settings.data, settings.tag);
    }
    if (me || preferences || locale) {
      // The whole envelope, or the members a preferences or locale write changed.
      updates['me'] = (current) => {
        let next = me ?? (current?.data as TmMeResult | undefined);
        if (next === undefined) {
          return undefined;
        }
        if (preferences) {
          next = { ...next, preferences: preferences.preferences };
        }
        if (locale) {
          const user = {
            ...next.user,
            preferredLanguage: locale.preferredLanguage,
            preferredCalendar: locale.preferredCalendar,
            preferredTimeZone: locale.preferredTimeZone,
            preferredRegion: locale.preferredRegion,
            preferredDigits: locale.preferredDigits,
            modifiedAt: locale.modifiedAt,
          };
          next = { ...next, user };
        }
        const tag = (locale ?? preferences)?.preferencesTag;
        if (tag !== undefined) {
          next = { ...next, preferencesTag: tag, tags: { ...next.tags, preferences: tag } };
        }
        return this.#doc(next);
      };
    }
    let release: () => void;
    try {
      release = this.#store.ɵshow(partition, updates);
    } catch (error) {
      this.#failed(error);
      return;
    }
    // An out-of-order adoption may store an older bag; the next header repairs it.
    this.#locked(partition, () => this.#store.ɵwrite(partition, updates).finally(release));
  }

  /**
   * Handles a tenant's `cache.changed` hub event: a tenant-wide tag after a
   * jitter, so the tenant's browsers do not sync at once, and the caller's
   * own tags, and a list's, at once. An unknown tag is ignored. Internal:
   * the leader tab calls it.
   *
   * @param tenantId - The tenant.
   * @param event - The event's payload.
   */
  ɵcacheChanged(tenantId: number, event: TmCacheChangedEvent): void {
    const { tag, value } = event;
    if (value === undefined) {
      const docs = tag === 'tenant' ? ['tenant'] : tag === 'permissions' || tag === 'preferences' ? ['me'] : null;
      if (docs !== null) {
        this.#later(10_000, () => void this.refresh(tenantId, docs, 'event'));
      }
    } else if (tag === 'settings') {
      this.#later(3_000, () => this.observe(tenantId, { settings: value }, 'event'));
    } else if (tag === 'permissions' || tag === 'preferences' || tag.startsWith(LIST)) {
      this.observe(tenantId, { [tag]: value }, 'event');
    }
  }

  /**
   * The signal a tenant's background reads abort with: when the tab leaves
   * the tenant or the page. Internal: the list loads share it.
   *
   * @param tenantId - The tenant.
   * @returns The signal.
   */
  ɵsignal(tenantId: number): AbortSignal {
    let controller = this.#aborts.get(tenantId);
    if (controller === undefined) {
      controller = new AbortController();
      this.#aborts.set(tenantId, controller);
    }
    return controller.signal;
  }

  /** The tab's session, resolved at use: the session's own services reach the store. */
  get #session(): TmSession {
    return this.#injector.get(TmSession);
  }

  /** The signed-in user's partition of a tenant, or `null` with no user. */
  #partitionOf(tenantId: number): TmTenantPartition | null {
    const sub = untracked(this.#session.profile)?.sub;
    return sub === undefined ? null : { kind: 'tenant', sub, tenantId };
  }

  /** A document stored now. */
  #doc<T>(data: T, tag: string | null = null): TmStoredDoc<T> {
    return { tag, savedAt: this.#now(), data };
  }

  /** A mirrored document now, untracked. */
  #read<T>(partition: TmTenantPartition, name: string): TmStoredDoc<T> | undefined {
    return untracked(this.#store.doc<T>(partition, name));
  }

  /** The wire tags the tab holds for a tenant, by name. */
  #held(partition: TmTenantPartition): Record<string, string> {
    // Spec 0025 §4.2: the settings tag, the envelope's two, the lists' composite and each list's.
    const held: Record<string, string> = {};
    const hold = (name: string, tag: string | null | undefined): void => {
      if (tag) {
        held[name] = tag;
      }
    };
    hold('settings', this.#read(partition, 'settings')?.tag);
    const me = this.#read<TmMeResult>(partition, 'me')?.data;
    hold('preferences', me?.preferencesTag);
    hold('permissions', me?.access.tag);
    hold('entities', this.#syncDoc(partition)?.entities);
    for (const name of this.#store.ɵnames(partition, LIST)) {
      hold(name, this.#read(partition, name)?.tag);
    }
    return held;
  }

  /** The reported client tags that differ from the held ones. */
  #wanted(partition: TmTenantPartition, tags: Readonly<Record<string, string>>): [string, string][] {
    const held = this.#held(partition);
    return CLIENT_TAGS.filter((name) => tags[name] !== undefined && tags[name] !== held[name])
      .map((name) => [name, tags[name]!]);
  }

  /** The tenant's mirrored `sync` document's value. */
  #syncDoc(partition: TmTenantPartition): TmSyncDoc | undefined {
    return this.#read<TmSyncDoc>(partition, 'sync')?.data;
  }

  /** When the tenant last synced, by this browser's clock, in epoch milliseconds; `-Infinity` before its first. */
  #syncAt(partition: TmTenantPartition): number {
    return this.#syncDoc(partition)?.at ?? -Infinity;
  }

  /**
   * Whether a sync was sent after a time. A stamp ahead of the clock, left
   * by a clock that was set back since, says nothing.
   */
  #sentSince(partition: TmTenantPartition, time: number): boolean {
    const at = this.#syncAt(partition);
    return time < at && at <= this.#now();
  }

  /** Whether the held envelope's grants still hold: it has no boundary, or the boundary is ahead. */
  #current(partition: TmTenantPartition): boolean {
    const access = this.#read<TmMeResult>(partition, 'me')?.data.access;
    return access !== undefined && !(Date.parse(access.validUntil ?? '') <= this.#now());
  }

  /** The name of a tenant's sync lock: one per distribution, user and tenant. */
  #lock(partition: TmTenantPartition): string {
    const slug = this.#injector.get(TmClientMetadataStore).peek()?.distribution.slug ?? '';
    return `tm:${slug}:${partition.sub}:${partition.tenantId}:sync`;
  }

  /** Runs a write under the tenant's sync lock; a failure is reported. */
  #locked(partition: TmTenantPartition, work: () => Promise<void>): void {
    this.#locks.run(this.#lock(partition), work).catch((error: unknown) => this.#failed(error));
  }

  /** Runs work after a random delay of up to `spread` milliseconds. */
  #later(spread: number, work: () => void): void {
    const timer = setTimeout(() => {
      this.#timers.delete(timer);
      work();
    }, Math.random() * spread);
    this.#timers.add(timer);
  }

  /**
   * Joins the tenant's pending run, or starts one: the run is `cold` when
   * any call is, and otherwise keeps its first call's trigger.
   */
  #join(partition: TmTenantPartition, trigger: TmSyncTrigger, needless: () => boolean, omit?: string): Promise<void> {
    const tenantId = partition.tenantId;
    let run = this.#pending.get(tenantId);
    if (run === undefined) {
      run = { trigger, omit: new Set(), needless: [], done: Promise.resolve() };
      this.#pending.set(tenantId, run);
      run.done = this.#run(partition, run);
    } else if (trigger === 'cold') {
      run.trigger = 'cold';
    }
    run.needless.push(needless);
    if (omit !== undefined) {
      run.omit.add(omit);
    }
    return trigger === 'cold' ? run.done : run.done.catch(() => undefined);
  }

  /** Runs one pending run of a tenant: the lock, the check, the throttle, the sync and the failure routing. */
  async #run(partition: TmTenantPartition, run: TmRun): Promise<void> {
    // Spec 0025 §5.3, steps 2 to 7; `#join` is step 1.
    const tenantId = partition.tenantId;
    const signal = this.ɵsignal(tenantId);
    // No longer joinable: a later call starts the next run.
    const close = (): void => {
      if (this.#pending.get(tenantId) === run) {
        this.#pending.delete(tenantId);
      }
    };
    try {
      // --- Step 2: queue on the lock, never `ifAvailable` ---
      await this.#locks.run(this.#lock(partition), async () => {
        // --- Step 3: another tab may have synced while this one queued ---
        // The re-read covers every document a needless test reads. A tab
        // that left the tenant while queuing, or during the re-read, ends
        // here, so the throttle never waits for an aborted run.
        await this.#store.ɵreread(partition, ['settings', 'me', 'tenant', 'sync']);
        if (signal.aborted) {
          throw new TmApiError({ kind: 'cancelled' });
        }
        if (run.needless.every((needless) => needless())) {
          close();
          return;
        }
        // --- Step 4: at most one sync every 2 s; calls arriving meanwhile join this run ---
        // A stamp ahead of the clock waits 2 s at most, so a clock set back
        // never holds the lock for longer.
        const wait = Math.min(THROTTLE_MS, this.#syncAt(partition) + THROTTLE_MS - this.#now());
        if (wait > 0) {
          await new Promise<void>((resolve, reject) => {
            const cancel = (): void => {
              clearTimeout(timer);
              reject(new TmApiError({ kind: 'cancelled' }));
            };
            const timer = setTimeout(() => {
              signal.removeEventListener('abort', cancel);
              resolve();
            }, wait);
            signal.addEventListener('abort', cancel, { once: true });
          });
        }
        close();
        await this.#sync(partition, run, signal);
      });
    } catch (error) {
      close();
      // --- Step 7: the refusals that end the tenant's use are presented ---
      this.#failed(error);
      if (run.trigger === 'cold') {
        throw error;
      }
    }
  }

  /** Sends the sync and stores its answer in one transaction. */
  async #sync(partition: TmTenantPartition, run: TmRun, signal: AbortSignal): Promise<void> {
    // Spec 0025 §5.3, steps 5 and 6.
    // An access refresh holds no permissions tag, so the answer carries the
    // envelope, decided at the server's clock; an entities refresh holds no
    // composite, so the answer carries every list's tag.
    const before = this.#held(partition);
    const held = { ...before };
    for (const name of run.omit) {
      delete held[name];
    }
    const at = this.#now();
    const started = performance.now();
    const result = await this.#api.read<TmSyncResult>(
      'client/sync',
      { held },
      // Only a cold entry goes in the foreground, which renews the session.
      // The server never stamps activity for client/sync, a polling route
      // (spec 0016 §4.3), although spec 0025 §5.3 step 5 says a cold one does.
      { signal, background: run.trigger !== 'cold', tenantId: partition.tenantId },
    );
    const { tenant, tags, settings, me, staleEntities } = result;
    const lists = Object.keys(tags).filter((name) => name.startsWith(LIST));
    const updates: Record<string, TmDocUpdate> = {
      tenant: () => this.#doc(tenant),
      sync: (current) => {
        let entities: string | null = tags['entities'] ?? null;
        // Every list's tag when the composite moved, else the held ones.
        const entityTags: Record<string, string> =
          lists.length > 0 ? {} : { ...(current?.data as TmSyncDoc | undefined)?.entityTags };
        for (const name of lists) {
          entityTags[name.slice(LIST.length)] = tags[name]!;
        }
        // A list counts as stale while its tag differs from its current one.
        // A list the server calls stale while the tags here say otherwise
        // loses its tag, and the composite goes, so the next sync reports
        // every list's tag.
        for (const name of staleEntities) {
          const current = entityTags[name];
          if (current === undefined || current === held[LIST + name]) {
            delete entityTags[name];
            entities = null;
          }
        }
        return this.#doc<TmSyncDoc>({ at, entities, entityTags });
      },
    };
    if (settings) {
      updates['settings'] = () => this.#doc(settings, tags['settings']);
    }
    if (me) {
      updates['me'] = () => this.#doc(me);
    }
    await this.#store.ɵwrite(partition, updates);

    // --- Telemetry: what the sync changed ---
    const changed = [
      settings && 'settings',
      me && 'me',
      (staleEntities.length > 0 || before['entities'] !== tags['entities']) && 'entities',
    ].filter((kind) => kind) as string[];
    this.#telemetry.timing('tm.sync.completed', performance.now() - started, {
      trigger: run.trigger,
      changed: changed.length > 1 ? 'several' : (changed[0] ?? 'none'),
    });
  }

  /**
   * Routes a failure: a tenant's `tenant-not-found` and `tenant-unavailable`
   * go to the error presenter; the HTTP layer already handles a session's
   * end and an outdated client, and a cancel is the tab's own; anything else
   * is reported, and the next trigger retries.
   */
  #failed(error: unknown): void {
    if (error instanceof TmApiError) {
      const kind = error.kind;
      if (kind === 'tenant-not-found' || kind === 'tenant-unavailable') {
        this.#errors.present(error);
      } else if (kind !== 'cancelled' && kind !== 'unauthenticated' && kind !== 'client-outdated') {
        this.#telemetry.event('tm.sync.failed', { kind });
      }
    } else if (!(error instanceof TmStoreRefusedError)) {
      // A refused write was reported by the store.
      this.#telemetry.error(error, { source: 'tm-cache-refresher' });
    }
  }

  /** Schedules the boundary refresh while the page is visible, or runs it at once when it is due. */
  #arm(): void {
    clearTimeout(this.#timer);
    const boundary = this.#boundary;
    if (boundary !== null && boundary.due < Infinity && this.#host.visible()) {
      this.#timer = setTimeout(
        () => (this.#now() < boundary.due ? this.#arm() : this.#fire(boundary)),
        Math.min(boundary.due - this.#now(), MAX_TIMER_MS),
      );
    }
  }

  /** Re-decides the grants at a boundary; an unchanged boundary then backs off. */
  #fire(boundary: TmBoundary): void {
    if (this.#boundary !== boundary) {
      return;
    }
    boundary.due = Infinity;
    void this.refresh(boundary.t, ['access'], 'boundary').finally(() => {
      // Still the same boundary: the server has not reached it, so the
      // device clock runs ahead. Re-arm at 30 s, doubling to 10 min.
      if (this.#boundary === boundary) {
        boundary.due = this.#now() + boundary.delay;
        boundary.delay = Math.min(boundary.delay * 2, BACKOFF_MAX_MS);
        this.#arm();
      }
    });
  }
}
