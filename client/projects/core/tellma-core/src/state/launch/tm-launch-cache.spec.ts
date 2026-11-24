// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterEveryRender, Component, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  TmFakeApi,
  TmFakeLocks,
  TmFakeTelemetry,
  type TmFakeApiCall,
} from '@tellma/core-testing';

import { TmApiError } from '../../api/tm-api-error';
import { TM_API_HOST } from '../../api/tm-api-host';
import { TmFakeApiHost } from '../../api/tm-api.testing.util';
import { TM_NOW } from '../../app/tm-now';
import { TM_ATTENTION_DOCUMENTS, type TmAssignmentsSummary, type TmInboxSummary } from '../../attention/tm-attention-documents';
import type { TmDecimal } from '../../json/tm-json';
import type { TmMembership } from '../../launch/tm-membership';
import { tmUnhandled } from '../../routes/tm-app.testing.util';
import { TM_LEADER_DOCUMENTS } from '../leader/tm-leader-documents';
import { TmClientStore, type TmStoredDoc } from '../store/tm-client-store';
import type { TmTenantPartition } from '../store/tm-store-layout';
import type { TmDistributionInfo } from '../tm-distribution-info';
import { TmLaunchCache } from './tm-launch-cache';
import type { TmMeResult, TmSyncDoc, TmTenantFacts, TmTenantSettingsForClient } from './tm-launch-docs';

// The launch cache over an in-memory store and a scripted API, on real
// timers: the stored sync is a minute old, so no sync waits out the throttle.

/** A page for every route. */
@Component({ template: '' })
class TmPage {}

const SUB = 'test-user';
const T17: TmTenantPartition = { kind: 'tenant', sub: SUB, tenantId: 17 };

const SETTINGS: TmTenantSettingsForClient = {
  tenantId: 17,
  names: ['Acme', null],
  languages: [{ code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false }],
  calendars: ['gc'],
  timeZone: 'Asia/Riyadh',
  entries: {},
};
const TENANT: TmTenantFacts = { id: 17, category: 'Live', state: 'Active', realtime: true };

/** An envelope whose access is current under the given tag. */
function me(permissions = 'A1'): TmMeResult {
  return {
    user: { id: 42, kind: 'Individual', state: 'Joined', name: 'Farah', modifiedAt: '2026-11-01T00:00:00.0000000+00:00' },
    preferencesTag: 'P1',
    preferences: {},
    access: { tag: permissions, formatVersion: 1, isSystem: false, securables: [], problems: [] },
    tags: { settings: 'S1', preferences: 'P1', permissions, entities: 'E1' },
  };
}

/** The answer of a cold `client/sync`. */
const COLD_ANSWER = {
  tenant: TENANT,
  tags: { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1', 'entity:acme.Customer': 'K1' },
  settings: SETTINGS,
  me: me(),
  staleEntities: [],
};

const CUSTOMERS = [
  { id: 1, name: 'Bole Traders' },
  { id: 2, name: 'Kazanchis Foods' },
];

/** What a spec drives and inspects. */
interface TmRig {
  readonly launch: TmLaunchCache;
  readonly store: TmClientStore;
  readonly api: TmFakeApi;
  readonly telemetry: TmFakeTelemetry;
  /** The idle callbacks the cache asked for, which the spec runs. */
  readonly idle: (() => void)[];
  /** The calls to one route so far. */
  calls(route: string): TmFakeApiCall[];
}

/** Runs the pending tasks, so loads started at a read land. */
async function settle(): Promise<void> {
  for (let i = 0; i < 4; i++) {
    await new Promise((resolve) => setTimeout(resolve));
    TestBed.tick();
  }
}

/** A stored document. */
function stored<T>(data: T, tag: string | null = null): TmStoredDoc<T> {
  return { tag, savedAt: Date.now() - 60_000, data };
}

/** Configures the launch cache on a URL, with tenant 17's launch documents stored unless `cold`. */
async function setUp(
  options: {
    /** The URL navigated to before the launch cache is created; `null` creates it before any navigation. */
    url?: string | null;
    cold?: boolean;
    entityTags?: Record<string, string>;
    attention?: boolean;
    /** Runs before the launch cache is created, such as to register a render hook ahead of its own. */
    beforeLaunch?: () => void;
  } = {},
): Promise<TmRig> {
  const idle: (() => void)[] = [];
  vi.spyOn(window, 'requestIdleCallback').mockImplementation((callback) => {
    idle.push(() => callback({ didTimeout: false, timeRemaining: () => 50 }));
    return idle.length;
  });
  TestBed.configureTestingModule({
    providers: [
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      provideRouter([{ path: '**', component: TmPage }]),
      { provide: TM_API_HOST, useValue: new TmFakeApiHost() },
      { provide: TM_NOW, useValue: () => Date.now() },
      // The attention documents, as provideTellma registers them, when the test reads them.
      ...(options.attention === true
        ? TM_ATTENTION_DOCUMENTS.map((document) => ({ provide: TM_LEADER_DOCUMENTS, multi: true, useValue: document }))
        : []),
    ],
  });
  if (options.url !== null) {
    await TestBed.inject(Router).navigateByUrl(options.url ?? '/17/centers');
  }
  const store = TestBed.inject(TmClientStore);
  if (options.cold !== true) {
    await store.ɵwrite(T17, {
      settings: () => stored(SETTINGS, 'S1'),
      me: () => stored(me()),
      tenant: () => stored(TENANT),
      sync: () =>
        stored<TmSyncDoc>({ at: Date.now() - 60_000, entities: 'E1', entityTags: options.entityTags ?? { 'acme.Customer': 'K1' } }),
    });
  }
  const api = TestBed.inject(TmFakeApi);
  api.respond('client/sync', COLD_ANSWER);
  api.respond('/bff/user', { tenants: [] });
  api.respond('/api/distribution-info', {});
  options.beforeLaunch?.();
  const launch = TestBed.inject(TmLaunchCache);
  return {
    launch,
    store,
    api,
    telemetry: TestBed.inject(TmFakeTelemetry),
    idle,
    calls: (route) => api.calls().filter((call) => call.route === route),
  };
}

describe('TmLaunchCache', () => {
  afterEach(() => vi.restoreAllMocks());

  describe('entering a tenant', () => {
    it('renders a cold entry after one sync, sent in the foreground', async () => {
      const rig = await setUp({ cold: true });
      expect(rig.launch.warm()).toBe(false);
      await expect(rig.launch.ɵenter(17)).resolves.toBe(true);

      expect(rig.calls('client/sync').map((call) => [call.body, (call.options as { background: boolean }).background])).toEqual([
        [{ held: {} }, false],
      ]);
      expect(rig.launch.warm()).toBe(true);
      expect(rig.launch.docs.settings()).toEqual(SETTINGS);
      expect(rig.launch.docs.me()).toEqual(me());
      expect(rig.launch.docs.tenant()).toEqual(TENANT);
    });

    it('enters a warm tenant from the store, with no sync', async () => {
      const rig = await setUp();
      await expect(rig.launch.ɵenter(17)).resolves.toBe(false);
      await settle();
      expect(rig.launch.warm()).toBe(true);
      expect(rig.calls('client/sync')).toEqual([]);
    });

    it("reads the routed tenant's inbox and assignments summaries from the store", async () => {
      const rig = await setUp({ attention: true });
      await rig.launch.ɵenter(17);
      // Nothing has read the assignments: they are absent, not an error.
      expect(rig.launch.docs.assignments()).toBeUndefined();
      const inbox: TmInboxSummary = { unseen: 2, unread: 3, latest: [], running: [], related: {}, version: '7' as TmDecimal };
      const assignments: TmAssignmentsSummary = { open: 1, new: 1, openByMe: 0, latest: [], latestByMe: [], related: {} };
      await rig.store.put(T17, 'inbox', stored(inbox));
      await rig.store.put(T17, 'assignments', stored(assignments));
      expect(rig.launch.docs.inbox()).toEqual(inbox);
      expect(rig.launch.docs.assignments()).toEqual(assignments);
    });

    it('rejects a cold entry whose sync fails, and syncs again on the next entry', async () => {
      const rig = await setUp({ cold: true });
      const failure = new TmApiError({ kind: 'offline' });
      rig.api.respond('client/sync', () => Promise.reject(failure));
      await expect(rig.launch.ɵenter(17)).rejects.toBe(failure);

      rig.api.respond('client/sync', COLD_ANSWER);
      await rig.launch.ɵenter(17);
      expect(rig.calls('client/sync')).toHaveLength(2);
      expect(rig.launch.warm()).toBe(true);
    });

    it('enters the routed tenant by itself, once', async () => {
      const rig = await setUp({ cold: true });
      await settle();
      expect(rig.calls('client/sync')).toHaveLength(1);
      await rig.launch.ɵenter(17);
      expect(rig.calls('client/sync')).toHaveLength(1);
      expect(rig.launch.warm()).toBe(true);
    });

    it('syncs once for a tenant left and entered again while its cold sync waited for the lock', async () => {
      const rig = await setUp({ cold: true });
      const router = TestBed.inject(Router);
      // Another tab holds the lock meanwhile.
      let release!: () => void;
      void TestBed.inject(TmFakeLocks).run('tm:acme:test-user:17:sync', () => new Promise<void>((resolve) => (release = resolve)));
      await settle();
      await router.navigateByUrl('/');
      await settle();
      await router.navigateByUrl('/17/centers');
      await settle();

      release();
      await rig.launch.ɵenter(17);
      expect(rig.calls('client/sync')).toHaveLength(1);
      expect(rig.launch.warm()).toBe(true);
    });

    it('checks the documents again on entering a tenant it left, so an entry after a sweep syncs', async () => {
      const rig = await setUp();
      const router = TestBed.inject(Router);
      await rig.launch.ɵenter(17);
      await settle();
      await router.navigateByUrl('/');
      await settle();
      await rig.store.sweep({ tenant: 17 });

      await router.navigateByUrl('/17/centers');
      await settle();
      await rig.launch.ɵenter(17);
      expect(rig.calls('client/sync')).toHaveLength(1);
      expect(rig.launch.warm()).toBe(true);
    });
  });

  describe('entity sets', () => {
    it('loads a list once at its first read, under the tag read before the rows', async () => {
      const rig = await setUp();
      rig.api.respond('customers/all', { entities: CUSTOMERS });
      const customers = rig.launch.entitySet<{ id: number; name: string }>('acme.Customer');
      expect(customers()).toBeUndefined();
      expect(customers()).toBeUndefined();
      await settle();

      expect(rig.calls('customers/all')).toEqual([
        {
          kind: 'read',
          route: 'customers/all',
          body: {},
          options: expect.objectContaining({ background: true, tenantId: 17 }) as unknown,
        },
      ]);
      expect(customers()).toEqual({ items: CUSTOMERS, byId: new Map([[1, CUSTOMERS[0]], [2, CUSTOMERS[1]]]), stale: false });
      expect(rig.store.doc(T17, 'entity:acme.Customer')()?.tag).toBe('K1');
      expect(rig.launch.entitySet('acme.Customer')).toBe(customers);
    });

    it('loads nothing when the app ends before the load it queued starts', async () => {
      const rig = await setUp();
      rig.api.respond('customers/all', { entities: CUSTOMERS });
      const error = vi.spyOn(rig.telemetry, 'error');
      const unhandled = await tmUnhandled(async () => {
        rig.launch.entitySet('acme.Customer')();
        TestBed.resetTestingModule();
        await new Promise((resolve) => setTimeout(resolve, 20));
      });
      expect(unhandled).toEqual([]);
      expect(error).not.toHaveBeenCalled();
      expect(rig.calls('customers/all')).toEqual([]);
    });

    it("reads no rows when the app ends while the sync for the list's tag is in flight", async () => {
      const rig = await setUp({ entityTags: {} });
      let land!: () => void;
      const landed = new Promise<void>((resolve) => (land = resolve));
      rig.api.respond('client/sync', () => landed.then(() => COLD_ANSWER));
      rig.api.respond('customers/all', { entities: CUSTOMERS });
      const error = vi.spyOn(rig.telemetry, 'error');
      rig.launch.entitySet('acme.Customer')();
      await settle();
      expect(rig.calls('client/sync')).toHaveLength(1);

      const unhandled = await tmUnhandled(async () => {
        TestBed.resetTestingModule();
        land();
        await new Promise((resolve) => setTimeout(resolve, 20));
      });
      expect(unhandled).toEqual([]);
      expect(error).not.toHaveBeenCalled();
      expect(rig.calls('customers/all')).toEqual([]);
    });

    it('stores nothing when the app ends while the rows are read', async () => {
      // The app's end cancels the read, which the cache counts as no failure.
      const rig = await setUp();
      let land!: () => void;
      const landed = new Promise<void>((resolve) => (land = resolve));
      rig.api.respond('customers/all', () => landed.then(() => ({ entities: CUSTOMERS })));
      const error = vi.spyOn(rig.telemetry, 'error');
      rig.launch.entitySet('acme.Customer')();
      await settle();
      expect(rig.calls('customers/all')).toHaveLength(1);

      const unhandled = await tmUnhandled(async () => {
        TestBed.resetTestingModule();
        land();
        await new Promise((resolve) => setTimeout(resolve, 20));
      });
      expect(unhandled).toEqual([]);
      expect(error).not.toHaveBeenCalled();
      expect(rig.store.doc(T17, 'entity:acme.Customer')()).toBeUndefined();
    });

    it('keeps serving a stale list while the newer one loads', async () => {
      const rig = await setUp({ entityTags: { 'acme.Customer': 'K2' } });
      await rig.store.put(T17, 'entity:acme.Customer', stored(CUSTOMERS.slice(0, 1), 'K1'));
      let answer!: (value: unknown) => void;
      rig.api.respond('customers/all', () => new Promise((resolve) => (answer = resolve)));
      const customers = rig.launch.entitySet('acme.Customer');
      expect(customers()).toEqual(expect.objectContaining({ items: CUSTOMERS.slice(0, 1), stale: true }));
      await settle();
      expect(customers()).toEqual(expect.objectContaining({ items: CUSTOMERS.slice(0, 1), stale: true }));

      answer({ entities: CUSTOMERS });
      await settle();
      expect(customers()).toEqual(expect.objectContaining({ items: CUSTOMERS, stale: false }));
      expect(rig.calls('customers/all')).toHaveLength(1);
    });

    it('stores nothing for a refused list, and tries again once the grants move', async () => {
      const rig = await setUp();
      rig.api.respond('customers/all', () => Promise.reject(new TmApiError({ kind: 'forbidden', status: 403 })));
      const customers = rig.launch.entitySet('acme.Customer');
      expect(customers()).toBeUndefined();
      await settle();
      expect(rig.calls('customers/all')).toHaveLength(1);

      // A sync that moves nothing the refusal depends on does not retry it.
      await rig.store.patch<TmSyncDoc>(T17, 'sync', (sync) => sync && { ...sync, at: Date.now() });
      expect(customers()).toBeUndefined();
      await settle();
      expect(rig.calls('customers/all')).toHaveLength(1);
      expect(rig.telemetry.errors()).toEqual([]);

      rig.api.respond('customers/all', { entities: CUSTOMERS });
      await rig.store.put(T17, 'me', { tag: null, savedAt: Date.now(), data: me('A2') });
      expect(customers()).toBeUndefined();
      await settle();
      expect(rig.calls('customers/all')).toHaveLength(2);
      expect(customers()?.items).toEqual(CUSTOMERS);
    });

    it("syncs first when the tab holds no tag for the list, holding no composite, so the answer reports every list's", async () => {
      const rig = await setUp({ entityTags: {} });
      rig.api.respond('customers/all', { entities: CUSTOMERS });
      rig.launch.entitySet('acme.Customer')();
      await settle();

      expect(rig.api.calls().map((call) => call.route).filter((route) => route !== '/bff/user' && route !== '/api/distribution-info')).toEqual([
        'client/sync',
        'customers/all',
      ]);
      expect(rig.calls('client/sync')[0]?.body).toEqual({ held: { settings: 'S1', preferences: 'P1', permissions: 'A1' } });
      expect(rig.store.doc(T17, 'entity:acme.Customer')()?.tag).toBe('K1');
    });

    it('loads nothing for a list the server does not cache until the lists move', async () => {
      const rig = await setUp({ entityTags: {} });
      // The answer reports no tag for the roles: the server does not cache them.
      const roles = rig.launch.entitySet('core.Role');
      expect(roles()).toBeUndefined();
      await settle();
      await rig.store.patch<TmSyncDoc>(T17, 'sync', (sync) => sync && { ...sync, at: Date.now() });
      roles();
      await settle();

      expect(rig.calls('client/sync')).toHaveLength(1);
      expect(rig.calls('roles/all')).toEqual([]);
    });

    it("costs no sync of its own for a list read while a cold sync is in flight, which reports the list's tag", async () => {
      const rig = await setUp({ cold: true });
      let answer!: (value: unknown) => void;
      rig.api.respond('client/sync', () => new Promise((resolve) => (answer = resolve)));
      rig.api.respond('customers/all', { entities: CUSTOMERS });
      // The routed tenant's entry sends the cold sync.
      await settle();
      expect(rig.calls('client/sync')).toHaveLength(1);
      const customers = rig.launch.entitySet('acme.Customer');
      customers();
      await settle();

      answer(COLD_ANSWER);
      await settle();
      expect(customers()?.items).toEqual(CUSTOMERS);
      expect(rig.calls('client/sync')).toHaveLength(1);
    });
  });

  describe('user documents', () => {
    it('reads the tenants and the distribution in the background, and stores them', async () => {
      const rig = await setUp();
      const tenants = [{ id: 17, name: 'Acme' }] as unknown as TmMembership[];
      const distribution = { slug: 'acme', displayName: 'Acme' } as unknown as TmDistributionInfo;
      rig.api.respond('/bff/user', { tenants, name: 'Farah' });
      rig.api.respond('/api/distribution-info', distribution);
      await rig.launch.revalidateUser();

      expect(rig.launch.tenants()).toEqual(tenants);
      expect(rig.launch.distribution()).toEqual(distribution);
      for (const route of ['/bff/user', '/api/distribution-info']) {
        expect(rig.calls(route)[0]?.options).toEqual(expect.objectContaining({ method: 'GET', background: true }));
      }
    });

    it('reads the user documents at idle after the first render, unless this launch has', async () => {
      const rig = await setUp();
      await rig.launch.revalidateUser();
      TestBed.tick();
      expect(rig.idle).toHaveLength(1);
      rig.idle[0]!();
      await settle();
      expect(rig.calls('/bff/user')).toHaveLength(1);
      expect(rig.calls('/api/distribution-info')).toHaveLength(1);
    });

    it('reads the tenants again on a tenantless route', async () => {
      const rig = await setUp({ url: '/' });
      await settle();
      expect(rig.calls('/bff/user')).toHaveLength(1);
      expect(rig.calls('/api/distribution-info')).toEqual([]);
      expect(rig.calls('client/sync')).toEqual([]);
    });

    it("reads a launch's tenants on a tenantless route only after the render that holds its first page", async () => {
      // A render hook registered ahead of the cache's own sees, at each render, the reads made before it.
      const renders: { navigated: boolean; reads: number }[] = [];
      const rig = await setUp({
        url: null,
        beforeLaunch: () => {
          const router = TestBed.inject(Router);
          const api = TestBed.inject(TmFakeApi);
          afterEveryRender(
            () => renders.push({ navigated: router.navigated, reads: api.calls().filter((call) => call.route === '/bff/user').length }),
            { injector: TestBed.inject(Injector) },
          );
        },
      });

      // The app's root view renders while the launch's first page still loads: no read.
      const harness = await RouterTestingHarness.create();
      await settle();
      expect(renders.length).toBeGreaterThan(0);
      expect(renders.every((render) => !render.navigated)).toBe(true);
      expect(rig.calls('/bff/user')).toEqual([]);

      // The render once the first navigation has ended holds the page; the read follows it.
      await harness.navigateByUrl('/tenants');
      await settle();
      expect(renders.find((render) => render.navigated)).toEqual({ navigated: true, reads: 0 });
      expect(rig.calls('/bff/user')).toHaveLength(1);
    });

    it('reads the tenants at once on a later tenantless entry', async () => {
      const rig = await setUp({ url: '/' });
      await settle();
      await TestBed.inject(Router).navigateByUrl('/17/centers');
      await settle();
      const before = rig.calls('/bff/user').length;

      await TestBed.inject(Router).navigateByUrl('/tenants');
      TestBed.tick();

      expect(rig.calls('/bff/user')).toHaveLength(before + 1);
    });
  });
});
