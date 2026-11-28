// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  TmFakeApi,
  TmFakeTabBus,
  TmFakeTelemetry,
  type TmFakeApiCall,
} from '@tellma/core-testing';

import { TmApiError } from '../../api/tm-api-error';
import { TM_API_HOST } from '../../api/tm-api-host';
import { TmFakeApiHost } from '../../api/tm-api.testing.util';
import { TM_APP_DECLARATIONS, type TmAppDeclarations } from '../../app/tm-app-declarations';
import { TM_NOW } from '../../app/tm-now';
import type { TmBusMessage } from '../bus/tm-bus-messages';
import type { TmMeResult, TmPreferencesResult, TmTenantFacts, TmTenantSettingsForClient } from '../launch/tm-launch-docs';
import { tmUnhandled } from '../../routes/tm-app.testing.util';
import { TmClientStore, type TmStoredDoc } from '../store/tm-client-store';
import type { TmTenantPartition } from '../store/tm-store-layout';
import { tmPreferenceKey } from './tm-preference-key';
import { TmPreferences } from './tm-preferences';

// The preferences over an in-memory store, a scripted server that keeps a
// bag, a recording tab bus, a faked page and faked timers.

/** A page for every route. */
@Component({ template: '' })
class TmPage {}

const NOW = 1_700_000_000_000;
const SUB = 'test-user';
const T17: TmTenantPartition = { kind: 'tenant', sub: SUB, tenantId: 17 };

const BAR = tmPreferenceKey<boolean>('nav.pinnedBarVisible', 'curated', true);
const PINNED = tmPreferenceKey<readonly number[]>('nav.pinned', 'curated', []);
const TOASTS = tmPreferenceKey<boolean>('notifications.toasts', 'curated', true);
const VIEW = tmPreferenceKey<{ wide?: string }>('search.gl.Center.view', 'write-behind', {});
const SIZE = tmPreferenceKey<number>(
  'search.gl.Center.pageSize',
  'write-behind',
  50,
  (value): value is number => value === 20 || value === 50 || value === 100,
);
const RECENT = tmPreferenceKey<readonly string[]>('nav.recent', 'write-behind', []);
const THRESHOLD = tmPreferenceKey<number>('acme.creditReview.threshold', 'write-behind', 90);

const SETTINGS: TmTenantSettingsForClient = {
  tenantId: 17,
  names: ['Acme'],
  languages: [{ code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false }],
  calendars: ['gc'],
  timeZone: 'Asia/Riyadh',
  entries: {},
};

/** An envelope with the given bag. */
function me(bag: Readonly<Record<string, string>>, tag = 'P1'): TmMeResult {
  return {
    user: { id: 42, kind: 'Individual', state: 'Joined', name: 'Farah', modifiedAt: '2026-11-01T00:00:00.0000000+00:00' },
    preferencesTag: tag,
    preferences: bag,
    access: { tag: 'A1', formatVersion: 1, isSystem: false, securables: [], problems: [] },
    tags: { settings: 'S1', preferences: tag, permissions: 'A1', entities: 'E1' },
  };
}

/** What a spec drives and inspects. */
interface TmRig {
  readonly prefs: TmPreferences;
  readonly store: TmClientStore;
  readonly api: TmFakeApi;
  readonly bus: TmFakeTabBus;
  readonly host: TmFakeApiHost;
  readonly telemetry: TmFakeTelemetry;
  /** The server's bag. */
  readonly bag: Record<string, string>;
  /** The preference writes and sends so far. */
  writes(): TmFakeApiCall[];
  /** The routed tenant's stored envelope. */
  me(): TmMeResult | undefined;
  /** Stores tenant 17's state. */
  state(state: TmTenantFacts['state']): Promise<void>;
  /** Stores tenant 17's envelope with a bag, as a sync does. */
  sync(bag: Readonly<Record<string, string>>): Promise<void>;
  /** Plays another tab's `prefs.changed`. */
  receive(values: Readonly<Record<string, string | null>>): void;
  /** The posts of `prefs.changed`. */
  posted(): TmBusMessage[];
  /** The telemetry items of one name. */
  items(name: string): unknown[];
}

/** Runs the pending microtasks, the effects and the timers due now. */
async function flush(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await vi.advanceTimersByTimeAsync(0);
    TestBed.tick();
  }
}

/** How {@link setUp} starts. */
interface TmSetUp {
  /** The stored bag. */
  readonly bag?: Readonly<Record<string, string>>;
  /** The stored tenant state. */
  readonly state?: TmTenantFacts['state'];
  /** The route the tab starts on. */
  readonly url?: string;
  /** The composed features, which the development check reads. */
  readonly features?: readonly string[];
}

/** Configures the preferences on `/17/centers`, with tenant 17's documents stored. */
async function setUp(options: TmSetUp = {}): Promise<TmRig> {
  const host = new TmFakeApiHost();
  TestBed.configureTestingModule({
    providers: [
      provideTellmaTesting(),
      provideTellmaStateTesting(),
      provideRouter([{ path: '**', component: TmPage }]),
      { provide: TM_API_HOST, useValue: host },
      { provide: TM_NOW, useValue: () => Date.now() },
      ...(options.features
        ? [{ provide: TM_APP_DECLARATIONS, useValue: { features: options.features } as unknown as TmAppDeclarations }]
        : []),
    ],
  });
  await TestBed.inject(Router).navigateByUrl(options.url ?? '/17/centers');
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  vi.setSystemTime(NOW);
  const store = TestBed.inject(TmClientStore);
  const stored = <T>(data: T, tag: string | null = null): TmStoredDoc<T> => ({ tag, savedAt: NOW - 60_000, data });
  const bag: Record<string, string> = { ...options.bag };
  await store.ɵwrite(T17, {
    settings: () => stored(SETTINGS, 'S1'),
    me: () => stored(me(bag)),
    tenant: () => stored<TmTenantFacts>({ id: 17, category: 'Live', state: options.state ?? 'Active', realtime: true }),
    sync: () => stored({ at: NOW, entities: 'E1', entityTags: {} }),
  });
  // The server: each write changes the bag and answers it whole.
  const api = TestBed.inject(TmFakeApi);
  let version = 1;
  const answer = (): TmPreferencesResult => ({ preferences: { ...bag }, preferencesTag: `P${++version}` });
  api.respond('users/me/preferences/set', (call: TmFakeApiCall) => {
    for (const { key, value } of (call.body as { items: { key: string; value: string }[] }).items) {
      bag[key] = value;
    }
    return answer();
  });
  api.respond('users/me/preferences/delete', (call: TmFakeApiCall) => {
    for (const key of (call.body as { keys: string[] }).keys) {
      delete bag[key];
    }
    return answer();
  });
  const prefs = TestBed.inject(TmPreferences);
  const bus = TestBed.inject(TmFakeTabBus);
  const telemetry = TestBed.inject(TmFakeTelemetry);
  await flush();
  return {
    prefs,
    store,
    api,
    bus,
    host,
    telemetry,
    bag,
    writes: () => api.calls().filter((call) => call.route.startsWith('users/me/preferences/')),
    me: () => store.doc<TmMeResult>(T17, 'me')()?.data,
    state: async (state) => {
      await store.ɵwrite(T17, { tenant: () => stored<TmTenantFacts>({ id: 17, category: 'Live', state, realtime: true }) });
      await flush();
    },
    sync: async (synced) => {
      await store.ɵwrite(T17, { me: () => stored(me(synced, 'P9')) });
      await flush();
    },
    receive: (values) => bus.receive({ type: 'prefs.changed', tenantId: 17, values }),
    posted: () => bus.posted().filter((message) => message.type === 'prefs.changed'),
    items: (name) =>
      telemetry
        .recent()
        .filter((item) => item.name === name)
        .map((item) => item.props),
  };
}

/** A failed write, as the HTTP layer rejects one. */
function failure(kind: TmApiError['kind'], errors?: readonly { code: string; path: string }[]): TmApiError {
  return new TmApiError({
    kind,
    status: 422,
    ...(errors && { problem: { code: 'validation', instance: 't', status: 422, errors: errors.map((error) => ({ ...error, message: 'x' })) } }),
  });
}

describe('TmPreferences', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe('reading', () => {
    it('reads the overlay first, then the stored bag, then the fallback', async () => {
      const rig = await setUp({ bag: { 'nav.pinnedBarVisible': 'false' } });
      const bar = rig.prefs.get(BAR);
      const toasts = rig.prefs.get(TOASTS);
      expect([bar(), toasts()]).toEqual([false, true]);
      rig.receive({ 'nav.pinnedBarVisible': 'true', 'notifications.toasts': 'false' });
      expect([bar(), toasts()]).toEqual([true, false]);
    });

    it('parses each distinct stored string once, sharing the value', async () => {
      const rig = await setUp({ bag: { 'search.gl.Center.view': '{"wide":"tiles"}' } });
      const one = rig.prefs.get(VIEW);
      const two = rig.prefs.get(VIEW);
      expect(one()).toEqual({ wide: 'tiles' });
      expect(two()).toBe(one());
    });

    it('reads an invalid stored value as the fallback and reports its family once', async () => {
      const rig = await setUp({ bag: { 'search.gl.Center.view': '{not json', 'search.gl.Center.pageSize': '30' } });
      expect(rig.prefs.get(VIEW)()).toEqual({});
      expect(rig.prefs.get(VIEW)()).toEqual({});
      expect(rig.prefs.get(SIZE)()).toBe(50);
      expect(rig.items('tm.prefs.invalid')).toEqual([{ family: 'search' }, { family: 'search' }]);
    });

    it('reads the fallback and ignores a set outside a tenant', async () => {
      const rig = await setUp({ url: '/tenants', bag: { 'nav.pinnedBarVisible': 'false' } });
      rig.prefs.set(BAR, false);
      await flush();
      expect(rig.prefs.get(BAR)()).toBe(true);
      expect(rig.posted()).toEqual([]);
      expect(rig.writes()).toEqual([]);
    });
  });

  describe('setting', () => {
    it('changes get() in the same frame and posts the stored string to the other tabs', async () => {
      const rig = await setUp();
      const bar = rig.prefs.get(BAR);
      expect(bar()).toBe(true);
      rig.prefs.set(BAR, false);
      expect(bar()).toBe(false);
      expect(rig.posted()).toEqual([{ type: 'prefs.changed', tenantId: 17, values: { 'nav.pinnedBarVisible': 'false' } }]);
      // The store holds server truth only.
      expect(rig.me()?.preferences).toEqual({});
    });

    it('deletes the key for a value deep-equal to the fallback', async () => {
      const rig = await setUp({ bag: { 'search.gl.Center.view': '{"wide":"tiles"}' } });
      rig.prefs.set(VIEW, {});
      expect(rig.posted()).toEqual([{ type: 'prefs.changed', tenantId: 17, values: { 'search.gl.Center.view': null } }]);
      await vi.advanceTimersByTimeAsync(2_000);
      await flush();
      expect(rig.writes().map((call) => [call.route, call.body])).toEqual([
        ['users/me/preferences/delete', { keys: ['search.gl.Center.view'] }],
      ]);
      expect(rig.me()?.preferences).toEqual({});
    });

    it('resets a key to its fallback', async () => {
      const rig = await setUp({ bag: { 'nav.pinnedBarVisible': 'false' } });
      rig.prefs.reset(BAR);
      expect(rig.prefs.get(BAR)()).toBe(true);
      await flush();
      expect(rig.writes().map((call) => call.route)).toEqual(['users/me/preferences/delete']);
    });

    it('does nothing for a value it already shows', async () => {
      const rig = await setUp({ bag: { 'nav.pinnedBarVisible': 'false' } });
      rig.prefs.set(BAR, false);
      await flush();
      expect(rig.posted()).toEqual([]);
      expect(rig.writes()).toEqual([]);
    });

    it('refuses a stored form over 32 KB before it applies, and throws in development', async () => {
      const rig = await setUp();
      const big = { wide: 'x'.repeat(33_000) };
      expect(() => rig.prefs.set(VIEW, big)).toThrow(/stores 33011 bytes/);
      expect(rig.prefs.get(VIEW)()).toEqual({});
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'too-large' }]);
      expect(rig.posted()).toEqual([]);
    });

    it('refuses, in development, a key whose first segment is neither a platform segment nor a feature key', async () => {
      const rig = await setUp({ features: ['core', 'acme'] });
      expect(() => rig.prefs.get(tmPreferenceKey('contoso.view', 'curated', 0))).toThrow(/'contoso'/);
      expect(() => rig.prefs.set(THRESHOLD, 80)).not.toThrow();
      expect(() => rig.prefs.get(BAR)).not.toThrow();
    });
  });

  describe('flushing', () => {
    it('flushes curated keys at once, coalescing the sets of one task', async () => {
      const rig = await setUp();
      rig.prefs.set(BAR, false);
      rig.prefs.set(TOASTS, false);
      await flush();
      expect(rig.writes().map((call) => [call.route, call.body, call.options])).toEqual([
        [
          'users/me/preferences/set',
          {
            items: [
              { key: 'nav.pinnedBarVisible', value: 'false' },
              { key: 'notifications.toasts', value: 'false' },
            ],
          },
          { tenantId: 17 },
        ],
      ]);
      expect(rig.items('tm.prefs.flush')).toEqual([expect.objectContaining({ kind: 'curated', keys: '2-5' })]);
    });

    it('flushes a write-behind key 2 s after its last change, then at most once per 10 s', async () => {
      const rig = await setUp();
      rig.prefs.set(SIZE, 20);
      await vi.advanceTimersByTimeAsync(1_500);
      rig.prefs.set(SIZE, 100);
      await vi.advanceTimersByTimeAsync(1_900);
      expect(rig.writes()).toEqual([]);
      await vi.advanceTimersByTimeAsync(100);
      await flush();
      expect(rig.writes().map((call) => call.body)).toEqual([{ items: [{ key: 'search.gl.Center.pageSize', value: '100' }] }]);

      // The next change waits for the 10 s since that flush.
      rig.prefs.set(SIZE, 20);
      await vi.advanceTimersByTimeAsync(9_000);
      expect(rig.writes()).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(1_000);
      await flush();
      expect(rig.writes()).toHaveLength(2);
      expect(rig.bag['search.gl.Center.pageSize']).toBe('20');
      expect(rig.items('tm.prefs.flush')).toEqual([
        expect.objectContaining({ kind: 'write-behind', keys: '1' }),
        expect.objectContaining({ kind: 'write-behind', keys: '1' }),
      ]);
    });

    it('flushes a write-behind key at once when its stored form exceeds 8 KB', async () => {
      const rig = await setUp();
      rig.prefs.set(VIEW, { wide: 'x'.repeat(9_000) });
      await flush();
      expect(rig.writes()).toHaveLength(1);
    });

    it('flushes the write-behind keys at once when the page hides', async () => {
      const rig = await setUp();
      rig.prefs.set(SIZE, 20);
      rig.host.visibleNow = false;
      rig.host.fire('document', 'visibilitychange');
      await flush();
      expect(rig.writes().map((call) => call.body)).toEqual([{ items: [{ key: 'search.gl.Center.pageSize', value: '20' }] }]);
    });

    it("spaces nav.recent's flushes 5 min apart, the hidden flush included", async () => {
      const rig = await setUp();
      rig.prefs.set(RECENT, ['centers']);
      await vi.advanceTimersByTimeAsync(2_000);
      await flush();
      expect(rig.writes()).toHaveLength(1);

      rig.prefs.set(RECENT, ['agents', 'centers']);
      await vi.advanceTimersByTimeAsync(60_000);
      rig.host.visibleNow = false;
      rig.host.fire('document', 'visibilitychange');
      await flush();
      expect(rig.writes()).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(240_000);
      await flush();
      expect(rig.writes()).toHaveLength(2);
      expect(rig.bag['nav.recent']).toBe('["agents","centers"]');
    });

    it('adopts the result: the bag and its tag are stored, and the key is no longer dirty', async () => {
      const rig = await setUp();
      rig.prefs.set(BAR, false);
      await flush();
      expect(rig.me()?.preferences).toEqual({ 'nav.pinnedBarVisible': 'false' });
      expect(rig.me()?.preferencesTag).toBe('P2');
      expect(rig.prefs.ɵdirty()).toEqual([]);
    });

    it("ends the overlay entry once the bag holds its string, so a later change of the bag shows", async () => {
      const rig = await setUp();
      const bar = rig.prefs.get(BAR);
      rig.prefs.set(BAR, false);
      await flush();
      // An administrator's later write reaches the tab through a sync.
      await rig.sync({ 'nav.pinnedBarVisible': 'true' });
      expect(bar()).toBe(true);
    });

    it("drops this tab's unsent value when another tab's value arrives: only the later choice's tab flushes", async () => {
      const rig = await setUp();
      const size = rig.prefs.get(SIZE);
      rig.prefs.set(SIZE, 20);
      rig.receive({ 'search.gl.Center.pageSize': '100' });
      expect(size()).toBe(100);
      await vi.advanceTimersByTimeAsync(20_000);
      await flush();
      expect(rig.writes()).toEqual([]);
      expect(rig.prefs.ɵdirty()).toEqual([]);
    });

    it("saves another tab's value again when it arrives while this tab's write is in flight", async () => {
      const rig = await setUp();
      let land!: () => void;
      const landed = new Promise<void>((resolve) => (land = resolve));
      rig.api.respond('users/me/preferences/set', async (call: TmFakeApiCall) => {
        await landed;
        for (const { key, value } of (call.body as { items: { key: string; value: string }[] }).items) {
          rig.bag[key] = value;
        }
        return { preferences: { ...rig.bag }, preferencesTag: `P${rig.writes().length + 1}` };
      });
      rig.prefs.set(BAR, false);
      await flush();
      rig.receive({ 'nav.pinnedBarVisible': 'true' });
      land();
      await flush();
      expect(rig.writes().map((call) => call.body)).toEqual([
        { items: [{ key: 'nav.pinnedBarVisible', value: 'false' }] },
        { items: [{ key: 'nav.pinnedBarVisible', value: 'true' }] },
      ]);
      expect(rig.prefs.get(BAR)()).toBe(true);
      expect(rig.bag['nav.pinnedBarVisible']).toBe('true');
    });
  });

  describe('failures', () => {
    it('drops the write at the key cap, and every tab returns to the stored strings', async () => {
      const rig = await setUp({ bag: { 'nav.pinned': '[1]' } });
      rig.api.respond('users/me/preferences/set', () => {
        throw failure('validation', [{ code: 'Users.TooManyPreferenceKeys', path: '' }]);
      });
      const pinned = rig.prefs.get(PINNED);
      const toasts = rig.prefs.get(TOASTS);
      rig.prefs.set(PINNED, [1, 2]);
      rig.prefs.set(TOASTS, false);
      expect([pinned(), toasts()]).toEqual([[1, 2], false]);
      await flush();
      expect([pinned(), toasts()]).toEqual([[1], true]);
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'key-cap' }]);
      expect(rig.items('tm.prefs.flush-failed')).toEqual([{ kind: 'validation' }]);
      // The other tabs hear the refused values as a revert, never as a later choice.
      expect(rig.posted().at(-1)).toEqual({
        type: 'prefs.changed',
        tenantId: 17,
        values: { 'nav.pinned': '[1,2]', 'notifications.toasts': 'false' },
        revert: true,
      });
      expect(rig.prefs.ɵdirty()).toEqual([]);
    });

    it.each([
      ['the key cap', () => failure('validation', [{ code: 'Users.TooManyPreferenceKeys', path: '' }])],
      ['a kind it has no rule for', () => new TmApiError({ kind: 'forbidden', status: 403 })],
    ])('keeps a value set while a write refused for %s was in flight, and saves it again', async (_, refusal) => {
      const rig = await setUp();
      let refuse!: () => void;
      let calls = 0;
      rig.api.respond('users/me/preferences/set', (call: TmFakeApiCall) => {
        if (++calls === 1) {
          return new Promise((_, reject) => (refuse = () => reject(refusal())));
        }
        const items = (call.body as { items: { key: string; value: string }[] }).items;
        return { preferences: Object.fromEntries(items.map((item) => [item.key, item.value])), preferencesTag: 'P2' };
      });
      const pinned = rig.prefs.get(PINNED);
      rig.prefs.set(PINNED, [1]);
      await flush();
      rig.prefs.set(PINNED, [1, 2]);
      const posts = rig.posted().length;
      refuse();
      await flush();
      expect(rig.writes().map((call) => call.body)).toEqual([
        { items: [{ key: 'nav.pinned', value: '[1]' }] },
        { items: [{ key: 'nav.pinned', value: '[1,2]' }] },
      ]);
      expect(pinned()).toEqual([1, 2]);
      expect(rig.prefs.ɵdirty()).toEqual([]);
      // Nothing was dropped, so nothing was taken back from the other tabs.
      expect(rig.items('tm.prefs.dropped')).toEqual([]);
      expect(rig.posted()).toHaveLength(posts);
    });

    it("never keeps another tab's revert as a later choice, and takes back only the refused value it shows", async () => {
      const rig = await setUp();
      let land!: () => void;
      rig.api.respond('users/me/preferences/set', (call: TmFakeApiCall) => {
        const items = (call.body as { items: { key: string; value: string }[] }).items;
        const result = { preferences: Object.fromEntries(items.map((item) => [item.key, item.value])), preferencesTag: 'P2' };
        return new Promise((resolve) => (land = () => resolve(result)));
      });
      const pinned = rig.prefs.get(PINNED);
      const toasts = rig.prefs.get(TOASTS);
      rig.prefs.set(PINNED, [1]);
      await flush();
      // Another tab's write of the same key was refused while this tab's is in flight.
      rig.bus.receive({ type: 'prefs.changed', tenantId: 17, values: { 'nav.pinned': '[9]' }, revert: true });
      expect(pinned()).toEqual([1]);
      land();
      await flush();
      expect(rig.writes()).toHaveLength(1);
      expect(rig.prefs.ɵdirty()).toEqual([]);
      expect(pinned()).toEqual([1]);

      // A refused value this tab shows goes; one it does not show changes nothing.
      rig.receive({ 'notifications.toasts': 'false' });
      expect(toasts()).toBe(false);
      rig.bus.receive({ type: 'prefs.changed', tenantId: 17, values: { 'notifications.toasts': 'true' }, revert: true });
      expect(toasts()).toBe(false);
      rig.bus.receive({ type: 'prefs.changed', tenantId: 17, values: { 'notifications.toasts': 'false' }, revert: true });
      expect(toasts()).toBe(true);
    });

    it('drops only the keys another validation code names, and saves the rest again', async () => {
      const rig = await setUp();
      let calls = 0;
      rig.api.respond('users/me/preferences/set', (call: TmFakeApiCall) => {
        if (++calls === 1) {
          throw failure('validation', [{ code: 'Users.PreferenceKeyInvalid', path: 'items[1].key' }]);
        }
        return { preferences: Object.fromEntries((call.body as { items: { key: string; value: string }[] }).items.map((item) => [item.key, item.value])), preferencesTag: 'P2' };
      });
      rig.prefs.set(BAR, false);
      rig.prefs.set(TOASTS, false);
      await flush();
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'rejected' }]);
      expect(rig.writes().map((call) => call.body)).toEqual([
        {
          items: [
            { key: 'nav.pinnedBarVisible', value: 'false' },
            { key: 'notifications.toasts', value: 'false' },
          ],
        },
        { items: [{ key: 'nav.pinnedBarVisible', value: 'false' }] },
      ]);
      expect(rig.prefs.get(TOASTS)()).toBe(true);
    });

    it('drops a write a frozen tenant refuses, silently, with read-only', async () => {
      const rig = await setUp();
      rig.api.respond('users/me/preferences/set', () => {
        throw new TmApiError({ kind: 'read-only', status: 403, code: 'tenant-frozen' });
      });
      const bar = rig.prefs.get(BAR);
      rig.prefs.set(BAR, false);
      await flush();
      expect(bar()).toBe(true);
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'read-only' }]);
      await vi.advanceTimersByTimeAsync(60_000);
      expect(rig.writes()).toHaveLength(1);
    });

    it('keeps the keys after a transient failure, and retries after 10 s or once the server is back', async () => {
      const rig = await setUp();
      let fail = true;
      rig.api.respond('users/me/preferences/set', () => {
        if (fail) {
          throw new TmApiError({ kind: 'offline' });
        }
        return { preferences: { 'nav.pinnedBarVisible': 'false' }, preferencesTag: 'P2' };
      });
      rig.prefs.set(BAR, false);
      await flush();
      expect(rig.items('tm.prefs.flush-failed')).toEqual([{ kind: 'offline' }]);
      await vi.advanceTimersByTimeAsync(9_000);
      expect(rig.writes()).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(1_000);
      await flush();
      expect(rig.writes()).toHaveLength(2);

      // Offline again: the server's return retries at once.
      rig.api.setOnline(false);
      await flush();
      rig.api.setOnline(true);
      fail = false;
      await flush();
      expect(rig.writes()).toHaveLength(3);
      expect(rig.prefs.ɵdirty()).toEqual([]);
      expect(rig.prefs.get(BAR)()).toBe(false);
    });

    it('retries a cancelled write as a transient failure, after 10 s', async () => {
      const rig = await setUp();
      let calls = 0;
      rig.api.respond('users/me/preferences/set', () => {
        if (++calls === 1) {
          throw new TmApiError({ kind: 'cancelled' });
        }
        return { preferences: { 'nav.pinnedBarVisible': 'false' }, preferencesTag: 'P2' };
      });
      rig.prefs.set(BAR, false);
      await flush();
      expect(rig.items('tm.prefs.flush-failed')).toEqual([{ kind: 'cancelled' }]);
      expect(rig.prefs.ɵdirty()).toEqual(['nav.pinnedBarVisible']);
      await vi.advanceTimersByTimeAsync(10_000);
      await flush();
      expect(rig.writes()).toHaveLength(2);
      expect(rig.prefs.ɵdirty()).toEqual([]);
      expect(rig.items('tm.prefs.dropped')).toEqual([]);
    });

    it.each(['unauthenticated', 'step-up', 'client-outdated'] as const)(
      'keeps the keys after %s without retrying them, until the next set of the key or the server is back',
      async (kind) => {
        const rig = await setUp();
        let fail = true;
        rig.api.respond('users/me/preferences/set', (call: TmFakeApiCall) => {
          if (fail) {
            throw new TmApiError({ kind });
          }
          const items = (call.body as { items: { key: string; value: string }[] }).items;
          return { preferences: Object.fromEntries(items.map((item) => [item.key, item.value])), preferencesTag: 'P2' };
        });
        const pinned = rig.prefs.get(PINNED);
        rig.prefs.set(PINNED, [1]);
        await flush();
        expect(rig.items('tm.prefs.flush-failed')).toEqual([{ kind }]);
        await vi.advanceTimersByTimeAsync(60_000);
        await flush();
        expect(rig.writes()).toHaveLength(1);
        expect(rig.prefs.ɵdirty()).toEqual(['nav.pinned']);
        expect(pinned()).toEqual([1]);
        expect(rig.items('tm.prefs.dropped')).toEqual([]);

        // The next set of the key sends it again.
        rig.prefs.set(PINNED, [1, 2]);
        await flush();
        expect(rig.writes()).toHaveLength(2);

        // So does the server's return.
        rig.api.setOnline(false);
        await flush();
        fail = false;
        rig.api.setOnline(true);
        await flush();
        expect(rig.writes()).toHaveLength(3);
        expect(rig.prefs.ɵdirty()).toEqual([]);
        expect(pinned()).toEqual([1, 2]);
      },
    );

    it('drops a write that fails with a kind it has no rule for as rejected', async () => {
      const rig = await setUp();
      rig.api.respond('users/me/preferences/set', () => {
        throw new TmApiError({ kind: 'forbidden', status: 403 });
      });
      const bar = rig.prefs.get(BAR);
      rig.prefs.set(BAR, false);
      await flush();
      expect(bar()).toBe(true);
      expect(rig.items('tm.prefs.flush-failed')).toEqual([{ kind: 'forbidden' }]);
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'rejected' }]);
      expect(rig.prefs.ɵdirty()).toEqual([]);
      await vi.advanceTimersByTimeAsync(60_000);
      expect(rig.writes()).toHaveLength(1);
    });

    it("drops only the key a delete's validation error names by its keys[i] path, and deletes the rest again", async () => {
      const rig = await setUp({ bag: { 'nav.pinnedBarVisible': 'false', 'notifications.toasts': 'false' } });
      let calls = 0;
      rig.api.respond('users/me/preferences/delete', (call: TmFakeApiCall) => {
        if (++calls === 1) {
          throw failure('validation', [{ code: 'Users.PreferenceKeyInvalid', path: 'keys[1]' }]);
        }
        const keys = (call.body as { keys: string[] }).keys;
        keys.forEach((key) => delete rig.bag[key]);
        return { preferences: { ...rig.bag }, preferencesTag: 'P2' };
      });
      const bar = rig.prefs.get(BAR);
      const toasts = rig.prefs.get(TOASTS);
      rig.prefs.set(BAR, true);
      rig.prefs.set(TOASTS, true);
      await flush();
      expect(rig.writes().map((call) => call.body)).toEqual([
        { keys: ['nav.pinnedBarVisible', 'notifications.toasts'] },
        { keys: ['nav.pinnedBarVisible'] },
      ]);
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'rejected' }]);
      // The dropped key shows the stored string again; the other is deleted.
      expect([bar(), toasts()]).toEqual([true, false]);
      expect(rig.prefs.ɵdirty()).toEqual([]);
    });

    it('holds every flush while the tenant is frozen, then flushes once a sync reports it active', async () => {
      const rig = await setUp({ state: 'Frozen' });
      rig.prefs.set(BAR, false);
      expect(rig.prefs.get(BAR)()).toBe(false);
      expect(rig.posted()).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(60_000);
      await flush();
      rig.host.fire('window', 'pagehide');
      expect(rig.writes()).toEqual([]);
      await rig.state('Active');
      expect(rig.writes().map((call) => call.route)).toEqual(['users/me/preferences/set']);
      expect(rig.bag['nav.pinnedBarVisible']).toBe('false');
    });
  });

  describe("the app's end", () => {
    it('sends nothing when the app ends with a flush queued for the end of its task', async () => {
      const rig = await setUp();
      const unhandled = await tmUnhandled(async () => {
        rig.prefs.set(BAR, false);
        TestBed.resetTestingModule();
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(unhandled).toEqual([]);
      expect(rig.writes()).toEqual([]);
    });

    it('arms nothing when a write lands after the app has ended', async () => {
      const rig = await setUp();
      // The server answers once the app has ended.
      let land!: () => void;
      const landed = new Promise<void>((resolve) => (land = resolve));
      rig.api.respond('users/me/preferences/set', () => landed.then(() => ({ preferences: { 'nav.pinnedBarVisible': 'false' }, preferencesTag: 'P2' })));
      rig.prefs.set(BAR, false);
      await flush();
      expect(rig.writes()).toHaveLength(1);

      const unhandled = await tmUnhandled(async () => {
        TestBed.resetTestingModule();
        land();
        await vi.advanceTimersByTimeAsync(60_000);
      });
      expect(unhandled).toEqual([]);
      expect(rig.writes()).toHaveLength(1);
      // The late result is dropped quietly: it is neither a failure nor an error.
      expect(rig.telemetry.errors()).toEqual([]);
      expect(rig.items('tm.prefs.flush-failed')).toEqual([]);
    });
  });

  describe('pagehide', () => {
    it('sends every dirty and in-flight key, newest first, to both routes', async () => {
      const rig = await setUp({ bag: { 'search.gl.Center.view': '{"wide":"tiles"}' } });
      rig.api.respond('users/me/preferences/set', () => new Promise(() => undefined));
      rig.prefs.set(BAR, false);
      await flush();
      await vi.advanceTimersByTimeAsync(1_000);
      rig.prefs.set(SIZE, 20);
      await vi.advanceTimersByTimeAsync(1_000);
      rig.prefs.set(VIEW, {});
      rig.host.fire('window', 'pagehide');
      const sends = rig.writes().filter((call) => call.kind === 'send');
      expect(sends.map((call) => [call.route, call.body, call.options])).toEqual([
        [
          'users/me/preferences/set',
          {
            items: [
              { key: 'search.gl.Center.pageSize', value: '20' },
              { key: 'nav.pinnedBarVisible', value: 'false' },
            ],
          },
          { tenantId: 17 },
        ],
        ['users/me/preferences/delete', { keys: ['search.gl.Center.view'] }, { tenantId: 17 }],
      ]);
    });

    it("sends each tenant's keys to that tenant, all of them under one 16 KB cut", async () => {
      const rig = await setUp();
      const T18: TmTenantPartition = { kind: 'tenant', sub: SUB, tenantId: 18 };
      const stored = <T>(data: T, tag: string | null = null): TmStoredDoc<T> => ({ tag, savedAt: NOW - 60_000, data });
      await rig.store.ɵwrite(T18, {
        settings: () => stored({ ...SETTINGS, tenantId: 18 }, 'S1'),
        me: () => stored(me({})),
        tenant: () => stored<TmTenantFacts>({ id: 18, category: 'Live', state: 'Active', realtime: true }),
        sync: () => stored({ at: NOW, entities: 'E1', entityTags: {} }),
      });
      const agent = tmPreferenceKey('search.gl.Agent.view', 'write-behind', {});
      const unit = tmPreferenceKey('search.gl.Unit.view', 'write-behind', {});
      // Tenant 17's keys first, then tenant 18's, each newer than the last.
      rig.prefs.set(VIEW, { wide: 'x'.repeat(7_000) });
      await vi.advanceTimersByTimeAsync(100);
      rig.prefs.set(THRESHOLD, 80);
      await vi.advanceTimersByTimeAsync(100);
      await TestBed.inject(Router).navigateByUrl('/18/centers');
      await flush();
      rig.prefs.set(agent, { wide: 'y'.repeat(7_000) });
      await vi.advanceTimersByTimeAsync(100);
      rig.prefs.set(unit, { wide: 'z'.repeat(7_000) });
      rig.host.fire('window', 'pagehide');
      const sends = rig.writes().filter((call) => call.kind === 'send');
      expect(sends.map((call) => [(call.body as { items: { key: string }[] }).items.map((item) => item.key), call.options])).toEqual([
        [['search.gl.Unit.view', 'search.gl.Agent.view'], { tenantId: 18 }],
        // Tenant 17's view would fit under a cut of its own; under the shared one it is the oldest, and drops.
        [['acme.creditReview.threshold'], { tenantId: 17 }],
      ]);
      expect(sends.reduce((total, call) => total + JSON.stringify(call.body).length, 0)).toBeLessThanOrEqual(16_384);
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'pagehide-cap' }]);
    });

    it('keeps both bodies within 16 KB, dropping the older keys with pagehide-cap', async () => {
      const rig = await setUp();
      rig.prefs.set(VIEW, { wide: 'x'.repeat(7_000) });
      await vi.advanceTimersByTimeAsync(1_000);
      rig.prefs.set(THRESHOLD, 80);
      await vi.advanceTimersByTimeAsync(500);
      rig.prefs.set(tmPreferenceKey('search.gl.Agent.view', 'write-behind', {}), { wide: 'y'.repeat(7_000) });
      await vi.advanceTimersByTimeAsync(300);
      rig.prefs.set(tmPreferenceKey('search.gl.Unit.view', 'write-behind', {}), { wide: 'z'.repeat(7_000) });
      await vi.advanceTimersByTimeAsync(100);
      rig.host.fire('window', 'pagehide');
      const sends = rig.writes().filter((call) => call.kind === 'send');
      const items = (sends[0]!.body as { items: { key: string }[] }).items.map((item) => item.key);
      expect(items).toEqual(['search.gl.Unit.view', 'search.gl.Agent.view', 'acme.creditReview.threshold']);
      expect(JSON.stringify(sends[0]!.body).length).toBeLessThanOrEqual(16_384);
      expect(rig.items('tm.prefs.dropped')).toEqual([{ reason: 'pagehide-cap' }]);
    });
  });
});
