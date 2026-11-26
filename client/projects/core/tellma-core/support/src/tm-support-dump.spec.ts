// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import {
  TmClientStore,
  type TmErrorRecord,
  type TmJson,
  TmPageContexts,
  TmRequestLog,
  TmSupport,
  TmTelemetry,
  type TmTelemetryItem,
  ɵTM_DRAFTS_STORAGE,
  type ɵTmStateDumpSection,
  type ɵTmSupportSnapshot,
} from '@tellma/core';
import { TmFakeTelemetry, tmWarmTenant } from '@tellma/core-testing';
import { type TmPageContextSnapshot, tmSerializePageContext } from '@tellma/core/page-snapshot';

import { TM_T17, TM_T17_DOCS, tmAppRig, tmNavigate, tmSettle, tmSettleUntil } from '../../src/routes/tm-app.testing.util';
import { TM_SUPPORT_DUMP_MAX_BYTES, tmBuildSupportDump, tmDownloadSupportDump, tmSupportDumpFileName, type TmSupportDump } from './tm-support-dump';

// The support dump: what it holds, the routed tenant's records alone after
// a tenant switch, the contributed sections' failures and cap, the four
// truncation steps in their order, what it never holds, the page's URL with
// and without page data, the storage estimate's time box, and the
// diagnostic file's name and download.

/** A string of a given length in bytes. */
function filler(bytes: number): string {
  return 'x'.repeat(bytes);
}

/** The UTF-8 length of a value's compact JSON. */
function size(value: unknown): number {
  return new TextEncoder().encode(JSON.stringify(value)).length;
}

/** Snapshots the page contexts now, as the support facade does in the press. */
function snapshotNow(): ɵTmSupportSnapshot<TmPageContextSnapshot> {
  const injector = TestBed.inject(Injector);
  const contexts = TestBed.inject(TmPageContexts);
  const current = contexts.current();
  const read = (includeData: boolean): ɵTmSupportSnapshot<TmPageContextSnapshot>['withData'] => ({
    current: current === null ? null : tmSerializePageContext(current, { includeData, injector }),
    overlays: contexts.overlays().map((context) => tmSerializePageContext(context, { includeData, injector })),
  });
  return {
    at: Date.now(),
    withData: read(true),
    withoutData: read(false),
    url: TestBed.inject(Router).url,
    routeTemplate: current?.routeTemplate ?? '',
    historyLength: history.length,
  };
}

/** A context snapshot of a list page, with page data when rows are given. */
function listContext(rows: string | null, state: TmJson = {}): TmPageContextSnapshot {
  return {
    kind: 'search',
    routeTemplate: '/:t/centers',
    overlay: false,
    entity: 'gl.Center',
    title: 'Centers',
    status: 'ready',
    state,
    visible: { columns: ['Code'], rowCount: 1, total: 1, ...(rows === null ? {} : { rows: [{ id: 1, cells: { Code: rows } }] }) },
    errors: [],
    actions: [],
  };
}

/** A hand-made snapshot of a list page. */
function listSnapshot(rows: string, state: TmJson = {}): ɵTmSupportSnapshot<TmPageContextSnapshot> {
  return {
    at: Date.parse('2026-10-20T11:03:12.512Z'),
    withData: { current: listContext(rows, state), overlays: [] },
    withoutData: { current: listContext(null), overlays: [] },
    url: '/17/centers?q=al%20faris',
    routeTemplate: '/:t/centers',
    historyLength: 3,
  };
}

/** Starts the app in tenant 17 on a page with the support host landed, as before any dump, and gives the injector. */
async function setUp(url = '/17/credit-review?q=al%20faris'): Promise<Injector> {
  await tmAppRig();
  await tmNavigate(url);
  const support = TestBed.inject(TmSupport);
  support.ɵstart();
  await tmSettleUntil(() => support.ɵsections().some((section) => section.name === 'state'), 'the support host lands');
  await tmSettle();
  return TestBed.inject(Injector);
}

describe('tmBuildSupportDump', () => {
  it('describes the tab: versions, distribution, session, locale, environment, page, contexts, requests, errors, telemetry and sections', async () => {
    const injector = await setUp();
    const log = TestBed.inject(TmRequestLog);
    log.begin({ traceId: '4bf92f3577b34da6a3ce929d0e0e4736', route: 'centers/save', tenantId: 17, method: 'POST', background: false }).end({ outcome: 'error', status: 422, code: 'validation' });
    const record: TmErrorRecord = { source: 'render', errorClass: 'TypeError', fingerprint: 'TypeError@main.js:1:2', routeTemplate: '/:t/credit-review', stack: '', at: 1, tenantId: 17, recentTraceIds: [] };
    vi.spyOn(TestBed.inject(TmTelemetry), 'ɵerrorRecords').mockReturnValue([record]);
    TestBed.inject(TmTelemetry).event('acme.test.ping', { n: 1 });

    const dump = await tmBuildSupportDump({ ...snapshotNow(), reported: { traceId: '4bf92f3577b34da6a3ce929d0e0e4736', kind: 'validation', code: 'validation' } }, { includeData: true, injector });

    expect(dump).toMatchObject({
      format: 'tellma-support-dump',
      formatVersion: 1,
      includesPageData: true,
      truncated: [],
      reported: { traceId: '4bf92f3577b34da6a3ce929d0e0e4736', kind: 'validation', code: 'validation' },
      versions: { worker: null, server: null, features: expect.any(Array) },
      distribution: { slug: expect.any(String), displayName: expect.any(String) },
      session: { sub: 'test-user', tenantId: 17, tabId: expect.any(String), state: 'active', online: true },
      locale: {
        language: 'en',
        dir: 'ltr',
        calendar: 'gc',
        timeZone: TM_T17_DOCS.settings.timeZone,
        deviceTimeZone: expect.any(String),
        zoneAuto: false,
        theme: expect.any(String),
        effectiveTheme: expect.stringMatching(/^(light|dark)$/),
      },
      environment: {
        userAgent: navigator.userAgent,
        languages: [...navigator.languages],
        viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
        standalone: false,
        reducedMotion: expect.any(Boolean),
        cookies: navigator.cookieEnabled,
      },
      page: { url: '/17/credit-review?q=al%20faris', routeTemplate: '/:t/credit-review', historyLength: history.length },
      contexts: { current: expect.objectContaining({ routeTemplate: '/:t/credit-review' }), overlays: [] },
      errors: [record],
      sections: { state: expect.objectContaining({ store: expect.objectContaining({ mode: 'memory' }) }) },
    });
    expect(dump.createdAt).toMatch(/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}[+-]\d\d:\d\d$/);
    expect(dump.requests[0]).toMatchObject({ traceId: '4bf92f3577b34da6a3ce929d0e0e4736', route: 'centers/save', outcome: 'error', status: 422 });
    expect(dump.telemetry.map((item) => item.name)).toContain('acme.test.ping');
    expect(dump.environment.storage === null || typeof dump.environment.storage.quota === 'number').toBe(true);
  });

  it("holds only the routed tenant's requests, errors, telemetry, draft keys and page-state keys after a tenant switch, and those of no tenant", async () => {
    const injector = await setUp('/17/centers');
    // --- Tenant 18 warm beside tenant 17 ---
    const beta = { ...TM_T17_DOCS, settings: { ...TM_T17_DOCS.settings, tenantId: 18, names: ['Beta', null] as const }, tenant: { ...TM_T17_DOCS.tenant, id: 18 } };
    await tmWarmTenant(TestBed.inject(TmClientStore), { ...TM_T17, tenantId: 18 }, beta);

    // --- Each tenant's records, and some of none ---
    const log = TestBed.inject(TmRequestLog);
    const request = (traceId: string, tenantId: number | null): void =>
      log.begin({ traceId, route: 'centers/query', tenantId, method: 'GET', background: false }).end({ outcome: 'ok', status: 200 });
    request('17'.repeat(16), 17);
    request('18'.repeat(16), 18);
    request('00'.repeat(15) + '01', null);
    const error = (fingerprint: string, tenantId: number | null): TmErrorRecord => ({
      source: 'render',
      errorClass: 'TypeError',
      fingerprint,
      routeTemplate: '/:t/centers',
      stack: '',
      at: 1,
      tenantId,
      recentTraceIds: [],
    });
    const telemetry = TestBed.inject(TmTelemetry);
    vi.spyOn(telemetry, 'ɵerrorRecords').mockReturnValue([error('TypeError@17', 17), error('TypeError@18', 18), error('TypeError@none', null)]);
    const item = (name: string, tenantId: number | null): TmTelemetryItem => ({ kind: 'event', name, at: 1, tenantId, props: {} });
    vi.spyOn(telemetry, 'recent').mockReturnValue([item('acme.seventeen', 17), item('acme.eighteen', 18), item('acme.none', null)]);
    const drafts = TestBed.inject(ɵTM_DRAFTS_STORAGE)!;
    drafts.setItem('tm.draft:test-user:17:gl.Center:5', '{}');
    drafts.setItem('tm.draft:test-user:18:gl.Center:6', '{}');
    const { TmPageState, tmPageStateKey } = await import('@tellma/core/memory');
    const pages = TestBed.inject(TmPageState);
    pages.remember(tmPageStateKey('/17/centers'), { rows: [] });
    pages.remember(tmPageStateKey('/18/centers'), { rows: [] });

    // --- The switch, then a dump on tenant 18 ---
    await TestBed.inject(Router).navigateByUrl('/18');
    await tmSettleUntil(() => TestBed.inject(Router).url === '/18', 'the tenant switches');
    const dump = await tmBuildSupportDump(snapshotNow(), { includeData: false, injector });

    expect(dump.session.tenantId).toBe(18);
    expect(dump.requests.map((record) => record.tenantId)).toEqual([null, 18]);
    expect(dump.errors.map((record) => record.fingerprint)).toEqual(['TypeError@18', 'TypeError@none']);
    expect(dump.telemetry.map((record) => record.name)).toEqual(['acme.eighteen', 'acme.none']);
    const state = dump.sections['state'] as unknown as ɵTmStateDumpSection;
    expect(state.drafts).toEqual(['18:gl.Center:6']);
    expect(state.pageState).toEqual(['/18/centers']);
    expect(JSON.stringify(dump)).not.toContain('17'.repeat(16));
    drafts.removeItem('tm.draft:test-user:17:gl.Center:5');
    drafts.removeItem('tm.draft:test-user:18:gl.Center:6');
  });

  it("keeps the URL's query with page data and the path alone without", async () => {
    const injector = await setUp();
    const snapshot = snapshotNow();
    const withData = await tmBuildSupportDump(snapshot, { includeData: true, injector });
    const without = await tmBuildSupportDump(snapshot, { includeData: false, injector });
    expect(withData.page.url).toBe('/17/credit-review?q=al%20faris');
    expect(without.page.url).toBe('/17/credit-review');
    expect(without.includesPageData).toBe(false);
    expect(without.contexts).toEqual(snapshot.withoutData);
    expect(withData.contexts).toEqual(snapshot.withData);
  });

  it('records a throwing reader by its error class and a section over 64 KB as truncated', async () => {
    const injector = await setUp();
    const support = TestBed.inject(TmSupport);
    support.registerDumpSection('broken', () => {
      throw new RangeError('no');
    });
    support.registerDumpSection('huge', () => filler(65_537));
    support.registerDumpSection('fits', () => ({ text: filler(60_000) }));
    support.registerDumpSection('nothing', () => undefined as unknown as TmJson);

    const dump = await tmBuildSupportDump(snapshotNow(), { includeData: false, injector });

    expect(dump.sections['broken']).toEqual({ error: 'RangeError' });
    expect(dump.sections['huge']).toEqual({ truncated: true });
    expect(dump.sections['fits']).toEqual({ text: filler(60_000) });
    expect(dump.sections['nothing']).toBeNull();
    expect(dump.truncated).toEqual(['huge']);
  });

  it('drops the visible rows, the telemetry, the page data and the sections in that order, each only while over 2 MiB', async () => {
    const injector = await setUp();
    const big = TM_SUPPORT_DUMP_MAX_BYTES + 1000;

    // --- Step 1 alone: the rows ---
    let dump = await tmBuildSupportDump(listSnapshot(filler(big)), { includeData: true, injector });
    expect(dump.truncated).toEqual(['visibleRows']);
    expect(dump.contexts.current?.visible).toEqual({ columns: ['Code'], rowCount: 1, total: 1 });
    expect(dump.includesPageData).toBe(true);
    expect(dump.page.url).toBe('/17/centers?q=al%20faris');

    // --- Steps 1 and 2: the telemetry too ---
    const telemetry = TestBed.inject(TmFakeTelemetry);
    for (let i = 0; i < 100; i++) {
      telemetry.event('acme.test.big', { blob: filler(25_000) });
    }
    dump = await tmBuildSupportDump(listSnapshot(filler(big)), { includeData: true, injector });
    expect(dump.truncated).toEqual(['visibleRows', 'telemetry']);
    expect(dump.telemetry).toEqual([]);
    expect(dump.includesPageData).toBe(true);

    // --- Steps 1 to 3: the rest of the page data ---
    dump = await tmBuildSupportDump(listSnapshot(filler(big), { query: { q: filler(big) } }), { includeData: true, injector });
    expect(dump.truncated).toEqual(['visibleRows', 'telemetry', 'pageData']);
    expect(dump.includesPageData).toBe(false);
    expect(dump.page.url).toBe('/17/centers');
    expect(dump.contexts.current).toEqual(listContext(null));

    // --- All four: the sections ---
    const support = TestBed.inject(TmSupport);
    for (let i = 0; i < 40; i++) {
      support.registerDumpSection(`big-${i}`, () => filler(60_000));
    }
    dump = await tmBuildSupportDump(listSnapshot(filler(big), { query: { q: filler(big) } }), { includeData: true, injector });
    expect(dump.truncated).toEqual(['visibleRows', 'telemetry', 'pageData', 'sections']);
    expect(dump.sections).toEqual({});
    expect(size(dump)).toBeLessThanOrEqual(TM_SUPPORT_DUMP_MAX_BYTES);
  });

  it('never holds cookies, the profile, storage contents, launch documents, drafts or request bodies', async () => {
    localStorage.setItem('tm-test-secret', 'local-secret-value');
    sessionStorage.setItem('tm.draft.test-user.17:gl.Center:5', JSON.stringify({ Name: 'draft-secret-value' }));
    document.cookie = 'tm-test-cookie=cookie-secret-value; path=/';
    try {
      const injector = await setUp();
      const log = TestBed.inject(TmRequestLog);
      log.begin({ traceId: '0af7651916cd43dd8448eb211c80319c', route: 'centers/save', tenantId: 17, method: 'POST', background: false }).end({ outcome: 'ok', status: 200 });
      const text = JSON.stringify(await tmBuildSupportDump(snapshotNow(), { includeData: true, injector }));
      for (const secret of [
        'local-secret-value',
        'draft-secret-value',
        'cookie-secret-value',
        'tm-test-cookie',
        'test.user@example.com',
        'Test User',
        TM_T17_DOCS.me.user.name,
        'preferencesTag',
      ]) {
        expect(text, secret).not.toContain(secret);
      }
    } finally {
      localStorage.removeItem('tm-test-secret');
      sessionStorage.removeItem('tm.draft.test-user.17:gl.Center:5');
      document.cookie = 'tm-test-cookie=; path=/; max-age=0';
    }
  });

  it('gives the storage estimate 200 ms, then reads it as unknown', async () => {
    const injector = await setUp();
    vi.spyOn(navigator.storage, 'estimate').mockReturnValue(new Promise(() => undefined));
    const started = performance.now();
    const dump = await tmBuildSupportDump(snapshotNow(), { includeData: false, injector });
    expect(dump.environment.storage).toBeNull();
    expect(performance.now() - started).toBeLessThan(1000);
  });
});

describe('the diagnostic file', () => {
  it("is named by the distribution's slug and the dump's local time", () => {
    const dump = { createdAt: '2026-10-20T14:03:12.512+03:00', distribution: { slug: 'acme', displayName: 'Acme' } } as TmSupportDump;
    expect(tmSupportDumpFileName(dump)).toBe('acme-diagnostics-20261020-140312.json');
    expect(tmSupportDumpFileName({ ...dump, distribution: { slug: '', displayName: '' } })).toBe('tellma-diagnostics-20261020-140312.json');
  });

  it('downloads through a temporary link and an object URL revoked later, and records whether it holds page data', async () => {
    const injector = await setUp();
    const dump = await tmBuildSupportDump(snapshotNow(), { includeData: false, injector });
    const clicks: { readonly name: string; readonly href: string; readonly attached: boolean }[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      clicks.push({ name: this.download, href: this.href, attached: this.isConnected });
    });
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    vi.useFakeTimers({ toFake: ['setTimeout'] });
    try {
      tmDownloadSupportDump(dump, injector);
      expect(clicks).toEqual([{ name: tmSupportDumpFileName(dump), href: expect.stringMatching(/^blob:/), attached: true }]);
      expect(document.querySelector('a[download]')).toBeNull();
      expect(revoke).not.toHaveBeenCalled();
      vi.runAllTimers();
      expect(revoke).toHaveBeenCalledWith(clicks[0]!.href);
    } finally {
      vi.useRealTimers();
    }
    expect(TestBed.inject(TmFakeTelemetry).recent().filter((item) => item.name === 'tm.support.dump-downloaded')).toEqual([
      expect.objectContaining({ props: { pageData: false } }),
    ]);
  });
});
