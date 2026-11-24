// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  TmFakeApi,
  TmFakeLocks,
  TmFakeSession,
  TmFakeTelemetry,
  type TmFakeApiCall,
  type TmFakeApiResponder,
} from '@tellma/core-testing';

import { TmApiError } from '../../api/tm-api-error';
import { TM_API_HOST } from '../../api/tm-api-host';
import { TmFakeApiHost } from '../../api/tm-api.testing.util';
import { TM_NOW } from '../../app/tm-now';
import { tmUnhandled } from '../../routes/tm-app.testing.util';
import { TmTabBus } from '../bus/tm-tab-bus';
import { TmRecordingTabBus } from '../bus/tm-tab-bus.testing.util';
import type {
  TmMeResult,
  TmSyncDoc,
  TmTenantFacts,
  TmTenantSettingsForClient,
} from '../launch/tm-launch-docs';
import { TmClientStore, type TmStoredDoc } from '../store/tm-client-store';
import { TM_STORE_HOST } from '../store/tm-store-host';
import type { TmTenantPartition } from '../store/tm-store-layout';
import { tmDeleteStoreDatabase, tmMemoryStoreHost } from '../store/tm-store.testing.util';
import { TM_SYNC_ERRORS, TmCacheRefresher, type TmSyncErrors } from './tm-cache-refresher';

// The refresher over an in-memory store, a scripted API, in-memory locks, a
// faked page and faked timers; and two tabs' refreshers over the browser's
// real IndexedDB, each with its own store, on real timers.

/** A page for every route. */
@Component({ template: '' })
class TmPage {}

const NOW = 1_700_000_000_000;
const SUB = 'test-user';
const T17: TmTenantPartition = { kind: 'tenant', sub: SUB, tenantId: 17 };
const LOCK = `tm:acme:${SUB}:17:sync`;

const SETTINGS: TmTenantSettingsForClient = {
  tenantId: 17,
  names: ['Acme', null],
  languages: [{ code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false }],
  calendars: ['gc'],
  timeZone: 'Asia/Riyadh',
  entries: {},
};
const TENANT: TmTenantFacts = { id: 17, category: 'Live', state: 'Active', realtime: true };

/** An envelope under the given tags. */
function me(patch: { preferencesTag?: string; permissions?: string; validUntil?: string; bag?: Record<string, string> } = {}): TmMeResult {
  const preferencesTag = patch.preferencesTag ?? 'P1';
  const permissions = patch.permissions ?? 'A1';
  return {
    user: {
      id: 42,
      kind: 'Individual',
      state: 'Joined',
      name: 'Farah',
      preferredLanguage: 'ar',
      preferredCalendar: 'uq',
      modifiedAt: '2026-11-01T00:00:00.0000000+00:00',
    },
    preferencesTag,
    preferences: patch.bag ?? { 'nav.pinned': '[]' },
    access: {
      tag: permissions,
      formatVersion: 1,
      isSystem: false,
      securables: [],
      problems: [],
      ...(patch.validUntil === undefined ? {} : { validUntil: patch.validUntil }),
    },
    tags: { settings: 'S1', preferences: preferencesTag, permissions, entities: 'E1' },
  };
}

/** The answer of `client/sync`: the held tags unless patched, and the given members. */
function answer(
  patch: {
    tags?: Record<string, string>;
    settings?: TmTenantSettingsForClient;
    me?: TmMeResult;
    staleEntities?: string[];
    tenant?: TmTenantFacts;
  } = {},
): unknown {
  return {
    tenant: patch.tenant ?? TENANT,
    tags: { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1', ...patch.tags },
    ...(patch.settings && { settings: patch.settings }),
    ...(patch.me && { me: patch.me }),
    staleEntities: patch.staleEntities ?? [],
  };
}

/** A promise the spec settles. */
interface TmGate<T> {
  readonly promise: Promise<T>;
  resolve(value: T): void;
  reject(error: unknown): void;
}

/** A promise the spec settles. */
function gate<T = unknown>(): TmGate<T> {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}

/** What a spec drives and inspects. */
interface TmRig {
  readonly refresher: TmCacheRefresher;
  readonly store: TmClientStore;
  readonly api: TmFakeApi;
  readonly locks: TmFakeLocks;
  readonly host: TmFakeApiHost;
  readonly telemetry: TmFakeTelemetry;
  /** The refusals handed to the error presenter, each with the tenant the sync was for. */
  readonly presented: (readonly [TmApiError, number])[];
  /** The `client/sync` calls so far. */
  syncs(): TmFakeApiCall[];
  /** The routed tenant's stored document. */
  doc<T>(name: string): TmStoredDoc<T> | undefined;
  /** A second refresher of the same store and locks, as another tab's. */
  another(): TmCacheRefresher;
}

/** Runs the pending microtasks and the timers due now. */
async function flush(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await vi.advanceTimersByTimeAsync(0);
  }
  TestBed.tick();
  await vi.advanceTimersByTimeAsync(0);
}

/**
 * The whole seconds since {@link NOW}. A timer armed for a boundary that has
 * passed fires 1 ms late when the fake clock is mid-tick as it is armed, and
 * whether it is depends on when Angular runs the effect that arms it.
 */
function seconds(): number {
  return Math.round((Date.now() - NOW) / 1_000);
}

/** How {@link setUp} starts. */
interface TmSetUp {
  /** Store no documents of tenant 17. */
  readonly cold?: boolean;
  /** The stored envelope, instead of {@link me}'s. */
  readonly me?: TmMeResult;
  /** When the stored `sync` document says tenant 17 last synced. */
  readonly at?: number;
  /** Start with the page hidden. */
  readonly hidden?: boolean;
  /** How `client/sync` answers from the start, before the refresher exists. */
  readonly respond?: TmFakeApiResponder;
}

/** Configures the refresher on `/17/centers`, with tenant 17's documents stored unless `cold`. */
async function setUp(options: TmSetUp = {}): Promise<TmRig> {
  const host = new TmFakeApiHost();
  host.visibleNow = options.hidden !== true;
  const presented: (readonly [TmApiError, number])[] = [];
  const errors: TmSyncErrors = { present: (error, tenantId) => void presented.push([error, tenantId]) };
  TestBed.configureTestingModule({
    providers: [
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      provideRouter([{ path: '**', component: TmPage }]),
      { provide: TM_API_HOST, useValue: host },
      { provide: TM_NOW, useValue: () => Date.now() },
      { provide: TM_SYNC_ERRORS, useValue: errors },
    ],
  });
  await TestBed.inject(Router).navigateByUrl('/17/centers');
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  vi.setSystemTime(NOW);
  const store = TestBed.inject(TmClientStore);
  if (options.cold !== true) {
    const stored = <T>(data: T, tag: string | null = null): TmStoredDoc<T> => ({ tag, savedAt: NOW - 60_000, data });
    await store.ɵwrite(T17, {
      settings: () => stored(SETTINGS, 'S1'),
      me: () => stored(options.me ?? me()),
      tenant: () => stored(TENANT),
      sync: () => stored<TmSyncDoc>({ at: options.at ?? NOW - 60_000, entities: 'E1', entityTags: { 'acme.Currency': 'C1' } }),
      'entity:acme.Currency': () => stored([{ id: 1, code: 'USD' }], 'C1'),
    });
  }
  const api = TestBed.inject(TmFakeApi);
  if (options.respond) {
    api.respond('client/sync', options.respond);
  }
  const refresher = TestBed.inject(TmCacheRefresher);
  await flush();
  return {
    refresher,
    store,
    api,
    locks: TestBed.inject(TmFakeLocks),
    host,
    telemetry: TestBed.inject(TmFakeTelemetry),
    presented,
    syncs: () => api.calls().filter((call) => call.route === 'client/sync'),
    doc: <T>(name: string) => store.doc<T>(T17, name)(),
    another: () => createEnvironmentInjector([TmCacheRefresher], TestBed.inject(EnvironmentInjector)).get(TmCacheRefresher),
  };
}

/** The tags tenant 17 holds when stored by {@link setUp}. */
const HELD = { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1', 'entity:acme.Currency': 'C1' };

/** {@link HELD} without the permissions tag, as an access refresh holds them. */
const HELD_BUT_PERMISSIONS = { settings: 'S1', preferences: 'P1', entities: 'E1', 'entity:acme.Currency': 'C1' };

/** {@link HELD} without the composite, as an entities refresh holds them. */
const HELD_BUT_ENTITIES = { settings: 'S1', preferences: 'P1', permissions: 'A1', 'entity:acme.Currency': 'C1' };

/** The telemetry items of one name. */
function items(rig: TmRig, name: string): unknown[] {
  return rig.telemetry
    .recent()
    .filter((item) => item.name === name)
    .map((item) => item.props);
}

describe('TmCacheRefresher', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  describe('observing reported tags', () => {
    it('syncs nothing for a warm header whose tags are all held', async () => {
      const rig = await setUp();
      rig.refresher.observe(17, { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1' }, 'header');
      await flush();
      expect(rig.syncs()).toEqual([]);
    });

    it('syncs once with every held tag for a tag that differs, and stores only what changed', async () => {
      const rig = await setUp();
      const changed = me({ preferencesTag: 'P2', bag: { 'nav.pinned': '[1]' } });
      rig.api.respond('client/sync', answer({ tags: { preferences: 'P2' }, me: changed }));
      rig.refresher.observe(17, { preferences: 'P2', settings: 'S1' }, 'header');
      await flush();

      expect(rig.syncs()).toEqual([
        {
          kind: 'read',
          route: 'client/sync',
          body: { held: HELD },
          options: expect.objectContaining({ background: true, tenantId: 17 }) as unknown,
        },
      ]);
      expect(rig.doc('me')?.data).toEqual(changed);
      expect(rig.doc('settings')?.savedAt).toBe(NOW - 60_000);
      expect(rig.doc<TmSyncDoc>('sync')?.data).toEqual({ at: NOW, entities: 'E1', entityTags: { 'acme.Currency': 'C1' } });
      expect(rig.refresher.lastSyncAt()).toBe(NOW);
      expect(items(rig, 'tm.sync.completed')).toEqual([expect.objectContaining({ trigger: 'header', changed: 'me' })]);
    });

    it("never syncs for an own write's header: the write's result is adopted first", async () => {
      const rig = await setUp();
      rig.api.respond('users/me/preferences/set', { preferences: { 'nav.pinned': '[2]' }, preferencesTag: 'P2' });
      // As the HTTP layer orders them: the caller gets the body, and the
      // header is observed one task later.
      void rig.api.write<{ preferences: Record<string, string>; preferencesTag: string }>('users/me/preferences/set', {}).then((result) => {
        rig.refresher.adopt(17, { preferences: result });
      });
      setTimeout(() => rig.refresher.observe(17, { preferences: 'P2' }, 'header'), 0);
      await flush();

      expect(rig.syncs()).toEqual([]);
      expect(rig.doc<TmMeResult>('me')?.data.preferencesTag).toBe('P2');
    });

    it('marks a held list stale from an event carrying its tag, with no sync', async () => {
      const rig = await setUp();
      rig.refresher.observe(17, { 'entity:acme.Currency': 'C2' }, 'event');
      await flush();
      expect(rig.syncs()).toEqual([]);
      expect(rig.doc<TmSyncDoc>('sync')?.data.entityTags).toEqual({ 'acme.Currency': 'C2' });
    });

    it('ignores a tenant with no signed-in user', async () => {
      const rig = await setUp();
      TestBed.inject(TmFakeSession).setProfile(null);
      rig.refresher.observe(17, { preferences: 'P2' }, 'header');
      await flush();
      expect(rig.syncs()).toEqual([]);
    });
  });

  describe('the sync lock', () => {
    it('serializes two tabs, and the second ends at the check when the first synced what it wanted', async () => {
      const rig = await setUp();
      const first = gate();
      rig.api.respond('client/sync', () => first.promise);
      const other = rig.another();
      rig.refresher.observe(17, { preferences: 'P2' }, 'header');
      await flush();
      other.observe(17, { preferences: 'P2' }, 'header');
      await flush();

      // The second tab queues behind the first's lock.
      expect(rig.syncs()).toHaveLength(1);
      expect(rig.locks.held()).toEqual([LOCK]);
      first.resolve(answer({ tags: { preferences: 'P2' }, me: me({ preferencesTag: 'P2' }) }));
      await flush();

      expect(rig.syncs()).toHaveLength(1);
      expect(rig.locks.held()).toEqual([]);
    });

    it("runs the second tab's sync after the first is stored, at least 2 s after it", async () => {
      const rig = await setUp();
      const other = rig.another();
      rig.api.respond('client/sync', answer({ tags: { preferences: 'P2' }, me: me({ preferencesTag: 'P2' }) }));
      rig.refresher.observe(17, { preferences: 'P2' }, 'header');
      other.observe(17, { permissions: 'A2' }, 'header');
      await flush();
      expect(rig.syncs()).toHaveLength(1);

      rig.api.respond('client/sync', answer({ tags: { preferences: 'P2', permissions: 'A2' }, me: me({ preferencesTag: 'P2', permissions: 'A2' }) }));
      await vi.advanceTimersByTimeAsync(1_999);
      expect(rig.syncs()).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(1);
      await flush();

      expect(rig.syncs().map((call) => (call.body as { held: Record<string, string> }).held['preferences'])).toEqual(['P1', 'P2']);
      expect(rig.doc<TmMeResult>('me')?.data.access.tag).toBe('A2');
    });

    it('ends at the check a forced refresh that a later-sent sync answered', async () => {
      const rig = await setUp();
      const other = rig.another();
      rig.api.respond('client/sync', answer());
      // Tab A's sync was sent 1 s ago, so its next one waits out the throttle.
      await rig.refresher.refresh(17, ['tenant'], 'leader');
      await vi.advanceTimersByTimeAsync(1_000);
      const second = rig.refresher.refresh(17, ['me'], 'event');
      await flush();
      // Tab B asks while A waits inside the lock; A's sync is sent after B asked.
      const third = other.refresh(17, ['tenant'], 'leader');
      await vi.advanceTimersByTimeAsync(1_000);
      await flush();
      await Promise.all([second, third]);

      expect(rig.syncs()).toHaveLength(2);
    });

    it('syncs a cold call that a later-sent sync left without the document it lacks', async () => {
      // As two builds side by side: the other tab holds its own copy of the
      // settings, so its sync brings none.
      const rig = await setUp({ at: NOW - 500 });
      await rig.store.patch(T17, 'settings', () => undefined);
      const other = rig.another();
      rig.api.respond('client/sync', answer());
      void other.refresh(17, ['tenant'], 'leader');
      await flush();
      // The other tab waits out the throttle inside the lock; its sync is sent after this call.
      const cold = rig.refresher.refresh(17, ['settings', 'me', 'tenant'], 'cold');
      await vi.advanceTimersByTimeAsync(1_500);
      await flush();
      expect(rig.syncs()).toHaveLength(1);

      rig.api.respond('client/sync', answer({ settings: SETTINGS }));
      await vi.advanceTimersByTimeAsync(2_000);
      await flush();
      await cold;
      expect(rig.syncs()).toHaveLength(2);
      expect(rig.doc('settings')).toEqual(expect.objectContaining({ tag: 'S1', data: SETTINGS }));
    });
  });

  describe('a clock set back', () => {
    it('takes a last sync stamped ahead of the clock as unknown, so a forced call syncs within 2 s', async () => {
      const rig = await setUp({ at: NOW + 3_600_000 });
      rig.api.respond('client/sync', answer());
      const done = rig.refresher.refresh(17, ['tenant'], 'leader');
      await vi.advanceTimersByTimeAsync(1_999);
      expect(rig.syncs()).toEqual([]);
      await vi.advanceTimersByTimeAsync(1);
      await flush();
      await done;

      expect(rig.syncs()).toHaveLength(1);
      expect(rig.doc<TmSyncDoc>('sync')?.data.at).toBe(NOW + 2_000);
      expect(rig.locks.held()).toEqual([]);
    });

    it('stores what a cold call lacks under a last sync stamped ahead of the clock', async () => {
      const rig = await setUp({ at: NOW + 3_600_000 });
      await rig.store.patch(T17, 'settings', () => undefined);
      rig.api.respond('client/sync', answer({ settings: SETTINGS }));
      const cold = rig.refresher.refresh(17, ['settings', 'me', 'tenant'], 'cold');
      await vi.advanceTimersByTimeAsync(2_000);
      await flush();
      await cold;
      expect(rig.doc('settings')).toEqual(expect.objectContaining({ tag: 'S1', data: SETTINGS }));
    });
  });

  describe('coalescing and the throttle', () => {
    it('joins calls arriving while one waits out the 2 s throttle into one sync', async () => {
      const rig = await setUp({ at: NOW - 500 });
      rig.api.respond('client/sync', answer());
      void rig.refresher.refresh(17, ['tenant'], 'leader');
      await flush();
      void rig.refresher.refresh(17, ['me'], 'event');
      rig.refresher.observe(17, { preferences: 'P2' }, 'header');
      await vi.advanceTimersByTimeAsync(1_499);
      expect(rig.syncs()).toEqual([]);
      await vi.advanceTimersByTimeAsync(1);
      await flush();

      expect(rig.syncs()).toHaveLength(1);
      expect(items(rig, 'tm.sync.completed')).toEqual([expect.objectContaining({ trigger: 'leader', changed: 'none' })]);
    });

    it('makes a run cold when a cold call joins it, so it is sent in the foreground and rejects on failure', async () => {
      const rig = await setUp({ at: NOW - 500 });
      rig.api.respond('client/sync', () => Promise.reject(new TmApiError({ kind: 'offline' })));
      const header = rig.refresher.refresh(17, ['tenant'], 'header');
      const cold = rig.refresher.refresh(17, ['settings', 'me', 'tenant'], 'cold');
      const outcome = cold.then(
        () => 'resolved',
        (error: unknown) => (error as TmApiError).kind,
      );
      await vi.advanceTimersByTimeAsync(2_000);
      await flush();

      expect(await outcome).toBe('offline');
      await expect(header).resolves.toBeUndefined();
      expect(rig.syncs()[0]?.options).toEqual(expect.objectContaining({ background: false }));
      expect(items(rig, 'tm.sync.failed')).toEqual([{ kind: 'offline' }]);
    });
  });

  describe('failures', () => {
    it('presents a tenant that no longer exists, even from a background sync', async () => {
      const rig = await setUp();
      const refusal = new TmApiError({ kind: 'tenant-not-found', status: 404, code: 'tenant-not-found' });
      rig.api.respond('client/sync', () => Promise.reject(refusal));
      await rig.refresher.refresh(17, ['tenant'], 'header');
      expect(rig.presented).toEqual([[refusal, 17]]);
      expect(items(rig, 'tm.sync.failed')).toEqual([]);
    });

    it('presents an unavailable tenant and rejects a cold refresh with it', async () => {
      const rig = await setUp({ cold: true });
      const refusal = new TmApiError({ kind: 'tenant-unavailable', status: 403, code: 'tenant-suspended' });
      rig.api.respond('client/sync', () => Promise.reject(refusal));
      await expect(rig.refresher.refresh(17, ['settings', 'me', 'tenant'], 'cold')).rejects.toBe(refusal);
      expect(rig.presented).toEqual([[refusal, 17]]);
    });

    it('leaves a session end and an outdated client to the HTTP layer, and reports anything else', async () => {
      const rig = await setUp();
      for (const kind of ['unauthenticated', 'client-outdated', 'server'] as const) {
        rig.api.respond('client/sync', () => Promise.reject(new TmApiError({ kind })));
        await rig.refresher.refresh(17, ['tenant'], 'poll');
        await vi.advanceTimersByTimeAsync(2_000);
      }
      expect(items(rig, 'tm.sync.failed')).toEqual([{ kind: 'server' }]);
      expect(rig.presented).toEqual([]);
    });

    it('aborts a sync when the tab leaves the tenant', async () => {
      const rig = await setUp({ cold: true });
      rig.api.respond('client/sync', () => new Promise(() => undefined));
      const sync = rig.refresher.refresh(17, ['settings', 'me', 'tenant'], 'cold');
      const outcome = sync.catch((error: unknown) => (error as TmApiError).kind);
      await flush();
      vi.useRealTimers();
      await TestBed.inject(Router).navigateByUrl('/18/centers');

      expect(await outcome).toBe('cancelled');
      expect((rig.syncs()[0]?.options as { signal: AbortSignal }).signal.aborted).toBe(true);
      expect(items(rig, 'tm.sync.failed')).toEqual([]);
    });

    it('aborts a sync when the page is hidden for good', async () => {
      const rig = await setUp();
      rig.api.respond('client/sync', () => new Promise(() => undefined));
      void rig.refresher.refresh(17, ['tenant'], 'leader');
      await flush();
      rig.host.fire('window', 'pagehide');
      expect((rig.syncs()[0]?.options as { signal: AbortSignal }).signal.aborted).toBe(true);
    });

    it('starts a new run for a tenant left and entered again while its run waited for the lock', async () => {
      const rig = await setUp();
      const router = TestBed.inject(Router);
      // Another tab holds the lock meanwhile.
      const elsewhere = gate();
      void rig.locks.run(LOCK, () => elsewhere.promise);
      rig.api.respond('client/sync', answer({ tags: { preferences: 'P2' }, me: me({ preferencesTag: 'P2' }) }));
      rig.refresher.observe(17, { preferences: 'P2' }, 'header');
      await flush();
      void router.navigateByUrl('/');
      await flush();
      void router.navigateByUrl('/17/centers');
      await flush();

      // The aborted run ends when the lock is granted; this call is not joined to it.
      rig.refresher.observe(17, { preferences: 'P2' }, 'header');
      elsewhere.resolve(undefined);
      await flush();
      expect(rig.syncs()).toHaveLength(1);
      expect(rig.doc<TmMeResult>('me')?.data.preferencesTag).toBe('P2');
    });

    it('ends a run whose page was hidden for good during its re-read, before the throttle', async () => {
      const rig = await setUp({ at: NOW - 500 });
      rig.api.respond('client/sync', answer());
      const reread = rig.store.ɵreread.bind(rig.store);
      vi.spyOn(rig.store, 'ɵreread').mockImplementation((partition, names) => {
        rig.host.fire('window', 'pagehide');
        return reread(partition, names);
      });
      const done = rig.refresher.refresh(17, ['tenant'], 'leader');
      await flush();

      // The run released the lock at once rather than wait out the throttle.
      expect(rig.locks.held()).toEqual([]);
      await vi.advanceTimersByTimeAsync(2_000);
      await done;
      expect(rig.syncs()).toEqual([]);
    });
  });

  describe('adoption', () => {
    it('stores a settings save whole, at once in the mirror', async () => {
      const rig = await setUp();
      const saved = { ...SETTINGS, timeZone: 'Asia/Dubai' };
      rig.refresher.adopt(17, { settings: { data: saved, tag: 'S2' } });
      expect(rig.doc('settings')).toEqual(expect.objectContaining({ tag: 'S2', data: saved }));
      await flush();
      expect(rig.doc('settings')).toEqual(expect.objectContaining({ tag: 'S2', data: saved }));
      expect(rig.locks.held()).toEqual([]);
    });

    it('stores a profile save whole', async () => {
      const rig = await setUp();
      const saved = me({ preferencesTag: 'P3' });
      rig.refresher.adopt(17, { me: saved });
      await flush();
      expect(rig.doc('me')?.data).toEqual(saved);
    });

    it("patches a preferences write's bag and tags into the envelope", async () => {
      const rig = await setUp();
      rig.refresher.adopt(17, { preferences: { preferences: { 'nav.pinned': '[7]' }, preferencesTag: 'P2' } });
      await flush();
      expect(rig.doc('me')?.data).toEqual({
        ...me(),
        preferences: { 'nav.pinned': '[7]' },
        preferencesTag: 'P2',
        tags: { ...me().tags, preferences: 'P2' },
      });
    });

    it("patches a locale write's profile members and tags into the envelope", async () => {
      const rig = await setUp();
      rig.refresher.adopt(17, {
        locale: {
          preferredLanguage: 'en',
          preferredTimeZone: 'Asia/Dubai',
          preferredDigits: 'Latin',
          modifiedAt: '2026-11-21T08:00:00.0000000+00:00',
          preferencesTag: 'P2',
        },
      });
      await flush();
      const before = me();
      expect(rig.doc('me')?.data).toEqual({
        ...before,
        user: {
          ...before.user,
          preferredLanguage: 'en',
          preferredCalendar: undefined,
          preferredTimeZone: 'Asia/Dubai',
          preferredRegion: undefined,
          preferredDigits: 'Latin',
          modifiedAt: '2026-11-21T08:00:00.0000000+00:00',
        },
        preferencesTag: 'P2',
        tags: { ...before.tags, preferences: 'P2' },
      });
    });

    it('writes an adoption under the sync lock, after the sync that holds it, and shows it throughout', async () => {
      const rig = await setUp();
      const sync = gate();
      rig.api.respond('client/sync', () => sync.promise);
      void rig.refresher.refresh(17, ['tenant'], 'leader');
      await flush();
      // What the mirror shows once each write lands.
      const write = rig.store.ɵwrite.bind(rig.store);
      const shown: (string | undefined)[] = [];
      vi.spyOn(rig.store, 'ɵwrite').mockImplementation(async (partition, updates) => {
        await write(partition, updates);
        shown.push(rig.doc<TmMeResult>('me')?.data.preferencesTag);
      });
      rig.refresher.adopt(17, { preferences: { preferences: {}, preferencesTag: 'P2' } });
      // The adopted tags show at once. The older envelope the sync then
      // brings is stored first, and the adoption stays shown over it until
      // its own write lands after it.
      expect(rig.doc<TmMeResult>('me')?.data.preferencesTag).toBe('P2');
      sync.resolve(answer({ me: me() }));
      await flush();
      expect(shown).toEqual(['P2', 'P2']);
      expect(rig.doc<TmMeResult>('me')?.data).toEqual(
        expect.objectContaining({ preferences: {}, preferencesTag: 'P2' }),
      );
      expect(rig.locks.held()).toEqual([]);
    });
  });

  describe('the membership boundary', () => {
    it("refreshes access at validUntil plus its jitter, holding no permissions tag", async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0.5);
      const rig = await setUp({ me: me({ validUntil: new Date(NOW + 60_000).toISOString() }) });
      const later = me({ permissions: 'A2', validUntil: new Date(NOW + 3_600_000).toISOString() });
      rig.api.respond('client/sync', answer({ tags: { permissions: 'A2' }, me: later }));
      await vi.advanceTimersByTimeAsync(60_999);
      expect(rig.syncs()).toEqual([]);
      await vi.advanceTimersByTimeAsync(1);
      await flush();

      expect(rig.syncs().map((call) => call.body)).toEqual([{ held: HELD_BUT_PERMISSIONS }]);
      expect(items(rig, 'tm.sync.completed')).toEqual([expect.objectContaining({ trigger: 'boundary', changed: 'me' })]);
      expect(rig.doc<TmMeResult>('me')?.data.access.tag).toBe('A2');
    });

    it('refreshes a hidden tab when it becomes visible after the boundary', async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0);
      const rig = await setUp({ hidden: true, me: me({ validUntil: new Date(NOW + 60_000).toISOString() }) });
      rig.api.respond('client/sync', answer({ me: me({ validUntil: new Date(NOW + 3_600_000).toISOString() }) }));
      await vi.advanceTimersByTimeAsync(120_000);
      expect(rig.syncs()).toEqual([]);

      rig.host.visibleNow = true;
      rig.host.fire('document', 'visibilitychange');
      await flush();
      expect(rig.syncs()).toHaveLength(1);
    });

    it("gives two tabs' timers one sync", async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0.25);
      const rig = await setUp({ me: me({ validUntil: new Date(NOW + 60_000).toISOString() }) });
      rig.another();
      await flush();
      rig.api.respond('client/sync', answer({ me: me({ validUntil: new Date(NOW + 3_600_000).toISOString() }) }));
      await vi.advanceTimersByTimeAsync(60_500);
      await flush();
      await vi.advanceTimersByTimeAsync(10_000);
      await flush();
      expect(rig.syncs()).toHaveLength(1);
    });

    it('backs off at 30 s, 60 s and on to 10 min while the server keeps the boundary, as with a clock ahead', async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0);
      // The device clock is an hour ahead: the server's boundary has passed here.
      const stuck = me({ validUntil: new Date(NOW - 3_600_000).toISOString() });
      const times: number[] = [];
      await setUp({
        me: stuck,
        respond: () => {
          times.push(seconds());
          return answer({ me: stuck });
        },
      });
      for (let i = 0; i < 5; i++) {
        await vi.advanceTimersByTimeAsync(600_000);
        await flush();
      }
      // Seconds after the start: each delay doubles from 30 s and stops at 600 s.
      expect(times.slice(0, 9)).toEqual([0, 30, 90, 210, 450, 930, 1530, 2130, 2730]);
    });

    it('starts the backoff again at 30 s on a new boundary', async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0);
      const stuck = me({ validUntil: new Date(NOW - 1_000).toISOString() });
      const times: number[] = [];
      let next: TmMeResult = stuck;
      await setUp({
        me: stuck,
        respond: () => {
          times.push(seconds());
          return answer({ me: next });
        },
      });
      // Past the second sync, at 30 s, which still finds the boundary passed.
      await vi.advanceTimersByTimeAsync(31_000);
      await flush();
      // The third, at 90 s, brings a new boundary 130 s after the start, which the server keeps.
      next = me({ validUntil: new Date(NOW + 130_000).toISOString() });
      await vi.advanceTimersByTimeAsync(159_000);
      await flush();
      expect(times).toEqual([0, 30, 90, 130, 160]);
    });

    it("arms nothing when the boundary's sync lands after the app has ended", async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0);
      const stuck = me({ validUntil: new Date(NOW - 3_600_000).toISOString() });
      // The server answers the boundary's sync once the app has ended.
      const landed = gate<void>();
      const rig = await setUp({ me: stuck, respond: () => landed.promise.then(() => answer({ me: stuck })) });
      // The sync goes out once the store's re-read under the lock lands.
      await vi.waitFor(async () => {
        await flush();
        expect(rig.syncs()).toHaveLength(1);
      });
      const error = vi.spyOn(rig.telemetry, 'error');

      const unhandled = await tmUnhandled(async () => {
        TestBed.resetTestingModule();
        landed.resolve();
        await vi.advanceTimersByTimeAsync(600_000);
      });
      expect(unhandled).toEqual([]);
      // The boundary's timer is not armed again once the late sync settles.
      expect(vi.getTimerCount()).toBe(0);
      // The app's end cancelled the sync, so nothing is stored or reported, and nothing syncs again.
      expect(error).not.toHaveBeenCalled();
      expect(items(rig, 'tm.sync.failed')).toEqual([]);
      expect(rig.syncs()).toHaveLength(1);
    });

    it('arms no jitter timer for a hub event that arrives after the app has ended', async () => {
      const rig = await setUp();
      rig.api.respond('client/sync', answer());

      const unhandled = await tmUnhandled(async () => {
        TestBed.resetTestingModule();
        const timers = vi.getTimerCount();
        rig.refresher.ɵcacheChanged(17, { tag: 'permissions' });
        rig.refresher.ɵcacheChanged(17, { tag: 'settings', value: 'S2' });
        expect(vi.getTimerCount()).toBe(timers);
        await vi.advanceTimersByTimeAsync(10_000);
      });
      expect(unhandled).toEqual([]);
      expect(rig.syncs()).toEqual([]);
    });
  });

  describe('hub events', () => {
    it('refreshes the envelope after a 0-10 s jitter on a tenant-wide permissions change', async () => {
      vi.spyOn(Math, 'random').mockReturnValue(0.5);
      const rig = await setUp();
      rig.api.respond('client/sync', answer());
      rig.refresher.ɵcacheChanged(17, { tag: 'permissions' });
      await vi.advanceTimersByTimeAsync(4_999);
      expect(rig.syncs()).toEqual([]);
      await vi.advanceTimersByTimeAsync(1);
      await flush();
      expect(rig.syncs()).toHaveLength(1);
      expect(items(rig, 'tm.sync.completed')).toEqual([expect.objectContaining({ trigger: 'event' })]);
    });

    it('observes a new settings tag after a 0-3 s jitter, and a user tag at once', async () => {
      vi.spyOn(Math, 'random').mockReturnValue(1);
      const rig = await setUp();
      rig.api.respond('client/sync', answer({ tags: { preferences: 'P2' }, me: me({ preferencesTag: 'P2' }) }));
      rig.refresher.ɵcacheChanged(17, { tag: 'settings', value: 'S2' });
      rig.refresher.ɵcacheChanged(17, { tag: 'preferences', value: 'P2' });
      await flush();
      expect(rig.syncs()).toHaveLength(1);
      // The settings event comes 3 s later, and finds its tag still wanted.
      await vi.advanceTimersByTimeAsync(3_000);
      await flush();
      expect(rig.syncs()).toHaveLength(2);
    });

    it('does nothing for a value already held, or for an unknown tag', async () => {
      const rig = await setUp();
      rig.refresher.ɵcacheChanged(17, { tag: 'preferences', value: 'P1' });
      rig.refresher.ɵcacheChanged(17, { tag: 'entities', value: 'E9' });
      rig.refresher.ɵcacheChanged(17, { tag: 'acme.something' });
      await vi.advanceTimersByTimeAsync(10_000);
      await flush();
      expect(rig.syncs()).toEqual([]);
    });
  });

  describe('the lists', () => {
    it("stores every list's tag when the composite moved", async () => {
      const rig = await setUp();
      rig.api.respond(
        'client/sync',
        answer({
          tags: { entities: 'E2', 'entity:acme.Currency': 'C2', 'entity:acme.Bank': 'B1' },
          staleEntities: ['acme.Currency'],
        }),
      );
      rig.refresher.observe(17, { entities: 'E2' }, 'header');
      await flush();
      expect(rig.doc<TmSyncDoc>('sync')?.data).toEqual({
        at: NOW,
        entities: 'E2',
        entityTags: { 'acme.Currency': 'C2', 'acme.Bank': 'B1' },
      });
      expect(items(rig, 'tm.sync.completed')).toEqual([expect.objectContaining({ changed: 'entities' })]);
    });

    it("drops a stale list's tag, and the composite, when the tags held cannot show it", async () => {
      const rig = await setUp();
      rig.api.respond('client/sync', answer({ staleEntities: ['acme.Currency'] }));
      await rig.refresher.refresh(17, ['tenant'], 'leader');
      expect(rig.doc<TmSyncDoc>('sync')?.data).toEqual({ at: NOW, entities: null, entityTags: {} });
    });

    it("holds no composite for an entities refresh or a list's, so every list reports its tag", async () => {
      const rig = await setUp();
      rig.api.respond('client/sync', answer({ tags: { 'entity:acme.Currency': 'C1' } }));
      await rig.refresher.refresh(17, ['entities'], 'list');
      await vi.advanceTimersByTimeAsync(2_000);
      await rig.refresher.refresh(17, ['entity:acme.Unit'], 'list');
      expect(rig.syncs().map((call) => call.body)).toEqual([{ held: HELD_BUT_ENTITIES }, { held: HELD_BUT_ENTITIES }]);
    });

    it("ends a list's call at the check once a sync in flight reported the list's tag", async () => {
      const rig = await setUp({ cold: true });
      const sync = gate();
      rig.api.respond('client/sync', () => sync.promise);
      const cold = rig.refresher.refresh(17, ['settings', 'me', 'tenant'], 'cold');
      await flush();
      // Called after the cold sync was sent, so it starts a run of its own.
      const list = rig.refresher.refresh(17, ['entity:acme.Bank'], 'list');
      sync.resolve(answer({ settings: SETTINGS, me: me(), tags: { 'entity:acme.Bank': 'B1' } }));
      await flush();
      await vi.advanceTimersByTimeAsync(2_000);
      await flush();
      await Promise.all([cold, list]);

      expect(rig.syncs()).toHaveLength(1);
      expect(rig.doc<TmSyncDoc>('sync')?.data.entityTags).toEqual({ 'acme.Bank': 'B1' });
    });
  });

  it('runs a cold sync with no held tags and stores every document', async () => {
    const rig = await setUp({ cold: true });
    rig.api.respond(
      'client/sync',
      answer({ settings: SETTINGS, me: me(), tags: { 'entity:acme.Currency': 'C1' } }),
    );
    await rig.refresher.refresh(17, ['settings', 'me', 'tenant'], 'cold');
    expect(rig.syncs().map((call) => [call.body, (call.options as { background: boolean }).background])).toEqual([
      [{ held: {} }, false],
    ]);
    expect(rig.doc('settings')).toEqual(expect.objectContaining({ tag: 'S1', data: SETTINGS }));
    expect(rig.doc('tenant')?.data).toEqual(TENANT);
    expect(items(rig, 'tm.sync.completed')).toEqual([expect.objectContaining({ trigger: 'cold', changed: 'several' })]);
  });

  describe('two tabs over IndexedDB', () => {
    beforeEach(tmDeleteStoreDatabase);
    afterEach(tmDeleteStoreDatabase);

    /** One tab: its own store and mirror over the one database, and its own refresher. */
    interface TmTab {
      readonly injector: EnvironmentInjector;
      readonly store: TmClientStore;
      readonly refresher: TmCacheRefresher;
    }

    /** What a two-tab spec drives and inspects. */
    interface TmTwoTabs {
      readonly first: TmTab;
      readonly second: TmTab;
      /** Settles the first `client/sync`. */
      readonly sync: TmGate<unknown>;
      /** The `client/sync` calls so far. */
      syncs(): TmFakeApiCall[];
      /** Resolves once both tabs' runs have ended, since it queues behind them. */
      settled(): Promise<void>;
    }

    /**
     * Configures two tabs on `/17/centers`, sharing the locks and the API.
     * Each tab bus delivers nothing, so only the re-read inside the lock can
     * show the second tab what the first stored.
     */
    async function twoTabs(): Promise<TmTwoTabs> {
      TestBed.configureTestingModule({
        providers: [
          provideTellmaTesting(),
          provideTellmaStateTesting(),
          provideRouter([{ path: '**', component: TmPage }]),
          { provide: TM_API_HOST, useValue: new TmFakeApiHost() },
          { provide: TM_NOW, useValue: () => Date.now() },
        ],
      });
      await TestBed.inject(Router).navigateByUrl('/17/centers');
      const tab = (): TmTab => {
        const injector = createEnvironmentInjector(
          [
            { provide: TM_STORE_HOST, useValue: { ...tmMemoryStoreHost(), memory: false, indexedDB } },
            { provide: TmTabBus, useClass: TmRecordingTabBus },
            TmClientStore,
            TmCacheRefresher,
          ],
          TestBed.inject(EnvironmentInjector),
        );
        return { injector, store: injector.get(TmClientStore), refresher: injector.get(TmCacheRefresher) };
      };
      const api = TestBed.inject(TmFakeApi);
      const sync = gate();
      api.respond('client/sync', () => sync.promise);
      return {
        first: tab(),
        second: tab(),
        sync,
        syncs: () => api.calls().filter((call) => call.route === 'client/sync'),
        settled: () => TestBed.inject(TmFakeLocks).run(LOCK, () => Promise.resolve()),
      };
    }

    it("ends the second tab's run at the check, from what it re-reads before any message tells it", async () => {
      const { first, second, sync, syncs, settled } = await twoTabs();
      const stored = <T>(data: T, tag: string | null = null): TmStoredDoc<T> => ({ tag, savedAt: Date.now() - 60_000, data });
      await first.store.ɵwrite(T17, {
        settings: () => stored(SETTINGS, 'S1'),
        me: () => stored(me()),
        tenant: () => stored(TENANT),
        sync: () => stored<TmSyncDoc>({ at: Date.now() - 60_000, entities: 'E1', entityTags: {} }),
      });
      await second.store.readAll(T17);

      first.refresher.observe(17, { preferences: 'P2' }, 'header');
      second.refresher.observe(17, { preferences: 'P2' }, 'header');
      await vi.waitFor(() => expect(syncs()).toHaveLength(1));
      sync.resolve(answer({ tags: { preferences: 'P2' }, me: me({ preferencesTag: 'P2' }) }));
      await settled();

      expect(syncs()).toHaveLength(1);
      expect(second.store.doc<TmMeResult>(T17, 'me')()?.data.preferencesTag).toBe('P2');
      first.injector.destroy();
      second.injector.destroy();
    });

    it("ends the second cold tab's run at the check once the first stored every launch document", async () => {
      const { first, second, sync, syncs, settled } = await twoTabs();
      await second.store.readAll(T17);
      const launch = ['settings', 'me', 'tenant'];
      const cold = [first.refresher.refresh(17, launch, 'cold'), second.refresher.refresh(17, launch, 'cold')];
      await vi.waitFor(() => expect(syncs()).toHaveLength(1));
      sync.resolve(answer({ settings: SETTINGS, me: me() }));
      await settled();
      await Promise.all(cold);

      expect(syncs()).toHaveLength(1);
      expect(second.store.doc(T17, 'tenant')()?.data).toEqual(TENANT);
      first.injector.destroy();
      second.injector.destroy();
    });
  });
});
