// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, createEnvironmentInjector, DestroyRef, EnvironmentInjector, input } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { TmFakeApi, TmFakeSession, TmFakeTelemetry, TmSupportOfflineHarness } from '@tellma/core-testing';
import { TmModalHarness } from '@tellma/core-ui-testing';

import { TmApiError } from '../api/tm-api-error';
import { tmDevModeProvider } from '../app/tm-dev-mode.testing.util';
import type { TmJsonObject } from '../json/tm-json';
import { TmRequestLog } from '../requests/tm-request-log';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmUrls } from '../urls/tm-urls';
import type { TmDistributionInfo } from '../state/tm-distribution-info';
import { TM_T17_DOCS, TM_TEST_SUB, tmAppRig, tmNavigate, tmSettle, tmSettleUntil, tmTestMembership } from '../routes/tm-app.testing.util';
import { TM_SUPPORT_LOAD, TM_SUPPORT_TOOLS_LOAD, TmSupport, type TmSupportModule, type TmSupportSnapshot, type TmSupportTools } from './tm-support';
import * as host from './tm-support-host';

// The support facade: the dump's contributed sections and their rules, when
// the user can send a request, and the support dialog opening in the
// press's frame over a snapshot of the page, then loading the support
// entry point into it, or showing the offline line when it cannot load.

/** The snapshots the support entry point's stand-in received, oldest first. */
const shown: TmSupportSnapshot[] = [];

/** The builds the stand-in ran, oldest first. */
const built: { readonly snapshot: TmSupportSnapshot; readonly includeData: boolean }[] = [];

/** The dumps the stand-in downloaded. */
const downloaded: TmJsonObject[] = [];

/** The support entry point's dialog, standing in: it records the snapshot it is given. */
@Component({ selector: 'tm-test-support-dialog', template: `<p class="tm-test-support-dialog">{{ snapshot().url }}</p>` })
class TmTestSupportDialog {
  /** The snapshot. */
  readonly snapshot = input.required<TmSupportSnapshot>();
  /** The error. */
  readonly error = input<TmApiError | undefined>(undefined);

  /** Records the snapshot. */
  constructor() {
    queueMicrotask(() => shown.push(this.snapshot()));
  }
}

/** Holds the stand-in's load until it opens, while set. */
let gate: Promise<void> | null = null;

/** The support entry point, standing in. */
const SUPPORT: TmSupportModule = {
  TmSupportDialog: TmTestSupportDialog,
  tmBuildSupportDump: (snapshot, { includeData }) => {
    built.push({ snapshot, includeData });
    return Promise.resolve({ format: 'tellma-support-dump' });
  },
  tmDownloadSupportDump: (dump) => void downloaded.push(dump),
};

/** The distribution document with support requests on or off. */
function distribution(enabled: boolean): TmDistributionInfo {
  return { support: { enabled, providerAccess: false } } as TmDistributionInfo;
}

/** How a test sets the app up. */
interface TmRigOptions {
  /** Whether the distribution enables support requests. */
  readonly enabled?: boolean;
  /** The support entry point's loader, when not the stand-in. */
  readonly load?: () => Promise<TmSupportModule>;
  /** The support host's loader, when not the real one. */
  readonly tools?: () => Promise<TmSupportTools>;
  /** Whether the app runs in development mode. */
  readonly dev?: boolean;
  /** Tenant 17's state. */
  readonly state?: 'Active' | 'ReadOnly' | 'Frozen';
  /** Whether the user's membership of tenant 17 is active. */
  readonly member?: boolean;
}

/** Starts the app with the distribution document stored, and gives the facade. */
async function setUp(options: TmRigOptions = {}): Promise<TmSupport> {
  shown.length = 0;
  built.length = 0;
  downloaded.length = 0;
  gate = null;
  const document = distribution(options.enabled ?? true);
  const rig = await tmAppRig({
    docs: { ...TM_T17_DOCS, tenant: { ...TM_T17_DOCS.tenant, state: options.state ?? 'Active' } },
    tenants: [tmTestMembership(17, { isActive: options.member ?? true })],
    providers: [
      { provide: TM_SUPPORT_LOAD, useValue: options.load ?? (() => (gate ?? Promise.resolve()).then(() => SUPPORT)) },
      ...(options.tools === undefined ? [] : [{ provide: TM_SUPPORT_TOOLS_LOAD, useValue: options.tools }]),
      ...(options.dev === undefined ? [] : [tmDevModeProvider(options.dev)]),
    ],
  });
  await TestBed.inject(TmClientStore).put({ kind: 'user', sub: TM_TEST_SUB }, 'distribution', { tag: null, savedAt: Date.now(), data: document });
  rig.api.respond('/api/distribution-info', document);
  return TestBed.inject(TmSupport);
}

describe('TmSupport', () => {
  describe('the dump sections', () => {
    it("registers the client state's section", async () => {
      const support = await setUp();
      const state = support.ɵsections().find((section) => section.name === 'state');
      expect(state?.read()).toMatchObject({ store: { mode: 'memory' }, leader: { isLeader: false }, drafts: [] });
    });

    it('refuses a malformed, reserved or taken name in development', async () => {
      const support = await setUp({ dev: true });
      for (const name of ['', 'Acme', '1st', 'acme_x', `a${'b'.repeat(32)}`, 'telemetry', 'sections', 'state']) {
        expect(() => support.registerDumpSection(name, () => null), name).toThrow(expect.objectContaining({ name: 'TmSupportError' }));
      }
      support.registerDumpSection(`a${'b'.repeat(31)}`, () => 1);
      support.registerDumpSection('acme-exposure-2', () => 2);
      expect(support.ɵsections().map((section) => section.name)).toEqual(['state', `a${'b'.repeat(31)}`, 'acme-exposure-2']);
    });

    it('keeps the first registration of a name in production', async () => {
      const support = await setUp({ dev: false });
      support.registerDumpSection('acme', () => 'first');
      support.registerDumpSection('acme', () => 'second');
      support.registerDumpSection('Bad Name', () => 'bad');
      expect(support.ɵsections().map((section) => [section.name, section.read()])).toEqual([
        ['state', expect.anything()],
        ['acme', 'first'],
      ]);
    });

    it('removes a section when its owner ends, and never a later registration of its name', async () => {
      const support = await setUp({ dev: true });
      const first = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
      support.registerDumpSection('acme', () => 'first', first.get(DestroyRef));
      first.destroy();
      expect(support.ɵsections().map((section) => section.name)).toEqual(['state']);

      const second = createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
      support.registerDumpSection('acme', () => 'second', second.get(DestroyRef));
      expect(support.ɵsections().find((section) => section.name === 'acme')?.read()).toBe('second');
      second.destroy();
      expect(support.ɵsections().map((section) => section.name)).toEqual(['state']);
    });
  });

  describe('canSend', () => {
    it('is true for an active member of an Active tenant, online, with support on', async () => {
      const support = await setUp();
      await tmNavigate('/17');
      await tmSettleUntil(() => support.canSend());
      expect(support.canSend()).toBe(true);
    });

    it('is false offline, after the session and off a tenant route', async () => {
      const support = await setUp();
      const harness = await tmNavigate('/17');
      await tmSettleUntil(() => support.canSend());
      TestBed.inject(TmFakeApi).setOnline(false);
      expect(support.canSend()).toBe(false);
      TestBed.inject(TmFakeApi).setOnline(true);
      TestBed.inject(TmFakeSession).setState('expired');
      expect(support.canSend()).toBe(false);
      TestBed.inject(TmFakeSession).setState('active');
      expect(support.canSend()).toBe(true);
      await harness.navigateByUrl(TestBed.inject(TmUrls).picker());
      await tmSettleUntil(() => !support.canSend());
    });

    for (const [name, options] of [
      ['with support off', { enabled: false }],
      ['on a ReadOnly tenant', { state: 'ReadOnly' }],
      ['on a Frozen tenant', { state: 'Frozen' }],
      ["for a deactivated member's session", { member: false }],
    ] as const) {
      it(`is false ${name}`, async () => {
        const support = await setUp(options);
        await tmNavigate('/17');
        await tmSettle();
        expect(support.canSend()).toBe(false);
      });
    }

  });

  describe('open', () => {
    it("opens the dialog in the press's frame over a snapshot of the page, then loads the support entry point into it", async () => {
      const support = await setUp();
      const harness = await tmNavigate('/17/credit-review?q=al%20faris');
      await tmSettle();
      // The first press waits for the support host; it lands, and the dialog shows the entry point.
      support.open();
      await tmSettleUntil(() => document.querySelector('.tm-test-support-dialog') !== null);
      const modal = await TestbedHarnessEnvironment.documentRootLoader(harness.fixture).getHarness(TmModalHarness);
      expect(await modal.getTitle()).toBe('Report a problem');
      await modal.close();
      await tmSettleUntil(() => document.querySelector('tm-support-host') === null);

      // --- A later press, the host loaded: the dialog and its placeholder are there before any await ---
      let open!: () => void;
      gate = new Promise<void>((done) => (open = done));
      support.open({ error: new TmApiError({ kind: 'server', status: 500, code: 'server.internal', traceId: '4bf92f3577b34da6a3ce929d0e0e4736' }) });
      // The dialog's panel is attached at once, and its placeholder renders with the same task's change detection.
      expect(document.querySelector('.tm-modal-panel')).not.toBeNull();
      TestBed.tick();
      expect(document.querySelector('tm-support-host [aria-busy="true"]')).not.toBeNull();
      open();
      await tmSettleUntil(() => shown.length === 2);

      // --- The snapshot: the page's address with its query, both serialisations, the reported error ---
      const [first, second] = shown;
      expect(first).toMatchObject({ url: '/17/credit-review?q=al%20faris', routeTemplate: '/:t/credit-review' });
      expect(first!.withData.current).toMatchObject({ kind: 'custom', routeTemplate: '/:t/credit-review', overlay: false });
      expect(first!.withoutData.current).toMatchObject({ kind: 'custom', routeTemplate: '/:t/credit-review' });
      expect(first!.reported).toBeUndefined();
      expect(second!.reported).toEqual({ traceId: '4bf92f3577b34da6a3ce929d0e0e4736', kind: 'server', code: 'server.internal' });
      expect(first!.historyLength).toBeGreaterThan(0);
    });

    it("shows the offline line with Copy diagnostics' keys when the support entry point does not load", async () => {
      const support = await setUp({ load: () => Promise.reject(new TypeError('Failed to fetch dynamically imported module')) });
      const harness = await tmNavigate('/17');
      await tmSettle();
      support.open();
      await tmSettleUntil(() => document.querySelector('tm-support-offline') !== null);
      await tmSettle();
      const line = await TestbedHarnessEnvironment.documentRootLoader(harness.fixture).getHarness(TmSupportOfflineHarness);
      expect(await line.getText()).toMatch(/^This isn't available offline\. Press .+ to copy diagnostics\.$/);
      expect((await line.getKeys()).length).toBeGreaterThan(1);
      const close = document.querySelector<HTMLButtonElement>('tm-support-host .tm-modal__footer button')!;
      expect(close.textContent?.trim()).toBe('Close');
      close.click();
      await tmSettleUntil(() => document.querySelector('tm-support-host') === null);
    });

    it("preloads the support host when the app's startup goes idle, so the first press opens the dialog in its frame", async () => {
      let loads = 0;
      let landed = false;
      const support = await setUp({
        tools: () => {
          loads++;
          return import('@tellma/core/page-snapshot' as string).then((pages) => {
            landed = true;
            return { host, pages: pages as TmSupportTools['pages'] };
          });
        },
      });
      await tmNavigate('/17');
      // Nothing was pressed: the startup's idle step loaded the host.
      await tmSettleUntil(() => landed);
      expect(loads).toBe(1);
      support.open();
      expect(document.querySelector('.tm-modal-panel')).not.toBeNull();
      await tmSettleUntil(() => document.querySelector('.tm-test-support-dialog') !== null);
      expect(loads).toBe(1);
    });

    it('opens one dialog and downloads one file for presses made before the support host lands', async () => {
      let release!: () => void;
      const held = new Promise<void>((done) => (release = done));
      const support = await setUp({
        tools: () => held.then(() => import('@tellma/core/page-snapshot' as string)).then((pages) => ({ host, pages: pages as TmSupportTools['pages'] })),
      });
      await tmNavigate('/17');
      await tmSettle();
      // A double press on each, while the host is still loading.
      support.open();
      support.open();
      const first = support.downloadDump();
      const second = support.downloadDump();
      expect(document.querySelector('tm-support-host')).toBeNull();
      release();
      await Promise.all([first, second]);
      await tmSettleUntil(() => shown.length > 0);
      await tmSettle();
      expect(document.querySelectorAll('tm-support-host').length).toBe(1);
      expect(shown.length).toBe(1);
      expect(downloaded.length).toBe(1);
    });

    it('reports a failed load of the support host, and tries it again on the next press', async () => {
      let loads = 0;
      const support = await setUp({
        tools: () =>
          ++loads <= 2 ? Promise.reject(new TypeError('Failed to fetch dynamically imported module')) : import('@tellma/core/page-snapshot' as string).then((pages) => ({ host, pages: pages as TmSupportTools['pages'] })),
      });
      await tmNavigate('/17');
      // The startup's idle preload fails first; the press tries again, and fails too.
      await tmSettleUntil(() => loads === 1);
      support.open();
      await tmSettleUntil(() => loads === 2);
      await tmSettle();
      expect(document.querySelector('tm-support-host')).toBeNull();
      expect(TestBed.inject(TmFakeTelemetry).recent().filter((item) => item.name === 'tm.error.captured')).toEqual([
        expect.objectContaining({ props: expect.objectContaining({ where: 'tm.support.open' }) }),
      ]);
      // The next press loads it.
      support.open();
      await tmSettleUntil(() => document.querySelector('.tm-test-support-dialog') !== null);
      expect(loads).toBe(3);
    });
  });

  describe('Copy diagnostics and the diagnostic file', () => {
    it('copies through the request log, naming the trigger', async () => {
      const support = await setUp();
      const copy = vi.spyOn(TestBed.inject(TmRequestLog), 'copy').mockImplementation(() => undefined);
      support.copyDiagnostics();
      support.copyDiagnostics('dialog');
      expect(copy.mock.calls).toEqual([['button'], ['dialog']]);
    });

    it('builds the file from a snapshot taken now, without page data, and downloads it', async () => {
      const support = await setUp();
      await tmNavigate('/17/credit-review?q=x');
      await tmSettle();
      support.downloadDump();
      await tmSettleUntil(() => downloaded.length === 1);
      expect(built).toEqual([{ snapshot: expect.objectContaining({ url: '/17/credit-review?q=x' }), includeData: false }]);
      expect(downloaded).toEqual([{ format: 'tellma-support-dump' }]);
    });
  });
});
