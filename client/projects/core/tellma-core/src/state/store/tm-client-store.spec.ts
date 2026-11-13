// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ApplicationRef, createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { tmDeleteExpiredUser } from '../../boot/tm-boot-expiry';
import { TmTelemetry } from '../../observe/tm-telemetry';
import { tmProfileCookies } from '../../session/tm-profile.testing.util';
import { TmSession } from '../../session/tm-session';
import { TmSessionPaths } from '../../session/tm-session-paths';
import { TmFakeSessionHost, TmRecordingTelemetry, tmSessionTestingProviders } from '../../session/tm-session.testing.util';
import { TmTabBus } from '../bus/tm-tab-bus';
import { TmRecordingTabBus } from '../bus/tm-tab-bus.testing.util';
import type { TmBootRecord, TmBootTheme } from '../tm-boot-record';
import { TM_REFUSING_STORAGE, TmFakeStorage } from '../tm-storage.testing.util';
import { TmClientStore, TmStoreRefusedError, type TmStoredDoc, type TmSweepRule } from './tm-client-store';
import { TM_DOC_FORMATS, TM_STORE_EXTRA_FORMATS, TM_STORE_FORMATS } from './tm-doc-formats';
import { TM_STORE_HOST, type TmStoreHost } from './tm-store-host';
import { TM_IMAGES_CACHE, TM_STORE_DATABASE, TM_STORE_DOCS, type TmPartition } from './tm-store-layout';
import vectors from './tm-store-layout-vectors.json';
import {
  tmDeleteStoreDatabase,
  tmReadRawKeys,
  tmReadRawRecords,
  tmSettle,
  tmWriteRawRecords,
  type TmRawRecord,
} from './tm-store.testing.util';

// The client store over the browser's real IndexedDB, with a faked session,
// tab bus, local storage and page events.

const NOW = 1_700_000_000_000;
const DAY = 86_400_000;
const FARAH = '0b4f';
const SARA = '7c1e';
const DEVICE: TmPartition = { kind: 'device' };
const USER: TmPartition = { kind: 'user', sub: FARAH };
const T17: TmPartition = { kind: 'tenant', sub: FARAH, tenantId: 17 };
const T170: TmPartition = { kind: 'tenant', sub: FARAH, tenantId: 170 };

/** A document saved now. */
function docOf<T>(data: T, savedAt = NOW, tag: string | null = null): TmStoredDoc<T> {
  return { tag, savedAt, data };
}

/** The vector documents as records, each holding its own key as its value. */
function vectorRecords(): TmRawRecord[] {
  return vectors.documents.map(([p, n]) => ({ p: p!, n: n!, tag: null, savedAt: NOW, data: `${p} ${n}` }));
}

/** A tenant's `sync` record, synced at a time. */
function syncRecord(p: string, at = NOW): TmRawRecord {
  return { p, n: 'sync@1', tag: null, savedAt: at, data: { at, entities: null, entityTags: {} } };
}

/** What a spec reaches. */
interface Fixture {
  readonly store: TmClientStore;
  readonly bus: TmRecordingTabBus;
  readonly session: TmFakeSessionHost;
  readonly telemetry: TmRecordingTelemetry;
  readonly local: TmFakeStorage;
  readonly events: EventTarget;
  readonly cacheDeletes: string[];
  readonly persists: { count: number };
}

/** Configures the store over the real IndexedDB, signed in as Farah unless the cookies say otherwise. */
function setUp(options: { cookies?: string; host?: Partial<TmStoreHost>; extraFormats?: Record<string, number> } = {}): Fixture {
  const session = new TmFakeSessionHost();
  session.cookieJar = options.cookies ?? tmProfileCookies(FARAH);
  const local = new TmFakeStorage();
  const events = new EventTarget();
  const cacheDeletes: string[] = [];
  const persists = { count: 0 };
  const host: TmStoreHost = {
    indexedDB,
    localStorage: local,
    caches: { delete: (name) => Promise.resolve(cacheDeletes.push(name) > 0) },
    events,
    documentLanguage: () => ({ lang: 'ar', dir: 'rtl' }),
    persist: () => void persists.count++,
    ...options.host,
  };
  TestBed.configureTestingModule({
    providers: [
      ...tmSessionTestingProviders(session, null, NOW),
      { provide: TM_STORE_HOST, useValue: host },
      { provide: TmTabBus, useClass: TmRecordingTabBus },
      ...(options.extraFormats ? [{ provide: TM_STORE_EXTRA_FORMATS, useValue: options.extraFormats }] : []),
    ],
  });
  // The session reads its user from the cookie when first injected.
  TestBed.inject(TmSession);
  return {
    store: TestBed.inject(TmClientStore),
    bus: TestBed.inject(TmTabBus) as TmRecordingTabBus,
    session,
    telemetry: TestBed.inject(TmTelemetry) as TmRecordingTelemetry,
    local,
    events,
    cacheDeletes,
    persists,
  };
}

/** Counts the store's transactions from now on, by mode. */
function spyTransactions(): () => IDBTransactionMode[] {
  const spy = vi.spyOn(IDBDatabase.prototype, 'transaction');
  return () => spy.mock.calls.map((call) => call[1] ?? 'readonly');
}

/** The events of one name the telemetry recorded, as their properties. */
function eventsNamed(telemetry: TmRecordingTelemetry, name: string): unknown[] {
  return telemetry.events.filter((event) => event.name === name).map((event) => event.props);
}

describe('TmClientStore', () => {
  beforeEach(tmDeleteStoreDatabase);
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });
  afterAll(tmDeleteStoreDatabase);

  describe('the write guard', () => {
    it("refuses a write into another user's partition, writing nothing", async () => {
      const { store, telemetry } = setUp();
      const sara: TmPartition = { kind: 'user', sub: SARA };
      await expect(store.put(sara, 'tenants', docOf([]))).rejects.toMatchObject({ reason: 'other-user' });
      await expect(store.patch(sara, 'tenants', () => [])).rejects.toBeInstanceOf(TmStoreRefusedError);
      expect(eventsNamed(telemetry, 'tm.store.refused')).toEqual([{ reason: 'other-user' }, { reason: 'other-user' }]);
      expect(store.doc(sara, 'tenants')()).toBeUndefined();
      expect(await tmReadRawKeys()).toEqual([]);
    });

    it('refuses a write while the cookie names another user or none, the device partition included', async () => {
      const { store, session, telemetry } = setUp();
      session.cookieJar = tmProfileCookies(SARA);
      await expect(store.put(USER, 'tenants', docOf([]))).rejects.toMatchObject({ reason: 'cookie' });
      session.cookieJar = '';
      await expect(store.patch(DEVICE, 'zone', () => ({}))).rejects.toMatchObject({ reason: 'cookie' });
      expect(eventsNamed(telemetry, 'tm.store.refused')).toEqual([{ reason: 'cookie' }, { reason: 'cookie' }]);
      expect(await tmReadRawKeys()).toEqual([]);
    });

    it('refuses a write once the session is not active', async () => {
      const { store, telemetry } = setUp();
      TestBed.inject(TmSessionPaths).state.set('signing-out');
      const error = await store.put(T17, 'me', docOf({})).catch((caught: unknown) => caught);
      expect(error).toBeInstanceOf(TmStoreRefusedError);
      expect(error).toMatchObject({ reason: 'session', name: 'TmStoreRefusedError' });
      expect(eventsNamed(telemetry, 'tm.store.refused')).toEqual([{ reason: 'session' }]);
      expect(await tmReadRawKeys()).toEqual([]);
    });

    it("lets the user's own writes through, the device partition's included", async () => {
      const { store } = setUp();
      await store.put(DEVICE, 'zone', docOf({ users: {} }));
      await store.put(USER, 'tenants', docOf([]));
      expect(await tmReadRawKeys()).toEqual([
        ['device', 'zone@1'],
        ['u:0b4f/', 'tenants@1'],
      ]);
    });
  });

  describe('sweeps', () => {
    for (const vector of vectors.sweeps) {
      it(`leaves exactly the right keys: ${vector.name}`, async () => {
        const { store, bus, telemetry } = setUp({ cookies: tmProfileCookies(vector.sub) });
        const timing = vi.spyOn(telemetry, 'timing');
        await tmWriteRawRecords(vectorRecords());
        await store.sweep(vector.rule as TmSweepRule);
        expect(await tmReadRawKeys()).toEqual(vector.remaining);
        // Every tab hears the deleted prefix, with no names.
        expect(bus.posted).toEqual([{ type: 'store.changed', partition: vector.prefix, names: null }]);
        const rule = typeof vector.rule === 'string' ? vector.rule : 'tenant' in vector.rule ? 'tenant' : 'session-other-than';
        expect(timing).toHaveBeenCalledExactlyOnceWith('tm.store.sweep', expect.any(Number), { rule });
      });
    }

    it("deletes the image cache when a session ends, and only then", async () => {
      const { store, cacheDeletes } = setUp();
      await store.sweep({ tenant: 17 });
      await store.sweep({ sessionOtherThan: 'Y4kQ1x-_' });
      expect(cacheDeletes).toEqual([]);
      await store.sweep('session-ended');
      expect(cacheDeletes).toEqual([TM_IMAGES_CACHE]);
    });

    it('empties the mirror of the deleted documents at once, and keeps the rest', async () => {
      const { store } = setUp();
      await tmWriteRawRecords([...vectorRecords(), syncRecord('u:0b4f/t17'), syncRecord('u:0b4f/t170')]);
      await store.readAll(USER, T17, T170);
      const swept = store.sweep({ tenant: 17 });
      expect(store.doc(T17, 'me')()).toBeUndefined();
      expect(store.doc(T170, 'me')()?.data).toBe('u:0b4f/t170 me@1');
      expect(store.doc(USER, 'tenants')()?.data).toBe('u:0b4f/ tenants@1');
      await swept;
    });

    it("deletes nothing of a user's own for the user's rules when no one is signed in", async () => {
      const { store, bus } = setUp({ cookies: '' });
      await tmWriteRawRecords(vectorRecords());
      await store.sweep({ tenant: 17 });
      await store.sweep({ sessionOtherThan: 'Y4kQ1x-_' });
      expect(await tmReadRawKeys()).toEqual(vectors.documents);
      expect(bus.posted).toEqual([]);
    });

    it("keeps the device zone through every sweep and the boot script's expiry delete", async () => {
      const { store } = setUp();
      await tmWriteRawRecords(vectorRecords());
      await store.sweep({ tenant: 17 });
      await store.sweep({ sessionOtherThan: 'Y4kQ1x-_' });
      await store.sweep('session-ended');
      // The boot script's delete runs over the same database, by the same layout.
      await tmDeleteExpiredUser(indexedDB, null, FARAH, new Promise<void>(() => undefined));
      expect(await tmReadRawKeys()).toEqual([['device', 'zone@1']]);
    });

    it("leaves the boot record's user and last tenant, which only sign-out's clean-up clears", async () => {
      const { store } = setUp();
      store.boot.patch({ sub: FARAH, lastTenantId: 17 });
      await store.sweep('session-ended');
      expect(store.boot.read()).toMatchObject({ sub: FARAH, lastTenantId: 17 });
      // Sign-out's clean-up adds this patch after the sweep (spec 0025 §2.6, step 3).
      store.boot.patch({ sub: null, lastTenantId: null });
      expect(store.boot.read()).toMatchObject({ sub: null, lastTenantId: null });
    });
  });

  describe('reads', () => {
    for (const vector of vectors.partitionReads) {
      it(`reads a partition's documents and no other's: ${vector.name}`, async () => {
        const { store } = setUp();
        await tmWriteRawRecords(vectorRecords());
        const reads = spyTransactions();
        await store.readAll(vector.partition as TmPartition);
        expect(reads()).toEqual(['readonly']);
        // The getAll over the partition's range found these, and no others.
        const getAll = vi.spyOn(IDBObjectStore.prototype, 'getAll');
        await store.readAll(vector.partition as TmPartition);
        const range = getAll.mock.calls[0]![0] as IDBKeyRange;
        expect(vectors.documents.filter((key) => range.includes(key))).toEqual(vector.documents);
      });
    }

    it('reads several partitions in one readonly transaction, and mirrors what it read', async () => {
      const { store } = setUp();
      await tmWriteRawRecords([...vectorRecords(), syncRecord('u:0b4f/t17')]);
      const reads = spyTransactions();
      await store.readAll(DEVICE, USER, T17);
      expect(reads()).toEqual(['readonly']);
      expect(store.doc(DEVICE, 'zone')()?.data).toBe('device zone@1');
      expect(store.doc(USER, 'distribution')()?.data).toBe('u:0b4f/ distribution@1');
      expect(store.doc(T17, 'entity:acme.Currency')()?.data).toBe('u:0b4f/t17 entity:acme.Currency@1');
    });

    it('empties a mirrored document the partition no longer holds', async () => {
      const { store } = setUp();
      await tmWriteRawRecords(vectorRecords());
      await store.readAll(USER);
      expect(store.doc(USER, 'tenants')()).toBeDefined();
      await tmDeleteStoreDatabase();
      await store.readAll(USER);
      expect(store.doc(USER, 'tenants')()).toBeUndefined();
    });
  });

  describe('formats', () => {
    it("stores each document under this build's stored name, the prefixes' included", async () => {
      const { store } = setUp({ extraFormats: { inbox: 3 } });
      await store.put(T17, 'settings', docOf({}));
      await store.put(T17, 'entity:acme.Currency', docOf([]));
      await store.put(T17, 'inbox', docOf([]));
      await store.put({ kind: 'session', sub: FARAH, sessionHandle: 'sh-0b4f' }, 'tree:/17/centers', docOf([]));
      expect(await tmReadRawKeys()).toEqual([
        ['u:0b4f/ssh-0b4f', 'tree:/17/centers@1'],
        ['u:0b4f/t17', 'entity:acme.Currency@1'],
        ['u:0b4f/t17', 'inbox@3'],
        ['u:0b4f/t17', 'settings@1'],
      ]);
    });

    it('refuses a document name with no format', () => {
      const { store } = setUp();
      expect(() => store.doc(T17, 'pins')).toThrowError(/no format for the document 'pins'/);
      expect(Object.isFrozen(TM_DOC_FORMATS)).toBe(true);
    });

    it("runs two builds' settings side by side: each reads its own copy, and the other's change re-reads nothing", async () => {
      const one = setUp();
      await tmWriteRawRecords([syncRecord('u:0b4f/t17')]);
      // The second build: the same database and session, settings at format 2, its own bus.
      const twoInjector = createEnvironmentInjector(
        [
          { provide: TM_STORE_FORMATS, useValue: { ...TM_DOC_FORMATS, settings: 2 } },
          { provide: TmTabBus, useClass: TmRecordingTabBus },
          { provide: TmClientStore, useClass: TmClientStore },
        ],
        TestBed.inject(EnvironmentInjector),
      );
      const two = twoInjector.get(TmClientStore);
      const twoBus = twoInjector.get(TmTabBus) as TmRecordingTabBus;
      await one.store.readAll(T17);
      await two.readAll(T17);
      await one.store.put(T17, 'settings', docOf('one'));
      await two.put(T17, 'settings', docOf('two'));
      expect(await tmReadRawKeys()).toEqual([
        ['u:0b4f/t17', 'settings@1'],
        ['u:0b4f/t17', 'settings@2'],
        ['u:0b4f/t17', 'sync@1'],
      ]);
      expect(twoBus.posted).toEqual([{ type: 'store.changed', partition: 'u:0b4f/t17', names: ['settings@2'] }]);

      // The first build hears the second's write, and reads nothing for it.
      const reads = spyTransactions();
      one.bus.receive(twoBus.posted[0]!);
      await new Promise((resolve) => setTimeout(resolve, 20));
      expect(reads()).toEqual([]);
      expect(one.store.doc(T17, 'settings')()?.data).toBe('one');
      expect(two.doc(T17, 'settings')()?.data).toBe('two');
      twoInjector.destroy();
    });

    it("skips another build's copy when it reads", async () => {
      const { store } = setUp();
      await tmWriteRawRecords([syncRecord('u:0b4f/t17'), { ...syncRecord('u:0b4f/t17'), n: 'settings@2', data: 'newer build' }]);
      await store.readAll(T17);
      expect(store.doc(T17, 'sync')()).toBeDefined();
      expect(store.doc(T17, 'settings')()).toBeUndefined();
    });
  });

  describe('the idle rule', () => {
    it("shows a tenant's documents only while its last sync lies within the window", async () => {
      const { store } = setUp();
      await store.put(T17, 'me', docOf({ name: 'Farah' }));
      expect(store.doc(T17, 'me')()).toBeUndefined();
      await store.put(T17, 'sync', docOf({ at: NOW - 7 * DAY }));
      expect(store.doc(T17, 'me')()?.data).toEqual({ name: 'Farah' });
      await store.put(T17, 'sync', docOf({ at: NOW - 7 * DAY - 1 }));
      expect(store.doc(T17, 'me')()).toBeUndefined();
      expect(store.doc(T17, 'sync')()).toBeUndefined();
      // The stored distribution's idleDays widens the window.
      await store.put(USER, 'distribution', docOf({ session: { idleDays: 30 } }));
      expect(store.doc(T17, 'me')()?.data).toEqual({ name: 'Farah' });
    });

    it("shows the user's tenants and distribution only while they are younger than the window", async () => {
      const { store } = setUp();
      await store.put(USER, 'tenants', docOf(['acme'], NOW - 8 * DAY));
      await store.put(USER, 'distribution', docOf({ session: { idleDays: 9 } }, NOW - 8 * DAY));
      expect(store.doc(USER, 'tenants')()?.data).toEqual(['acme']);
      await store.put(USER, 'distribution', docOf({ session: { idleDays: 7 } }, NOW - 8 * DAY));
      expect(store.doc(USER, 'distribution')()).toBeUndefined();
      expect(store.doc(USER, 'tenants')()).toBeUndefined();
    });

    it('has no idle rule for the device zone or a session partition', async () => {
      const { store } = setUp();
      const session: TmPartition = { kind: 'session', sub: FARAH, sessionHandle: 'sh-0b4f' };
      await store.put(DEVICE, 'zone', docOf({}, 0));
      await store.put(session, 'tree:/17/centers', docOf([], 0));
      expect(store.doc(DEVICE, 'zone')()).toBeDefined();
      expect(store.doc(session, 'tree:/17/centers')()).toBeDefined();
    });
  });

  describe('put and patch', () => {
    it('shows a put at once, and tells the other tabs once it commits', async () => {
      const { store, bus } = setUp();
      const put = store.put(USER, 'tenants', docOf(['acme']));
      expect(store.doc(USER, 'tenants')()?.data).toEqual(['acme']);
      expect(bus.posted).toEqual([]);
      await put;
      expect(bus.posted).toEqual([{ type: 'store.changed', partition: 'u:0b4f/', names: ['tenants@1'] }]);
    });

    it('keeps a stored copy saved later, here and in the mirror, and writes nothing', async () => {
      const { store, bus } = setUp();
      await tmWriteRawRecords([{ p: 'u:0b4f/', n: 'tenants@1', tag: null, savedAt: NOW, data: ['later'] }]);
      await store.put(USER, 'tenants', docOf(['earlier'], NOW - 1));
      expect(store.doc(USER, 'tenants')()?.data).toEqual(['later']);
      expect((await tmReadRawRecords())[0]?.data).toEqual(['later']);
      expect(bus.posted).toEqual([]);
      // A put saved at the same time or later replaces it.
      await store.put(USER, 'tenants', docOf(['same'], NOW));
      expect((await tmReadRawRecords())[0]?.data).toEqual(['same']);
    });

    it("patches in one readwrite transaction, from the stored value, keeping the tag and stamping now", async () => {
      const { store, bus } = setUp();
      await tmWriteRawRecords([{ p: 'u:0b4f/t17', n: 'me@1', tag: 'held', savedAt: 1, data: { pins: ['a'] } }]);
      const transactions = spyTransactions();
      await store.patch<{ pins: string[] }>(T17, 'me', (current) => ({ pins: [...(current?.pins ?? []), 'b'] }));
      expect(transactions()).toEqual(['readwrite']);
      expect(await tmReadRawRecords()).toEqual([
        { p: 'u:0b4f/t17', n: 'me@1', tag: 'held', savedAt: NOW, data: { pins: ['a', 'b'] } },
      ]);
      expect(bus.posted).toEqual([{ type: 'store.changed', partition: 'u:0b4f/t17', names: ['me@1'] }]);
    });

    it('deletes the document when the update returns undefined', async () => {
      const { store } = setUp();
      await store.put(USER, 'tenants', docOf(['acme']));
      await store.patch(USER, 'tenants', () => undefined);
      expect(store.doc(USER, 'tenants')()).toBeUndefined();
      expect(await tmReadRawKeys()).toEqual([]);
    });

    it('writes nothing when the update throws, and rejects with its error', async () => {
      const { store } = setUp();
      await store.put(USER, 'tenants', docOf(['acme']));
      const failure = new Error('The update failed.');
      await expect(
        store.patch(USER, 'tenants', () => {
          throw failure;
        }),
      ).rejects.toBe(failure);
      expect(store.doc(USER, 'tenants')()?.data).toEqual(['acme']);
      expect((await tmReadRawRecords())[0]?.data).toEqual(['acme']);
    });

    it('re-reads what another tab changed', async () => {
      const { store, bus } = setUp();
      await store.put(USER, 'tenants', docOf(['acme']));
      await tmWriteRawRecords([{ p: 'u:0b4f/', n: 'tenants@1', tag: null, savedAt: NOW, data: ['acme', 'beta'] }]);
      bus.receive({ type: 'store.changed', partition: 'u:0b4f/', names: ['tenants@1'] });
      await vi.waitFor(() => expect(store.doc(USER, 'tenants')()?.data).toEqual(['acme', 'beta']));
    });

    it("re-reads under another tab's sweep prefix, so a partition the prefix covers but the sweep kept stays", async () => {
      const { store, bus } = setUp();
      await tmWriteRawRecords([...vectorRecords(), syncRecord('u:0b4f/t17'), syncRecord('u:0b4f/t170')]);
      await store.readAll(T17, T170);
      // Another tab swept tenant 17.
      const db = await tmSettle(indexedDB.open(TM_STORE_DATABASE));
      const transaction = db.transaction(TM_STORE_DOCS, 'readwrite');
      transaction.objectStore(TM_STORE_DOCS).delete(IDBKeyRange.bound(['u:0b4f/t17'], ['u:0b4f/t17', []]));
      await new Promise((resolve) => (transaction.oncomplete = resolve));
      db.close();
      bus.receive({ type: 'store.changed', partition: 'u:0b4f/t17', names: null });
      await vi.waitFor(() => expect(store.doc(T17, 'me')()).toBeUndefined());
      expect(store.doc(T170, 'me')()?.data).toBe('u:0b4f/t170 me@1');
    });

    it('reads every mirrored document again when the page comes back from the back-forward cache', async () => {
      const { store, events } = setUp();
      await store.put(USER, 'tenants', docOf(['acme']));
      await tmWriteRawRecords([{ p: 'u:0b4f/', n: 'tenants@1', tag: null, savedAt: NOW, data: ['missed'] }]);
      events.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: false }));
      await new Promise((resolve) => setTimeout(resolve, 20));
      expect(store.doc(USER, 'tenants')()?.data).toEqual(['acme']);
      events.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
      await vi.waitFor(() => expect(store.doc(USER, 'tenants')()?.data).toEqual(['missed']));
    });

    it("asks to keep the site's storage once, after the first tenant write", async () => {
      const { store, persists } = setUp();
      await store.put(USER, 'tenants', docOf([]));
      expect(persists.count).toBe(0);
      await store.put(T17, 'me', docOf({}));
      await store.patch(T17, 'me', () => ({ again: true }));
      expect(persists.count).toBe(1);
    });
  });

  describe('fallbacks', () => {
    it('drops a write the quota refuses, keeping it in the mirror', async () => {
      const { store, bus, telemetry } = setUp();
      vi.spyOn(IDBObjectStore.prototype, 'put').mockImplementation(() => {
        throw new DOMException('The quota is exceeded.', 'QuotaExceededError');
      });
      await store.put(USER, 'tenants', docOf(['acme']));
      await store.patch(T17, 'me', () => ({}));
      expect(store.doc(USER, 'tenants')()?.data).toEqual(['acme']);
      expect(eventsNamed(telemetry, 'tm.store.quota')).toEqual([{ reason: 'put' }, { reason: 'patch' }]);
      expect(bus.posted).toEqual([]);
      vi.restoreAllMocks();
      expect(await tmReadRawKeys()).toEqual([]);
    });

    it('runs in memory without IndexedDB: the mirror serves this tab, and no other tab hears of it', async () => {
      const { store, bus, telemetry } = setUp({ host: { indexedDB: null } });
      await store.readAll(DEVICE, USER);
      expect(store.mode).toBe('memory');
      await store.put(USER, 'tenants', docOf(['acme']));
      await store.patch<string[]>(USER, 'tenants', (current) => [...(current ?? []), 'beta']);
      expect(store.doc(USER, 'tenants')()).toEqual({ tag: null, savedAt: NOW, data: ['acme', 'beta'] });
      await store.sweep('session-ended');
      expect(store.doc(USER, 'tenants')()).toBeUndefined();
      expect(bus.posted).toEqual([]);
      expect(eventsNamed(telemetry, 'tm.store.unavailable')).toEqual([{ reason: 'missing' }]);
    });

    it('runs in memory when the database is at a later version', async () => {
      (await tmSettle(indexedDB.open(TM_STORE_DATABASE, 2))).close();
      const { store, telemetry } = setUp();
      await store.readAll(DEVICE);
      expect(store.mode).toBe('memory');
      expect(eventsNamed(telemetry, 'tm.store.unavailable')).toEqual([{ reason: 'version' }]);
    });

    it('runs in memory when the open throws', async () => {
      const factory = {
        open: () => {
          throw new DOMException('Blocked.', 'SecurityError');
        },
      } as unknown as IDBFactory;
      const { store, telemetry } = setUp({ host: { indexedDB: factory } });
      await store.readAll(DEVICE);
      expect([store.mode, eventsNamed(telemetry, 'tm.store.unavailable')]).toEqual(['memory', [{ reason: 'failed' }]]);
    });

    it('runs in memory when the database takes over 3 s to open', async () => {
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
      const factory = { open: () => ({}) as IDBOpenDBRequest } as unknown as IDBFactory;
      const { store, telemetry } = setUp({ host: { indexedDB: factory } });
      let read = false;
      void store.readAll(DEVICE).then(() => (read = true));
      await vi.advanceTimersByTimeAsync(2_999);
      expect(read).toBe(false);
      await vi.advanceTimersByTimeAsync(1);
      expect([read, store.mode, eventsNamed(telemetry, 'tm.store.unavailable')]).toEqual([
        true,
        'memory',
        [{ reason: 'timeout' }],
      ]);
    });

    it('closes its connection when the database is deleted elsewhere, and opens again at the next access', async () => {
      const { store } = setUp();
      await store.put(USER, 'tenants', docOf(['acme']));
      const request = indexedDB.deleteDatabase(TM_STORE_DATABASE);
      let blocked = false;
      request.onblocked = () => (blocked = true);
      await tmSettle(request);
      expect(blocked).toBe(false);
      await store.put(USER, 'tenants', docOf(['again']));
      expect(store.mode).toBe('indexeddb');
      expect(await tmReadRawKeys()).toEqual([['u:0b4f/', 'tenants@1']]);
    });
  });

  describe('the boot record', () => {
    it("reads the defaults, with the document's language, while none is stored or the stored one is invalid", () => {
      const { store, local } = setUp();
      const defaults: TmBootRecord = { v: 1, theme: 'system', lang: 'ar', dir: 'rtl', sub: null, lastTenantId: null };
      expect(store.boot.read()).toEqual(defaults);
      local.setItem('tm.boot', '{"v":2}');
      expect(store.boot.read()).toEqual(defaults);
    });

    it('patches members into local storage, and refuses a patch that makes an invalid record', () => {
      const { store, local } = setUp();
      store.boot.patch({ theme: 'dark', lastTenantId: 17 });
      expect(local.json('tm.boot')).toEqual({ v: 1, theme: 'dark', lang: 'ar', dir: 'rtl', sub: null, lastTenantId: 17 });
      expect(() => store.boot.patch({ theme: 'blue' as TmBootTheme })).toThrowError(/invalid boot record/);
      expect(store.boot.read().theme).toBe('dark');
    });

    it("emits another tab's writes of it, and a clear of the whole storage", () => {
      const { store, local, events } = setUp();
      const seen: TmBootRecord[] = [];
      const subscription = store.boot.changes.subscribe((record) => seen.push(record));
      local.setItem('tm.boot', JSON.stringify({ v: 1, theme: 'light', lang: 'en', dir: 'ltr', sub: null, lastTenantId: null }));
      events.dispatchEvent(new StorageEvent('storage', { key: 'tm.boot' }));
      events.dispatchEvent(new StorageEvent('storage', { key: 'tm.login' }));
      events.dispatchEvent(new StorageEvent('storage', { key: null }));
      subscription.unsubscribe();
      events.dispatchEvent(new StorageEvent('storage', { key: 'tm.boot' }));
      expect(seen.map((record) => record.theme)).toEqual(['light', 'light']);
    });

    it('lives in memory when local storage refuses', () => {
      const { store, telemetry } = setUp({ host: { localStorage: TM_REFUSING_STORAGE } });
      expect(store.boot.read().theme).toBe('system');
      store.boot.patch({ theme: 'dark' });
      store.boot.patch({ sub: FARAH });
      expect(store.boot.read()).toMatchObject({ theme: 'dark', sub: FARAH });
      expect(eventsNamed(telemetry, 'tm.store.unavailable')).toEqual([{ reason: 'local-storage' }]);
    });
  });

  describe('the boot step', () => {
    /** The vector documents with tenant 17 synced now. */
    const warmRecords = (): TmRawRecord[] => [...vectorRecords(), syncRecord('u:0b4f/t17')];

    /** Holds the idle callbacks the boot step schedules, which run only when the spec runs them. */
    function holdIdleCallbacks(): IdleRequestCallback[] {
      const held: IdleRequestCallback[] = [];
      vi.spyOn(window, 'requestIdleCallback').mockImplementation((callback) => held.push(callback));
      return held;
    }

    it('reads a warm launch in one readonly transaction, and nothing else', async () => {
      const { store, bus, local } = setUp();
      await tmWriteRawRecords(warmRecords());
      local.setItem('tm.boot', JSON.stringify({ v: 1, theme: 'system', lang: 'en', dir: 'ltr', sub: FARAH, lastTenantId: 17 }));
      holdIdleCallbacks();
      const transactions = spyTransactions();
      await store.ɵstart(17);
      expect(transactions()).toEqual(['readonly']);
      expect(store.doc(DEVICE, 'zone')()?.data).toBe('device zone@1');
      expect(store.doc(USER, 'tenants')()?.data).toBe('u:0b4f/ tenants@1');
      expect(store.doc(T17, 'me')()?.data).toBe('u:0b4f/t17 me@1');
      expect(bus.posted).toEqual([]);
      expect((await tmReadRawKeys()).length).toBe(warmRecords().length);
    });

    it('sweeps every user first when the boot record names no user, then records the user', async () => {
      const { store, bus } = setUp();
      await tmWriteRawRecords(warmRecords());
      const transactions = spyTransactions();
      await store.ɵstart(17);
      expect(transactions()).toEqual(['readwrite', 'readonly']);
      expect(await tmReadRawKeys()).toEqual([['device', 'zone@1']]);
      // The zone survives the backstop, and is mirrored by the read.
      expect(store.doc(DEVICE, 'zone')()?.data).toBe('device zone@1');
      expect(bus.posted).toEqual([{ type: 'store.changed', partition: 'u:', names: null }]);
      expect(store.boot.read().sub).toBe(FARAH);
    });

    it('sweeps every user first when the boot record names another user', async () => {
      const { store } = setUp();
      await tmWriteRawRecords(warmRecords());
      store.boot.patch({ sub: SARA });
      await store.ɵstart(null);
      expect(await tmReadRawKeys()).toEqual([['device', 'zone@1']]);
      expect(store.boot.read().sub).toBe(FARAH);
    });

    it('reads the device partition alone, and records no user, when no one is signed in', async () => {
      const { store } = setUp({ cookies: '' });
      await tmWriteRawRecords(warmRecords());
      await store.ɵstart(17);
      expect(store.doc(DEVICE, 'zone')()).toBeDefined();
      expect(store.boot.read().sub).toBeNull();
    });

    it('waits for the tab bus to open before it reads, so it misses no change', async () => {
      const { store, bus } = setUp();
      let open!: () => void;
      bus.ɵopened = new Promise<void>((resolve) => (open = resolve));
      store.boot.patch({ sub: FARAH });
      const transactions = spyTransactions();
      const started = store.ɵstart(null);
      await new Promise((resolve) => setTimeout(resolve, 20));
      expect(transactions()).toEqual([]);
      open();
      await started;
      expect(transactions()).toEqual(['readonly']);
    });

    it("sweeps the user's other sessions at idle after the first render", async () => {
      const { store } = setUp();
      await tmWriteRawRecords([
        ...warmRecords(),
        { p: 'u:0b4f/ssh-0b4f', n: 'tree:/17/centers@1', tag: null, savedAt: NOW, data: [] },
      ]);
      store.boot.patch({ sub: FARAH });
      const idle = holdIdleCallbacks();
      await store.ɵstart(17);
      // Nothing is swept until the app renders and the browser is idle.
      TestBed.inject(ApplicationRef).tick();
      await vi.waitFor(() => expect(idle).toHaveLength(1));
      expect((await tmReadRawKeys()).filter(([p]) => p.startsWith('u:0b4f/s'))).toHaveLength(4);
      idle[0]!({ didTimeout: false, timeRemaining: () => 50 });
      await vi.waitFor(async () =>
        expect((await tmReadRawKeys()).filter(([p]) => p.startsWith('u:0b4f/s'))).toEqual([
          ['u:0b4f/ssh-0b4f', 'tree:/17/centers@1'],
        ]),
      );
    });
  });
});
