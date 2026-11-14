// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, type Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TM_BLOB_FETCHER } from '@tellma/core-ui/image';
import { provideTellmaTesting, TmFakeApi, tmTestMetadata } from '@tellma/core-testing';

import { provideTellma } from '../app/provide-tellma';
import { TM_NOW } from '../app/tm-now';
import { TmLaunch } from '../launch/tm-launch';
import { TmClientMetadataStore, type TmContracts } from '../metadata/tm-client-metadata';
import { TM_METADATA_LOADERS, type TmMetadataLoaders } from '../metadata/tm-metadata-loaders';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmRequestLog } from '../requests/tm-request-log';
import { tmProfileCookies, tmProfileCookieValue } from '../session/tm-profile.testing.util';
import { TmSession } from '../session/tm-session';
import { TmSessionPaths } from '../session/tm-session-paths';
import { TmFakeSessionHost, tmSessionTestingProviders } from '../session/tm-session.testing.util';
import { TmApi } from './tm-api';
import { TmApiError } from './tm-api-error';
import { TM_API_HOST } from './tm-api-host';
import { TM_API_CACHE, TM_API_LOCALE, TM_API_UPDATES } from './tm-api-seams';
import {
  TM_TEST_API_LOCALE,
  tmAnswer,
  TmFakeApiCache,
  TmFakeApiHost,
  TmFakeApiUpdates,
  type TmFakeRequest,
  tmProblemAnswer,
  TmRecordingApiTelemetry,
  TmRecordingRequestLog,
} from './tm-api.testing.util';
import { TmHttpApi } from './tm-http-api';

// The HTTP layer over a faked browser: every request is answered by the
// spec, the clock is faked, and the session reads a faked cookie jar.

/** A page for every route. */
@Component({ template: '' })
class TmPage {}

const NOW = 1_700_000_000_000;
const FARAH = '0b4f';
const SARA = '7c1e';
const UI_VERSION = '1.0.0+0123abc';
const TRACEPARENT = /^00-[0-9a-f]{32}-[0-9a-f]{16}-01$/;

/** A route's key in the Contracts chunk. */
const key = (route: string): string => `POST /{tenantId}/api/web/${route}`;

/** The Contracts chunk the specs load. */
const CONTRACTS: TmContracts = {
  fingerprints: {
    [key('centers/query')]: 'fp-query',
    [key('centers/save')]: 'fp-save',
    [key('centers/export')]: 'fp-export',
    [key('centers/recompute')]: 'fp-recompute',
  },
  long: [key('centers/recompute')],
  idempotent: [key('centers/query'), key('centers/export')],
};

/** Everything a spec drives or inspects. */
interface TmRig {
  readonly host: TmFakeApiHost;
  readonly sessionHost: TmFakeSessionHost;
  readonly api: TmHttpApi;
  readonly log: TmRecordingRequestLog;
  readonly telemetry: TmRecordingApiTelemetry;
  readonly cache: TmFakeApiCache;
  readonly updates: TmFakeApiUpdates;
  readonly paths: TmSessionPaths;
  readonly contracts: { loads: number; fail: boolean };
}

/** Configures the layer over a faked browser, routed to a URL, and fakes the clock. */
async function setUp(options: { url?: string; providers?: Provider[] } = {}): Promise<TmRig> {
  const host = new TmFakeApiHost();
  // A hidden page sends no reachability probe, which has a spec of its own.
  host.visibleNow = false;
  const sessionHost = new TmFakeSessionHost();
  sessionHost.cookieJar = tmProfileCookies(FARAH, 'ar');
  const cache = new TmFakeApiCache();
  const updates = new TmFakeApiUpdates();
  const contracts = { loads: 0, fail: false };
  const unused = (): Promise<never> => Promise.reject(new Error('not loaded in these specs'));
  const loaders: TmMetadataLoaders = {
    core: unused,
    entities: unused,
    contracts: () => {
      contracts.loads++;
      return contracts.fail ? Promise.reject(new Error('unreachable')) : Promise.resolve(CONTRACTS);
    },
    strings: {},
  };
  TestBed.configureTestingModule({
    providers: [
      ...tmSessionTestingProviders(sessionHost, null, NOW),
      // The clock follows the faked timers, so waits move it.
      { provide: TM_NOW, useValue: () => Date.now() },
      { provide: TmTelemetry, useClass: TmRecordingApiTelemetry },
      { provide: TmRequestLog, useClass: TmRecordingRequestLog },
      provideRouter([{ path: '**', component: TmPage }]),
      { provide: TM_API_HOST, useValue: host },
      { provide: TM_API_LOCALE, useValue: TM_TEST_API_LOCALE },
      { provide: TM_API_CACHE, useValue: cache },
      { provide: TM_API_UPDATES, useValue: updates },
      { provide: TM_METADATA_LOADERS, useValue: loaders },
      ...(options.providers ?? []),
    ],
  });
  // The boot loads the Core chunk, and starts the session, before any call.
  TestBed.inject(TmClientMetadataStore).set(tmTestMetadata());
  TestBed.inject(TmSession);
  await TestBed.inject(Router).navigateByUrl(options.url ?? '/17/centers');
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  vi.setSystemTime(NOW);
  return {
    host,
    sessionHost,
    api: TestBed.inject(TmHttpApi),
    log: TestBed.inject(TmRequestLog) as TmRecordingRequestLog,
    telemetry: TestBed.inject(TmTelemetry) as TmRecordingApiTelemetry,
    cache,
    updates,
    paths: TestBed.inject(TmSessionPaths),
    contracts,
  };
}

/** Runs the pending microtasks and the timers that are due now. */
const flush = async (): Promise<void> => {
  // Reading a body takes the browser a few real tasks; each advance yields one.
  for (let i = 0; i < 5; i++) {
    await vi.advanceTimersByTimeAsync(0);
  }
};

/** A promise's outcome, as it settles. */
interface TmTracked<T> {
  settled: boolean;
  value: T | undefined;
  error: unknown;
}

/** Tracks a promise, so a spec reads its outcome without awaiting it. */
function track<T>(promise: Promise<T>): TmTracked<T> {
  const state: TmTracked<T> = { settled: false, value: undefined, error: undefined };
  promise.then(
    (value) => Object.assign(state, { settled: true, value }),
    (error: unknown) => Object.assign(state, { settled: true, error }),
  );
  return state;
}

/** A read's options with a fresh signal. */
const signal = (): { signal: AbortSignal } => ({ signal: new AbortController().signal });

/** The `tm.api.retry` reasons recorded. */
const retries = (rig: TmRig): unknown[] =>
  rig.telemetry.events.filter((event) => event.name === 'tm.api.retry').map((event) => event.props?.['reason']);

describe('TmHttpApi', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('is the root default of TmApi', async () => {
    await setUp();
    expect(TestBed.inject(TmApi)).toBe(TestBed.inject(TmHttpApi));
  });

  it('lets the services it calls inject TmApi themselves', async () => {
    const cache = new TmFakeApiCache();
    const rig = await setUp({
      providers: [
        { provide: TM_API_CACHE, useFactory: () => (inject(TmApi), cache) },
        { provide: TM_API_LOCALE, useFactory: () => (inject(TmApi), TM_TEST_API_LOCALE) },
        { provide: TmTelemetry, useFactory: () => (inject(TmApi), new TmRecordingApiTelemetry()) },
      ],
    });
    const read = track(rig.api.read('centers/query', {}, signal()));
    await flush();
    expect(rig.host.last().header('Accept-Language')).toBe('ar-SA');
    rig.host.last().answer(tmProblemAnswer(429, 'rate-limited', { 'Retry-After': '1' }));
    await flush();
    await vi.advanceTimersByTimeAsync(1_000);
    await flush();
    expect(rig.host.requests).toHaveLength(2);
    rig.host.last().answer(tmAnswer(200, { ok: true }, { 'Tellma-Version-Tags': 'settings=1.9f04' }));
    await flush();
    expect(read.value).toEqual({ ok: true });
    expect(cache.observed).toHaveLength(1);
    expect(retries(rig)).toEqual(['429']);
  });

  describe('routes and headers', () => {
    it('sends a tenant read to the routed tenant with every header', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', { top: 25 }, { ...signal(), background: true, priority: 'low' }));
      await flush();

      const request = rig.host.last();
      expect(request.url).toBe('/17/api/web/centers/query');
      expect(request.init.method).toBe('POST');
      expect(request.init.credentials).toBe('same-origin');
      expect(request.init.body).toBe('{"top":25}');
      expect(request.header('Tellma-Client')).toBe(`web/${UI_VERSION}`);
      expect(request.header('Tellma-Contract')).toBe('fp-query');
      expect(request.header('Accept-Language')).toBe('ar-SA');
      expect(request.header('Tellma-Calendar')).toBe('uq');
      expect(request.header('traceparent')).toMatch(TRACEPARENT);
      expect(request.header('Tellma-Activity')).toBe('background');
      expect(request.header('Tellma-Priority')).toBe('low');
      expect(request.header('Content-Type')).toBe('application/json');
      expect(request.header('Tellma-Time-Zone')).toBeNull();

      request.answer(tmAnswer(200, { result: [] }));
      await flush();
      expect(read.value).toEqual({ result: [] });
    });

    it('sends a tenantless GET as is, with no body, contract or calendar', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('/bff/user', undefined, { ...signal(), method: 'GET' }));
      await flush();

      const request = rig.host.last();
      expect(request.url).toBe('/bff/user');
      expect(request.init.method).toBe('GET');
      expect(request.init.body).toBeUndefined();
      expect(request.header('Tellma-Contract')).toBeNull();
      expect(request.header('Tellma-Calendar')).toBeNull();
      expect(request.header('Content-Type')).toBeNull();
      expect(request.header('Accept-Language')).toBe('ar-SA');
      expect(rig.contracts.loads).toBe(0);

      request.answer(tmAnswer(200, { sub: FARAH }));
      await flush();
      expect(read.value).toEqual({ sub: FARAH });
    });

    it('sends a write to the tenant it names, with the confirmed warnings', async () => {
      const rig = await setUp();
      track(rig.api.write('centers/save', { entities: [] }, { tenantId: 18, confirmations: ['Inventory.BackdatedRecost', 'Sales.OpenOrders'] }));
      await flush();

      const request = rig.host.last();
      expect(request.url).toBe('/18/api/web/centers/save');
      expect(request.header('Tellma-Confirm')).toBe('Inventory.BackdatedRecost, Sales.OpenOrders');
      expect(request.header('Tellma-Contract')).toBe('fp-save');
      expect(request.header('Tellma-Activity')).toBeNull();
      expect(request.header('Tellma-Priority')).toBeNull();
    });

    it('refuses malformed confirmations in development', async () => {
      const rig = await setUp();
      const many = Array.from({ length: 17 }, (_, i) => `Key.${i}`);
      await expect(rig.api.write('centers/save', {}, { confirmations: many })).rejects.toThrow(/at most 16 keys/);
      await expect(rig.api.write('centers/save', {}, { confirmations: ['A,B'] })).rejects.toThrow(/at most 16 keys/);
      expect(rig.host.requests).toHaveLength(0);
    });

    it('gives every call a fresh trace id', async () => {
      const rig = await setUp();
      track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.byte = 0x12;
      track(rig.api.write('centers/save', {}));
      await flush();
      const [first, second] = rig.host.requests.map((request) => request.header('traceparent')!.split('-')[1]);
      expect(first).toBe('ab'.repeat(16));
      expect(second).toBe('12'.repeat(16));
    });

    it('rejects a body on a GET in development', async () => {
      const rig = await setUp();
      await expect(rig.api.read('/bff/user', { x: 1 }, { ...signal(), method: 'GET' })).rejects.toThrow(/has a body/);
    });
  });

  describe('a tenant route with no tenant', () => {
    it('throws in development without sending', async () => {
      const rig = await setUp({ url: '/tenants' });
      await expect(rig.api.read('centers/query', {}, signal())).rejects.toThrow(/was called with no tenant/);
      expect(() => rig.api.send('centers/save', {})).toThrow(/was called with no tenant/);
      expect(rig.host.requests).toHaveLength(0);
    });

    it('rejects server (no-tenant) in production without sending', async () => {
      const rig = await setUp({ url: '/tenants' });
      rig.host.dev = false;
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      expect(read.error).toBeInstanceOf(TmApiError);
      expect(read.error).toMatchObject({ kind: 'server', code: 'no-tenant' });
      rig.api.send('centers/save', {});
      expect(rig.host.requests).toHaveLength(0);
    });
  });

  describe('a URL that is not a path on this origin', () => {
    /** URLs a tenantless route or a blob fetch must never send. */
    const FOREIGN = [
      '//cdn.example/logo.png',
      '/\\cdn.example/logo.png',
      '/\t/cdn.example/logo.png',
      '/health/../17/blobs/attachment/9',
      '/health/%2E%2E/17/blobs/attachment/9',
      '/17/blobs/./9',
    ];

    it.each(['https://cdn.example/logo.png', ...FOREIGN])('throws for the blob %j in development, unsent', async (url) => {
      const rig = await setUp();
      await expect(rig.api.fetchBlob(url)).rejects.toThrow(/is not a path on this origin/);
      expect(rig.host.requests).toHaveLength(0);
    });

    it.each(FOREIGN)('rejects the route %j as server (invalid-url) in production, unsent, the server still reachable', async (url) => {
      const rig = await setUp();
      rig.host.dev = false;
      const read = track(rig.api.read(url, undefined, { ...signal(), method: 'GET' }));
      const blob = track(rig.api.fetchBlob(url));
      rig.api.send(url, {});
      await flush();
      expect(read.error).toMatchObject({ kind: 'server', code: 'invalid-url' });
      expect(blob.error).toMatchObject({ kind: 'server', code: 'invalid-url' });
      expect(rig.host.requests).toHaveLength(0);
      expect(rig.api.online()).toBe(true);
      expect(rig.paths.state()).toBe('active');
    });

    it('rejects a tenant route or a blob kind with a dot segment', async () => {
      const rig = await setUp();
      rig.host.dev = false;
      const write = track(rig.api.write('centers/../../../bff/user', {}));
      const upload = track(rig.api.upload('..', new Blob(['1']), { ...signal(), fileName: 'a.txt' }));
      await flush();
      expect(write.error).toMatchObject({ kind: 'server', code: 'invalid-url' });
      expect(upload.error).toMatchObject({ kind: 'server', code: 'invalid-url' });
      expect(rig.host.requests).toHaveLength(0);
      expect(rig.host.uploads).toHaveLength(0);
    });

    it('sends a local path with dots inside a segment or in the query', async () => {
      const rig = await setUp();
      track(rig.api.fetchBlob('/17/blobs/v1.2/9..png?name=a/../b'));
      await flush();
      expect(rig.host.last().url).toBe('/17/blobs/v1.2/9..png?name=a/../b');
    });
  });

  describe('read or write', () => {
    it('refuses a read of a mapped route that is not idempotent in development', async () => {
      const rig = await setUp();
      await expect(rig.api.read('centers/save', {}, signal())).rejects.toThrow(/is not safe to retry/);
      expect(rig.host.requests).toHaveLength(0);
    });

    it('runs such a read once in production, without retry', async () => {
      const rig = await setUp();
      rig.host.dev = false;
      const read = track(rig.api.read('centers/save', {}, signal()));
      await flush();
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(10_000);
      expect(rig.host.requests).toHaveLength(1);
      expect(read.error).toMatchObject({ kind: 'offline', code: 'network' });
    });

    it('trusts a read of a route outside the map', async () => {
      const rig = await setUp();
      track(rig.api.read('reports/acme-credit', {}, signal()));
      await flush();
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(2_000);
      expect(rig.host.requests).toHaveLength(2);
      expect(rig.host.requests[0].header('Tellma-Contract')).toBeNull();
    });

    it('sends a read route through write once, never retried', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/query', {}));
      await flush();
      rig.host.last().answer(tmProblemAnswer(503, 'unavailable', { 'Retry-After': '1' }));
      await vi.advanceTimersByTimeAsync(5_000);
      expect(rig.host.requests).toHaveLength(1);
      expect(write.error).toMatchObject({ kind: 'unavailable', retryAfterMs: 1_000 });
    });
  });

  describe('the user check', () => {
    it('runs the expiry path and rejects unauthenticated, unsent, when the session is gone', async () => {
      const rig = await setUp();
      rig.sessionHost.cookieJar = '';
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      expect(rig.host.requests).toHaveLength(0);
      expect(write.error).toMatchObject({ kind: 'unauthenticated' });
      expect(rig.paths.state()).toBe('expired');
      // The expiry path sweeps the store, then leaves to sign in again.
      await vi.waitFor(() => expect(rig.sessionHost.assigned).toHaveLength(1));
      expect(rig.sessionHost.reloads).toBe(0);
    });

    it('runs the changed-user path and rejects cancelled, unsent, under another user', async () => {
      const rig = await setUp();
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      expect(rig.host.requests).toHaveLength(0);
      expect(read.error).toMatchObject({ kind: 'cancelled' });
      expect(rig.sessionHost.replaced).toEqual(['/']);
    });

    it("runs again on the response: another user's answer is discarded", async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      rig.host.last().answer(tmAnswer(200, { secret: true }));
      await flush();
      expect(read.error).toMatchObject({ kind: 'cancelled' });
      expect(read.value).toBeUndefined();
      expect(rig.sessionHost.replaced).toEqual(['/']);
    });

    it("observes nothing of another user's answer", async () => {
      const rig = await setUp();
      const launch = TestBed.inject(TmLaunch);
      launch.markAutomatic(17);
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      rig.host.last().answer(tmAnswer(200, {}, { 'Tellma-Version-Tags': 'settings=1.9f04' }));
      await flush();
      expect(write.error).toMatchObject({ kind: 'cancelled' });
      expect(rig.cache.observed).toEqual([]);
      expect(launch.automatic()).toBe(17);
    });

    it('runs no expiry path for a 401 from an anonymous route', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('/api/distribution-info', undefined, { ...signal(), method: 'GET' }));
      await flush();
      rig.host.last().answer(tmProblemAnswer(401, 'unauthenticated'));
      await flush();
      expect(read.error).toMatchObject({ kind: 'unauthenticated', status: 401 });
      expect(rig.paths.state()).toBe('active');
      expect(rig.sessionHost.reloads).toBe(0);
    });

    it('runs again on the response: a session gone meanwhile rejects unauthenticated', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.sessionHost.cookieJar = '';
      rig.host.last().answer(tmAnswer(200, {}));
      await flush();
      expect(write.error).toMatchObject({ kind: 'unauthenticated' });
      // The expiry path sweeps the store, then leaves to sign in again.
      await vi.waitFor(() => expect(rig.sessionHost.assigned).toHaveLength(1));
      expect(rig.sessionHost.reloads).toBe(0);
    });

    it.each(['/api/distribution-info', '/health/ready', '/.well-known/openid-configuration'])('skips the anonymous route %s', async (route) => {
      const rig = await setUp();
      rig.sessionHost.cookieJar = '';
      const read = track(rig.api.read(route, undefined, { ...signal(), method: 'GET' }));
      await flush();
      rig.host.last().answer(tmAnswer(200, { slug: 'acme' }));
      await flush();
      expect(read.value).toEqual({ slug: 'acme' });
      expect(rig.paths.state()).toBe('active');
    });

    it('runs the expiry path on a 401 for the session the call went out under', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(401, 'unauthenticated'));
      await flush();
      expect(read.error).toMatchObject({ kind: 'unauthenticated', status: 401 });
      // The expiry path sweeps the store, then leaves to sign in again.
      await vi.waitFor(() => expect(rig.sessionHost.assigned).toHaveLength(1));
      expect(rig.sessionHost.reloads).toBe(0);
    });

    it('cancels, ending nothing, a 401 for a session the cookie no longer names', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      // The same user signed in again in another tab: a new session handle.
      const renewed = { v: 2, sub: FARAH, sh: 'sh-renewed', name: 'Farah Nasser', email: 'farah@acme.example', locale: 'ar' };
      rig.sessionHost.cookieJar = `tellma.profile=${tmProfileCookieValue(renewed)}`;
      rig.host.last().answer(tmProblemAnswer(401, 'unauthenticated'));
      await flush();
      expect(read.error).toMatchObject({ kind: 'cancelled', status: 401 });
      expect(rig.sessionHost.reloads).toBe(0);
      expect(rig.paths.state()).toBe('active');
    });
  });

  describe('an offline write', () => {
    it('is not sent', async () => {
      const rig = await setUp();
      rig.host.onLineNow = false;
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      expect(rig.host.requests).toHaveLength(0);
      expect(write.error).toMatchObject({ kind: 'offline', code: 'not-sent', status: 0 });
    });
  });

  describe('status to kind', () => {
    it.each([
      [400, 'query-invalid', 'query-invalid'],
      [400, 'bad-request', 'bad-request'],
      [403, 'tenant-read-only', 'read-only'],
      [403, 'tenant-frozen', 'read-only'],
      [413, 'limit-exceeded', 'limit'],
      [401, 'step-up-required', 'step-up'],
      [403, 'forbidden', 'forbidden'],
      [403, 'tenant-suspended', 'tenant-unavailable'],
      [404, 'tenant-not-found', 'tenant-not-found'],
      [404, 'not-found', 'not-found'],
      [409, 'concurrency-conflict', 'conflict'],
      [409, 'count-mismatch', 'conflict'],
      [409, 'confirmation-required', 'confirmation'],
      [422, 'validation', 'validation'],
      [400, 'blob-rejected', 'validation'],
      [413, 'blob-rejected', 'validation'],
      [429, 'rate-limited', 'unavailable'],
      [503, 'maintenance', 'unavailable'],
      [504, 'timeout', 'unavailable'],
      [502, 'partial-failure', 'partial-failure'],
      [403, 'csrf-rejected', 'server'],
      [403, 'individual-required', 'server'],
      [500, 'internal-error', 'server'],
    ])('maps %i %s to %s', async (status, code, kind) => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmProblemAnswer(status, code));
      await flush();
      expect(write.error).toBeInstanceOf(TmApiError);
      expect(write.error).toMatchObject({ kind, code, status });
      expect((write.error as TmApiError).problem).toMatchObject({ code, status });
    });

    it.each([
      [405, '', 'server', 'unexpected-response'],
      [415, '', 'server', 'unexpected-response'],
      [500, '<html>Proxy error</html>', 'server', 'unexpected-response'],
      [502, '<html>Bad gateway</html>', 'server', 'unexpected-response'],
      [503, '<html>Down</html>', 'unavailable', 'unavailable'],
      [429, '', 'unavailable', 'rate-limited'],
    ])('maps a bare %i to %s (%s)', async (status, body, kind, code) => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmAnswer(status, body === '' ? undefined : body));
      await flush();
      expect(write.error).toMatchObject({ kind, code, status, problem: null });
    });

    it('maps a body that is not JSON on a 2xx to server (unexpected-response)', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmAnswer(200, '<html>Captive portal</html>'));
      await flush();
      expect(read.error).toMatchObject({ kind: 'server', code: 'unexpected-response', status: 200 });
    });

    it('resolves an empty success as undefined', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmAnswer(204));
      await flush();
      expect(write.settled).toBe(true);
      expect(write.value).toBeUndefined();
      expect(write.error).toBeUndefined();
    });

    it('takes the trace id from Tellma-Trace-Id, else the problem, else its own', async () => {
      const rig = await setUp();
      const traces: unknown[] = [];
      for (const answer of [
        tmProblemAnswer(400, 'bad-request', { 'Tellma-Trace-Id': 'from-header' }),
        tmProblemAnswer(400, 'bad-request'),
        tmAnswer(500),
      ]) {
        const write = track(rig.api.write('centers/save', {}));
        await flush();
        rig.host.last().answer(answer);
        await flush();
        traces.push((write.error as TmApiError).traceId);
      }
      expect(traces).toEqual(['from-header', '0af7651916cd43dd8448eb211c80319c', 'ab'.repeat(16)]);
    });

    it('parses the challenge of a step-up', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(
        tmProblemAnswer(401, 'step-up-required', {
          'WWW-Authenticate': 'Bearer error="insufficient_user_authentication", acr_values="mfa", max_age=300',
        }),
      );
      await flush();
      expect(write.error).toMatchObject({ kind: 'step-up', challenge: { acr: 'mfa', maxAge: 300 } });
      expect(rig.sessionHost.reloads).toBe(0);
    });

    it('refreshes the tenant document on read-only', async () => {
      const rig = await setUp();
      track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmProblemAnswer(403, 'tenant-frozen'));
      await flush();
      expect(rig.cache.refreshed).toEqual([{ tenantId: 17, docs: ['tenant'] }]);
    });

    it('rejects cancelled when the caller aborts', async () => {
      const rig = await setUp();
      const controller = new AbortController();
      const read = track(rig.api.read('centers/query', {}, { signal: controller.signal }));
      await flush();
      controller.abort();
      await flush();
      expect(rig.host.last().aborted).toBe(true);
      expect(read.error).toMatchObject({ kind: 'cancelled', status: 0 });
      await vi.advanceTimersByTimeAsync(10_000);
      expect(rig.host.requests).toHaveLength(1);
    });

    it('rejects cancelled, unsent, when the signal is already aborted', async () => {
      const rig = await setUp();
      const controller = new AbortController();
      controller.abort();
      const read = track(rig.api.read('centers/query', {}, { signal: controller.signal }));
      await flush();
      expect(rig.host.requests).toHaveLength(0);
      expect(read.error).toMatchObject({ kind: 'cancelled' });
    });

    it('rejects a write that fails at the network level as offline (outcome-unknown)', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().fail();
      await flush();
      expect(write.error).toMatchObject({ kind: 'offline', code: 'outcome-unknown' });
      expect(rig.api.online()).toBe(false);
    });
  });

  describe('retries', () => {
    it('retries a network failure twice, after 1 s and 3 s with jitter', async () => {
      const rig = await setUp();
      rig.host.byte = 0;
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().fail();
      // A zero draw is the low end of the ±25 % jitter.
      await vi.advanceTimersByTimeAsync(749);
      expect(rig.host.requests).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(1);
      expect(rig.host.requests).toHaveLength(2);
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(2_249);
      expect(rig.host.requests).toHaveLength(2);
      await vi.advanceTimersByTimeAsync(1);
      expect(rig.host.requests).toHaveLength(3);
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(10_000);
      expect(rig.host.requests).toHaveLength(3);
      expect(read.error).toMatchObject({ kind: 'offline', code: 'network' });
      expect(retries(rig)).toEqual(['network', 'network']);
      expect(rig.api.online()).toBe(false);
    });

    it('keeps the trace id and draws a new span for each retry', async () => {
      const rig = await setUp();
      track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.byte = 0x34;
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(1_500);
      const [first, second] = rig.host.requests.map((request) => request.header('traceparent')!.split('-'));
      expect(second[1]).toBe(first[1]);
      expect(second[2]).not.toBe(first[2]);
    });

    it('does not retry a network failure while the browser is offline', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.onLineNow = false;
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(10_000);
      expect(rig.host.requests).toHaveLength(1);
      expect(read.error).toMatchObject({ kind: 'offline', code: 'network' });
    });

    it('retries a 429 after Retry-After, capped at 10 s, twice', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      for (let i = 0; i < 3; i++) {
        await flush();
        rig.host.last().answer(tmProblemAnswer(429, 'rate-limited', { 'Retry-After': '20' }));
        await vi.advanceTimersByTimeAsync(9_999);
        expect(rig.host.requests).toHaveLength(i + 1);
        await vi.advanceTimersByTimeAsync(1);
      }
      expect(rig.host.requests).toHaveLength(3);
      expect(read.error).toMatchObject({ kind: 'unavailable', code: 'rate-limited', retryAfterMs: 20_000 });
      expect(retries(rig)).toEqual(['429', '429']);
    });

    it('retries a 429 without Retry-After after 1 s', async () => {
      const rig = await setUp();
      track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(429, 'rate-limited'));
      await vi.advanceTimersByTimeAsync(999);
      expect(rig.host.requests).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(1);
      expect(rig.host.requests).toHaveLength(2);
    });

    it('retries a 503 with Retry-After, as an HTTP date too, capped at 30 s', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(503, 'maintenance', { 'Retry-After': '45' }));
      await vi.advanceTimersByTimeAsync(29_999);
      expect(rig.host.requests).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(1);
      expect(rig.host.requests).toHaveLength(2);
      rig.host.last().answer(tmProblemAnswer(503, 'maintenance', { 'Retry-After': new Date(Date.now() + 5_000).toUTCString() }));
      await vi.advanceTimersByTimeAsync(4_999);
      expect(rig.host.requests).toHaveLength(2);
      await vi.advanceTimersByTimeAsync(1);
      expect(rig.host.requests).toHaveLength(3);
      rig.host.last().answer(tmAnswer(200, { ok: true }));
      await flush();
      expect(read.value).toEqual({ ok: true });
      expect(retries(rig)).toEqual(['503', '503']);
    });

    it('does not retry a 503 without Retry-After', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(503, 'maintenance'));
      await vi.advanceTimersByTimeAsync(60_000);
      expect(rig.host.requests).toHaveLength(1);
      expect(read.error).toMatchObject({ kind: 'unavailable', code: 'maintenance' });
    });

    it('ends a retry wait at once when the caller aborts', async () => {
      const rig = await setUp();
      const controller = new AbortController();
      const read = track(rig.api.read('centers/query', {}, { signal: controller.signal }));
      await flush();
      rig.host.last().answer(tmProblemAnswer(429, 'rate-limited', { 'Retry-After': '5' }));
      await vi.advanceTimersByTimeAsync(1_000);
      controller.abort();
      await flush();
      expect(read.error).toMatchObject({ kind: 'cancelled' });
      await vi.advanceTimersByTimeAsync(10_000);
      expect(rig.host.requests).toHaveLength(1);
      expect(rig.log.entries[0].end).toMatchObject({ outcome: 'cancelled' });
    });

    it('checks the user again before a retry is sent', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(503, 'maintenance', { 'Retry-After': '5' }));
      await flush();
      // Another user signs in elsewhere during the wait.
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      await vi.advanceTimersByTimeAsync(5_000);
      await flush();
      expect(rig.host.requests).toHaveLength(1);
      expect(read.error).toMatchObject({ kind: 'cancelled' });
      expect(rig.sessionHost.replaced).toEqual(['/']);
      expect(rig.log.entries[0].end).toMatchObject({ outcome: 'cancelled' });
    });

    it('runs the expiry path instead of a retry when the session ends during the wait', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(429, 'rate-limited', { 'Retry-After': '2' }));
      await flush();
      rig.sessionHost.cookieJar = '';
      await vi.advanceTimersByTimeAsync(2_000);
      await flush();
      expect(rig.host.requests).toHaveLength(1);
      expect(read.error).toMatchObject({ kind: 'unauthenticated' });
      expect(rig.paths.state()).toBe('expired');
    });

    it('never retries a write', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmProblemAnswer(429, 'rate-limited', { 'Retry-After': '1' }));
      await vi.advanceTimersByTimeAsync(10_000);
      expect(rig.host.requests).toHaveLength(1);
      expect(write.error).toMatchObject({ kind: 'unavailable' });
    });
  });

  describe('the outdated-client verdict', () => {
    /** Answers the next write with a 412 under a server build, and returns its error. */
    async function verdictOf(rig: TmRig, build: string | null, status = 412): Promise<TmTracked<unknown>> {
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      const code = status === 412 ? 'client-outdated' : 'route-not-found';
      rig.host.last().answer(tmProblemAnswer(status, code, build === null ? {} : { 'Tellma-Build': build }));
      await flush();
      return write;
    }

    /** The verdicts recorded. */
    const verdicts = (rig: TmRig): unknown[] =>
      rig.telemetry.events.filter((event) => event.name === 'tm.api.client-outdated').map((event) => event.props);

    it('blames the client when the server is newer, and asks the worker to update', async () => {
      const rig = await setUp();
      const write = await verdictOf(rig, '1.1.0+9e8d7c6');
      expect(write.error).toMatchObject({ kind: 'client-outdated', code: 'client-outdated', status: 412 });
      expect(rig.updates.checks).toBe(1);
      expect(verdicts(rig)).toEqual([{ verdict: 'client-outdated', source: 'build' }]);
    });

    it('blames the server when it is older, at once', async () => {
      const rig = await setUp();
      const write = await verdictOf(rig, '0.9.0+9e8d7c6', 404);
      expect(write.error).toMatchObject({ kind: 'client-outdated', code: 'server-outdated', status: 404 });
      expect(rig.updates.checks).toBe(0);
      expect(verdicts(rig)).toEqual([{ verdict: 'server-outdated', source: 'build' }]);
    });

    it('orders pre-releases below their release', async () => {
      const rig = await setUp();
      const write = await verdictOf(rig, '1.0.0-rc.1+9e8d7c6');
      expect(write.error).toMatchObject({ code: 'server-outdated' });
    });

    it.each([
      ['ready', 'client-outdated'],
      ['none', 'server-outdated'],
      ['failed', 'client-outdated'],
      ['unreachable', 'client-outdated'],
      ['disabled', 'client-outdated'],
    ] as const)('asks the worker when the builds are unordered: %s gives %s', async (answer, code) => {
      const rig = await setUp();
      rig.updates.answer = answer;
      const write = await verdictOf(rig, '1.0.0+fedcba9');
      expect(write.error).toMatchObject({ kind: 'client-outdated', code });
      expect(verdicts(rig)).toEqual([{ verdict: code, source: 'worker' }]);
    });

    it('asks the worker when the server names no build', async () => {
      const rig = await setUp();
      rig.updates.answer = 'none';
      const write = await verdictOf(rig, null);
      expect(write.error).toMatchObject({ code: 'server-outdated' });
      expect(rig.updates.checks).toBe(1);
    });

    it('blames the client when the worker does not answer within 3 s', async () => {
      const rig = await setUp();
      rig.updates.answer = null;
      const write = await verdictOf(rig, '1.0.0+fedcba9');
      expect(write.settled).toBe(false);
      await vi.advanceTimersByTimeAsync(2_999);
      expect(write.settled).toBe(false);
      await vi.advanceTimersByTimeAsync(1);
      expect(write.error).toMatchObject({ code: 'client-outdated' });
      expect(verdicts(rig)).toEqual([{ verdict: 'client-outdated', source: 'worker' }]);
    });

    it('reports the same build refusing its own contract as a bug', async () => {
      const rig = await setUp();
      const write = await verdictOf(rig, UI_VERSION);
      expect(write.error).toMatchObject({ kind: 'server', code: 'contract-mismatch', status: 412 });
      expect(rig.telemetry.errors).toEqual([{ error: write.error, context: { route: 'centers/save', build: UI_VERSION } }]);
      expect(verdicts(rig)).toEqual([]);
    });

    it('asks the worker at a second older verdict for the same build past the retry window, as after a rollback', async () => {
      const rig = await setUp();
      rig.updates.answer = 'ready';
      expect((await verdictOf(rig, '0.9.0')).error).toMatchObject({ code: 'server-outdated' });
      await vi.advanceTimersByTimeAsync(30_000);
      expect((await verdictOf(rig, '0.9.0')).error).toMatchObject({ code: 'client-outdated' });
      rig.updates.answer = 'none';
      expect((await verdictOf(rig, '0.9.0')).error).toMatchObject({ code: 'server-outdated' });
      expect(verdicts(rig)).toEqual([
        { verdict: 'server-outdated', source: 'build' },
        { verdict: 'client-outdated', source: 'worker' },
        { verdict: 'server-outdated', source: 'worker' },
      ]);
    });

    it('still judges a verdict 29.999 s after the first one as the first', async () => {
      const rig = await setUp();
      rig.updates.answer = 'ready';
      expect((await verdictOf(rig, '0.9.0')).error).toMatchObject({ code: 'server-outdated' });
      await vi.advanceTimersByTimeAsync(29_999);
      expect((await verdictOf(rig, '0.9.0')).error).toMatchObject({ code: 'server-outdated' });
      expect(rig.updates.checks).toBe(0);
      expect(verdicts(rig)).toEqual(Array(2).fill({ verdict: 'server-outdated', source: 'build' }));
    });

    it('judges the calls one slot swap answers at once as the same first verdict', async () => {
      const rig = await setUp();
      rig.updates.answer = 'ready';
      const reads = [0, 1, 2].map(() => track(rig.api.read('centers/query', {}, signal())));
      await flush();
      expect(rig.host.requests).toHaveLength(3);
      await vi.advanceTimersByTimeAsync(5_000);
      for (const request of [...rig.host.requests]) {
        request.answer(tmProblemAnswer(412, 'client-outdated', { 'Tellma-Build': '0.9.0' }));
      }
      await flush();
      expect(rig.updates.checks).toBe(0);
      expect(verdicts(rig)).toEqual(Array(3).fill({ verdict: 'server-outdated', source: 'build' }));
      // Each read retries once, 30 s after its own verdict, and the swap has finished by then.
      await vi.advanceTimersByTimeAsync(30_000);
      expect(rig.host.requests).toHaveLength(6);
      for (const request of rig.host.requests.slice(3)) {
        request.answer(tmAnswer(200, { ok: true }));
      }
      await flush();
      expect(reads.map((read) => read.value)).toEqual(Array(3).fill({ ok: true }));
    });

    it('retries a read judged server-outdated once, after 30 s', async () => {
      const rig = await setUp();
      rig.updates.answer = 'none';
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(412, 'client-outdated', { 'Tellma-Build': '0.9.0' }));
      await vi.advanceTimersByTimeAsync(29_999);
      expect(rig.host.requests).toHaveLength(1);
      await vi.advanceTimersByTimeAsync(1);
      expect(rig.host.requests).toHaveLength(2);
      rig.host.last().answer(tmProblemAnswer(412, 'client-outdated', { 'Tellma-Build': '0.9.0' }));
      await vi.advanceTimersByTimeAsync(60_000);
      expect(rig.host.requests).toHaveLength(2);
      expect(read.error).toMatchObject({ code: 'server-outdated' });
      expect(retries(rig)).toEqual(['server-outdated']);
    });
  });

  describe('the watchdog', () => {
    it('ends a black-holed write after 60 s as offline (outcome-unknown)', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      await vi.advanceTimersByTimeAsync(59_999);
      expect(write.settled).toBe(false);
      await vi.advanceTimersByTimeAsync(1);
      expect(rig.host.last().aborted).toBe(true);
      expect(write.error).toMatchObject({ kind: 'offline', code: 'outcome-unknown' });
      expect(rig.api.online()).toBe(true);
    });

    it('ends a black-holed read without retrying it', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      await flush();
      await vi.advanceTimersByTimeAsync(60_000);
      await vi.advanceTimersByTimeAsync(10_000);
      expect(rig.host.requests).toHaveLength(1);
      expect(read.error).toMatchObject({ kind: 'offline', code: 'outcome-unknown' });
    });

    it('allows 6 min for a long route without the option', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('centers/recompute', {}));
      await flush();
      await vi.advanceTimersByTimeAsync(359_999);
      expect(write.settled).toBe(false);
      await vi.advanceTimersByTimeAsync(1);
      expect(write.error).toMatchObject({ code: 'outcome-unknown' });
    });

    it('allows 6 min for a route outside the map with the option', async () => {
      const rig = await setUp();
      const write = track(rig.api.write('acme/rebuild', {}, { long: true }));
      await flush();
      await vi.advanceTimersByTimeAsync(359_999);
      expect(write.settled).toBe(false);
      await vi.advanceTimersByTimeAsync(1);
      expect(write.settled).toBe(true);
    });
  });

  describe('the observers', () => {
    it('begins one log entry per call and ends it with the outcome', async () => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, { ...signal(), background: true }));
      await flush();
      expect(rig.log.entries).toEqual([
        {
          start: { traceId: 'ab'.repeat(16), route: 'centers/query', tenantId: 17, method: 'POST', background: true },
          end: null,
        },
      ]);
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(1_500);
      rig.host.last().answer(tmAnswer(200, {}, { 'Tellma-Build': '1.0.0+0123abc' }));
      await flush();
      expect(read.settled).toBe(true);
      expect(rig.log.entries).toHaveLength(1);
      expect(rig.log.entries[0].end).toEqual({ outcome: 'ok', status: 200, serverBuild: '1.0.0+0123abc' });
    });

    it('ends a failed call with its status and code', async () => {
      const rig = await setUp();
      track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmProblemAnswer(422, 'validation'));
      await flush();
      expect(rig.log.entries[0].end).toEqual({ outcome: 'error', status: 422, code: 'validation' });
    });

    it('settles the launch mark at a 2xx from the tenant only', async () => {
      const rig = await setUp();
      const launch = TestBed.inject(TmLaunch);
      launch.markAutomatic(17);
      track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmProblemAnswer(404, 'not-found'));
      await flush();
      expect(launch.automatic()).toBe(17);
      track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().answer(tmAnswer(200, {}));
      await flush();
      expect(launch.automatic()).toBeNull();
    });

    it('reports version tags one task after the body is delivered', async () => {
      const rig = await setUp();
      const seen: number[] = [];
      const write = rig.api.write('centers/save', {}).then(() => seen.push(rig.cache.observed.length));
      await flush();
      rig.host.last().answer(tmAnswer(200, {}, { 'Tellma-Version-Tags': 'settings=1.9f04, permissions=1.3f2a' }));
      await vi.advanceTimersByTimeAsync(0);
      await write;
      expect(seen).toEqual([0]);
      expect(rig.cache.observed).toEqual([{ tenantId: 17, tags: { settings: '1.9f04', permissions: '1.3f2a' } }]);
    });

    it('marks the server unreachable after a network failure, and reachable at the next answer', async () => {
      const rig = await setUp();
      rig.host.dev = false;
      track(rig.api.write('centers/save', {}));
      await flush();
      rig.host.last().fail();
      await flush();
      expect(rig.api.online()).toBe(false);
      track(rig.api.read('centers/query', {}, signal()));
      await flush();
      rig.host.last().answer(tmProblemAnswer(429, 'rate-limited'));
      await flush();
      expect(rig.api.online()).toBe(true);
    });

    it('counts the unsettled writes', async () => {
      const rig = await setUp();
      track(rig.api.write('centers/save', {}));
      track(rig.api.write('centers/save', {}));
      track(rig.api.read('centers/query', {}, signal()));
      await flush();
      expect(rig.api.writesInFlight()).toBe(2);
      rig.host.requests[0].answer(tmAnswer(200, {}));
      await flush();
      expect(rig.api.writesInFlight()).toBe(1);
      rig.host.requests[1].answer(tmProblemAnswer(422, 'validation'));
      await flush();
      expect(rig.api.writesInFlight()).toBe(0);
    });
  });

  describe('the Contracts chunk', () => {
    it('is loaded once', async () => {
      const rig = await setUp();
      track(rig.api.read('centers/query', {}, signal()));
      track(rig.api.write('centers/save', {}));
      await flush();
      expect(await rig.api.fingerprint('centers/save')).toBe('fp-save');
      expect(rig.contracts.loads).toBe(1);
    });

    it('fails a call it could not load as offline, unsent', async () => {
      const rig = await setUp();
      rig.contracts.fail = true;
      const read = track(rig.api.read('centers/query', {}, signal()));
      const write = track(rig.api.write('centers/save', {}));
      await flush();
      expect(rig.host.requests).toHaveLength(0);
      expect(read.error).toMatchObject({ kind: 'offline', code: 'network' });
      expect(write.error).toMatchObject({ kind: 'offline', code: 'not-sent' });
    });

    it("names each route's fingerprint, and none outside the map", async () => {
      const rig = await setUp();
      vi.useRealTimers();
      expect(await rig.api.fingerprint('centers/query')).toBe('fp-query');
      expect(await rig.api.fingerprint('acme/rebuild')).toBeNull();
      expect(await rig.api.fingerprint('/bff/user')).toBeNull();
    });
  });

  describe('the session leaving the tab', () => {
    it.each(['signing-out', 'expired'] as const)('rejects new calls cancelled once %s, except the sign-out and sends', async (state) => {
      const rig = await setUp();
      rig.paths.state.set(state);
      const read = track(rig.api.read('centers/query', {}, signal()));
      const write = track(rig.api.write('centers/save', {}));
      const download = track(rig.api.download('centers/export', {}, signal()));
      const upload = track(rig.api.upload('attachment', new Blob(['1']), { ...signal(), fileName: 'a.txt' }));
      const blob = track(rig.api.fetchBlob('/17/blobs/51'));
      const logout = track(rig.api.write('/bff/logout', { scope: 'global' }));
      rig.api.send('/api/telemetry', { items: [] });
      await flush();
      for (const call of [read, write, download, upload, blob]) {
        expect(call.error).toMatchObject({ kind: 'cancelled' });
      }
      expect(rig.host.uploads).toHaveLength(0);
      expect(rig.host.requests.map((request) => request.url)).toEqual(['/bff/logout', '/api/telemetry']);
      rig.host.requests[0].answer(tmAnswer(200, { redirectUrl: '/signout' }));
      await flush();
      expect(logout.value).toEqual({ redirectUrl: '/signout' });
    });

    it("delivers the sign-out's answer, which clears the session cookie", async () => {
      const rig = await setUp();
      rig.paths.state.set('signing-out');
      const logout = track(rig.api.write('/bff/logout', { scope: 'global' }));
      await flush();
      // The server's answer deletes both cookies, the profile cookie included.
      rig.sessionHost.cookieJar = '';
      rig.host.last().answer(tmAnswer(200, { redirectUrl: '/signout' }));
      await flush();
      expect(logout.value).toEqual({ redirectUrl: '/signout' });
      expect(rig.log.entries[0].end).toMatchObject({ outcome: 'ok', status: 200 });
      expect(rig.paths.state()).toBe('signing-out');
      expect(rig.sessionHost.reloads).toBe(0);
    });

    it.each(['signing-out', 'expired'] as const)('aborts the reads, downloads, uploads and blob fetches in flight once %s', async (state) => {
      const rig = await setUp();
      const read = track(rig.api.read('centers/query', {}, signal()));
      const download = track(rig.api.download('centers/export', {}, signal()));
      const blob = track(rig.api.fetchBlob('/17/blobs/51'));
      const write = track(rig.api.write('centers/save', {}));
      const upload = track(rig.api.upload('attachment', new Blob(['1']), { ...signal(), fileName: 'a.txt' }));
      await flush();
      expect(rig.host.requests).toHaveLength(4);
      rig.paths.state.set(state);
      TestBed.tick();
      await flush();
      for (const call of [read, download, blob, upload]) {
        expect(call.error).toMatchObject({ kind: 'cancelled' });
      }
      expect(rig.host.uploads[0].aborted).toBe(true);
      expect(rig.host.requests.find((request) => request.url.endsWith('centers/save'))?.aborted).toBe(false);
      expect(write.settled).toBe(false);
    });
  });

  describe('download', () => {
    /** A response whose body the spec streams chunk by chunk. */
    function streamed(
      request: TmFakeRequest,
      headers: Record<string, string>,
    ): { response: Response; push: (bytes: number) => void; close: () => void } {
      let controller!: ReadableStreamDefaultController<Uint8Array>;
      const body = new ReadableStream<Uint8Array>({ start: (c) => void (controller = c) });
      // Aborting a fetch errors its body, as the browser does.
      request.init.signal?.addEventListener('abort', () => controller.error(new DOMException('aborted', 'AbortError')));
      return {
        response: new Response(body, { status: 200, headers }),
        push: (bytes) => controller.enqueue(new Uint8Array(bytes)),
        close: () => controller.close(),
      };
    }

    it('streams the body, reports bytes and names the file, filename* first', async () => {
      const rig = await setUp();
      const progress: number[] = [];
      const download = track(rig.api.download('centers/export', {}, { ...signal(), onProgress: (bytes) => progress.push(bytes) }));
      await flush();
      const body = streamed(rig.host.last(), {
        'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        'Content-Disposition': `attachment; filename="Centers.xlsx"; filename*=UTF-8''%D9%85%D8%B1%D8%A7%D9%83%D8%B2.xlsx`,
      });
      rig.host.last().answer(body.response);
      await flush();
      body.push(3);
      await flush();
      body.push(4);
      body.close();
      await flush();
      expect(progress).toEqual([3, 7]);
      expect(download.value).toMatchObject({
        fileName: 'مراكز.xlsx',
        contentType: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });
      expect((download.value as { blob: Blob }).blob.size).toBe(7);
    });

    it('restarts the watchdog at each chunk, so only a stall ends it', async () => {
      const rig = await setUp();
      const download = track(rig.api.download('centers/export', {}, signal()));
      await flush();
      const body = streamed(rig.host.last(), { 'Content-Disposition': 'attachment; filename=centers.xlsx' });
      rig.host.last().answer(body.response);
      for (let i = 0; i < 3; i++) {
        await vi.advanceTimersByTimeAsync(50_000);
        body.push(1);
      }
      await flush();
      expect(download.settled).toBe(false);
      await vi.advanceTimersByTimeAsync(60_000);
      expect(download.error).toMatchObject({ kind: 'offline', code: 'outcome-unknown' });
    });
  });

  describe('upload', () => {
    /** A file with a type. */
    const file = (type = 'image/png'): Blob => new Blob(['12345678'], { type });

    it('posts the raw file with its headers and reports its progress', async () => {
      const rig = await setUp();
      const progress: number[] = [];
      const upload = track(
        rig.api.upload('customer-image', file(), { ...signal(), fileName: 'logo 1.png', onProgress: (share) => progress.push(share) }),
      );
      await flush();
      const xhr = rig.host.uploads[0];
      expect(xhr.method).toBe('POST');
      expect(xhr.url).toBe('/17/blobs/customer-image?fileName=logo%201.png');
      expect(xhr.headers).toMatchObject({
        'Tellma-Client': `web/${UI_VERSION}`,
        'Content-Type': 'image/png',
        'Accept-Language': 'ar-SA',
        'Tellma-Calendar': 'uq',
      });
      expect(xhr.headers['traceparent']).toMatch(TRACEPARENT);
      expect(xhr.body).toBeInstanceOf(Blob);
      xhr.progress(4, 8);
      xhr.progress(8, 8);
      const descriptor = { id: 51, kind: 'customer-image', contentType: 'image/png', size: '8', width: 64, height: 64 };
      xhr.respond(201, descriptor);
      await flush();
      expect(progress).toEqual([0.5, 1]);
      expect(upload.value).toEqual(descriptor);
      expect(rig.log.entries[0].start).toMatchObject({ route: 'blobs/customer-image', tenantId: 17 });
    });

    it('is not sent while the browser is offline', async () => {
      const rig = await setUp();
      rig.host.onLineNow = false;
      const upload = track(rig.api.upload('attachment', file(), { ...signal(), fileName: 'a.png' }));
      await flush();
      expect(rig.host.uploads).toHaveLength(0);
      expect(upload.error).toMatchObject({ kind: 'offline', code: 'not-sent' });
    });

    it('declares a file with no type as octet-stream', async () => {
      const rig = await setUp();
      track(rig.api.upload('attachment', file(''), { ...signal(), fileName: 'notes' }));
      await flush();
      expect(rig.host.uploads[0].headers['Content-Type']).toBe('application/octet-stream');
    });

    it('aborts on the signal and rejects cancelled', async () => {
      const rig = await setUp();
      const controller = new AbortController();
      const upload = track(rig.api.upload('attachment', file(), { signal: controller.signal, fileName: 'a.png' }));
      await flush();
      controller.abort();
      await flush();
      expect(rig.host.uploads[0].aborted).toBe(true);
      expect(upload.error).toMatchObject({ kind: 'cancelled' });
    });

    it('maps a refusal and never retries', async () => {
      const rig = await setUp();
      const upload = track(rig.api.upload('attachment', file(), { ...signal(), fileName: 'a.exe' }));
      await flush();
      rig.host.uploads[0].respond(400, { code: 'blob-rejected', status: 400, instance: 'x', arguments: { reason: 'type' } });
      await vi.advanceTimersByTimeAsync(10_000);
      expect(upload.error).toMatchObject({ kind: 'validation', code: 'blob-rejected' });
      expect(rig.host.uploads).toHaveLength(1);
    });

    it('rejects a network failure as offline, never retried', async () => {
      const rig = await setUp();
      const upload = track(rig.api.upload('attachment', file(), { ...signal(), fileName: 'a.png' }));
      await flush();
      rig.host.uploads[0].error();
      await vi.advanceTimersByTimeAsync(10_000);
      expect(upload.error).toMatchObject({ kind: 'offline', code: 'network' });
      expect(rig.host.uploads).toHaveLength(1);
    });

    it('ends an upload that stalls or whose answer is lost after 60 s, restarting at each progress event', async () => {
      const rig = await setUp();
      const upload = track(rig.api.upload('attachment', file(), { ...signal(), fileName: 'a.png' }));
      await flush();
      const xhr = rig.host.uploads[0];
      for (let i = 1; i <= 3; i++) {
        await vi.advanceTimersByTimeAsync(50_000);
        xhr.progress(i * 2, 8);
      }
      // The whole file is sent; the answer never comes.
      xhr.progress(8, 8);
      await vi.advanceTimersByTimeAsync(59_999);
      expect(upload.settled).toBe(false);
      await vi.advanceTimersByTimeAsync(1);
      expect(xhr.aborted).toBe(true);
      expect(upload.error).toMatchObject({ kind: 'offline', code: 'outcome-unknown' });
      expect(rig.host.uploads).toHaveLength(1);
    });

    it('applies the user check on the answer', async () => {
      const rig = await setUp();
      const upload = track(rig.api.upload('attachment', file(), { ...signal(), fileName: 'a.png' }));
      await flush();
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      rig.host.uploads[0].respond(201, { id: 1 });
      await flush();
      expect(upload.error).toMatchObject({ kind: 'cancelled' });
    });
  });

  describe('send', () => {
    it('sends a keepalive POST whose answer is never read', async () => {
      const rig = await setUp();
      rig.api.send('users/me/preferences', { theme: 'dark' }, { background: true });
      await flush();
      const request = rig.host.last();
      expect(request.url).toBe('/17/api/web/users/me/preferences');
      expect(request.init).toMatchObject({ method: 'POST', keepalive: true, credentials: 'same-origin', body: '{"theme":"dark"}' });
      expect(request.header('Tellma-Activity')).toBe('background');
      expect(request.header('Content-Type')).toBe('application/json');
      expect(request.header('traceparent')).toMatch(TRACEPARENT);
      request.fail();
      await flush();
    });

    it('throws over 16 KB in development and drops the body in production', async () => {
      const rig = await setUp();
      const large = { text: 'x'.repeat(16 * 1024) };
      expect(() => rig.api.send('users/me/preferences', large)).toThrow(/over the 16 KB/);
      rig.host.dev = false;
      rig.api.send('users/me/preferences', large);
      rig.api.send('users/me/preferences', { text: 'x'.repeat(16 * 1024 - 11) });
      expect(rig.host.requests).toHaveLength(1);
    });

    it("goes only under the tab's own user, starting no path", async () => {
      const rig = await setUp();
      rig.sessionHost.cookieJar = '';
      rig.api.send('users/me/preferences', {});
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      rig.api.send('users/me/preferences', {});
      await flush();
      expect(rig.host.requests).toHaveLength(0);
      expect(rig.paths.state()).toBe('active');
      expect(rig.sessionHost.replaced).toEqual([]);
      expect(rig.sessionHost.reloads).toBe(0);
    });

    it('carries the fingerprint once the chunk is loaded', async () => {
      const rig = await setUp();
      rig.api.send('centers/save', {});
      vi.useRealTimers();
      await rig.api.fingerprint('centers/save');
      rig.api.send('centers/save', {});
      expect(rig.host.requests.map((request) => request.header('Tellma-Contract'))).toEqual([null, 'fp-save']);
    });
  });

  describe('the blob fetcher', () => {
    /** The providers of an app whose metadata is never loaded. */
    const app = (): ReturnType<typeof provideTellma> => {
      const unused = (): Promise<never> => Promise.reject(new Error('unused'));
      return provideTellma({ metadata: { core: unused, entities: unused, contracts: unused, strings: {} }, languages: [], features: [] });
    };

    it('is provided by provideTellma over TmApi', async () => {
      const options = { ifNoneMatch: '"v1"' };
      const result = { status: 304, blob: null, etag: '"v1"' };
      const fetchBlob = vi.fn(() => Promise.resolve(result));
      TestBed.configureTestingModule({ providers: [app(), { provide: TmApi, useValue: { fetchBlob } }] });
      await expect(TestBed.inject(TM_BLOB_FETCHER)('/17/blobs/51', options)).resolves.toBe(result);
      expect(fetchBlob).toHaveBeenCalledWith('/17/blobs/51', options);
    });

    it('is answered by TmFakeApi under provideTellmaTesting', async () => {
      TestBed.configureTestingModule({ providers: [app(), provideTellmaTesting()] });
      const fake = TestBed.inject(TmFakeApi);
      fake.respond('/17/blobs/51', { status: 200, blob: new Blob(['png']), etag: '"v3"' });
      await expect(TestBed.inject(TM_BLOB_FETCHER)('/17/blobs/51')).resolves.toMatchObject({ status: 200, etag: '"v3"' });
      expect(fake.calls()).toMatchObject([{ kind: 'fetch-blob', route: '/17/blobs/51' }]);
    });

    it('revalidates with If-None-Match and reports a 304 with no body', async () => {
      const rig = await setUp();
      const fetched = track(rig.api.fetchBlob('/17/blobs/51?size=64', { ifNoneMatch: '"v1"' }));
      await flush();
      const request = rig.host.last();
      expect(request.init.method).toBe('GET');
      expect(request.header('If-None-Match')).toBe('"v1"');
      expect(request.header('Tellma-Client')).toBe(`web/${UI_VERSION}`);
      request.answer(tmAnswer(304, undefined, { ETag: '"v1"' }));
      await flush();
      expect(fetched.value).toEqual({ status: 304, blob: null, etag: '"v1"' });
      expect(rig.log.entries).toHaveLength(0);
    });

    it('fetches the bytes of a 200', async () => {
      const rig = await setUp();
      const fetched = track(rig.api.fetchBlob('/17/blobs/51'));
      await flush();
      rig.host.last().answer(new Response(new Blob(['png']), { status: 200, headers: { ETag: '"v2"' } }));
      await flush();
      expect(fetched.value).toMatchObject({ status: 200, etag: '"v2"' });
    });

    it("honours the caller's signal and never retries", async () => {
      const rig = await setUp();
      const controller = new AbortController();
      const aborted = track(rig.api.fetchBlob('/17/blobs/51', { signal: controller.signal }));
      await flush();
      controller.abort();
      await flush();
      expect(aborted.error).toBe(controller.signal.reason);
      expect(rig.host.requests[0].aborted).toBe(true);
      const failed = track(rig.api.fetchBlob('/17/blobs/52'));
      await flush();
      rig.host.last().fail();
      await vi.advanceTimersByTimeAsync(10_000);
      expect(failed.error).toMatchObject({ kind: 'offline', code: 'network' });
      expect(rig.host.requests).toHaveLength(2);
    });

    it('applies the user check at both ends, and maps a refusal', async () => {
      const rig = await setUp();
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      const changed = track(rig.api.fetchBlob('/17/blobs/51'));
      await flush();
      expect(changed.error).toMatchObject({ kind: 'cancelled' });
      expect(rig.host.requests).toHaveLength(0);
      rig.sessionHost.cookieJar = tmProfileCookies(FARAH, 'ar');
      const missing = track(rig.api.fetchBlob('/17/blobs/51'));
      await flush();
      rig.host.last().answer(tmProblemAnswer(404, 'not-found'));
      await flush();
      expect(missing.error).toMatchObject({ kind: 'not-found' });
      const swapped = track(rig.api.fetchBlob('/17/blobs/53'));
      await flush();
      rig.sessionHost.cookieJar = tmProfileCookies(SARA);
      rig.host.last().answer(new Response(new Blob(['png']), { status: 200 }));
      await flush();
      expect(swapped.error).toMatchObject({ kind: 'cancelled' });
      expect(swapped.value).toBeUndefined();
    });
  });
});
