// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { VersionEvent } from '@angular/service-worker';
import { TmFakeTelemetry } from '@tellma/core-testing';
import { TM_NOW, TmBanners, ɵTM_API_HOST } from '@tellma/core';
import { Subject } from 'rxjs';

import { TmFakeApiHost } from '../../src/api/tm-api.testing.util';
import { tmAppRig } from '../../src/routes/tm-app.testing.util';
import { TM_UPDATE_HIDDEN_MS, TM_UPDATE_INTERVAL_MS, TM_UPDATES_HOST, TmUpdates, type TmUpdatesContainer } from './tm-updates';
import { TM_REGISTER_WAIT_MS, tmWorkerScriptUrl, type TmScriptUrlRules, type TmTrustedTypes } from './tm-worker-registration';

// The update service over a scripted worker: Angular's update service is a
// stand-in whose events and check answers the spec plays, and the page's
// container, Trusted Types and reload are recorded.

/** The UI version the test metadata names: this page's. */
const OURS = '1.0.0+0123abc';

/** A worker version as the worker's events name it. */
function version(name: string | null, hash = 'h-' + (name ?? 'none')): { hash: string; appData?: object } {
  return name === null ? { hash } : { hash, appData: { version: name } };
}

/** Angular's update service, scripted. */
class TmScriptedSwUpdate {
  /** The worker's version events. */
  readonly versionUpdates = new Subject<VersionEvent>();
  /** The worker's broken-cache events. */
  readonly unrecoverable = new Subject<{ type: 'UNRECOVERABLE_STATE'; reason: string }>();
  /** What each check plays before it answers: the events, then the answer or a failure. */
  script: () => Promise<boolean> = () => Promise.resolve(false);
  /** How many checks ran. */
  checks = 0;

  /** Runs a check through the script. */
  checkForUpdate(): Promise<boolean> {
    this.checks++;
    return this.script();
  }
}

/** The page's worker container, recorded. */
class TmRecordingContainer implements TmUpdatesContainer {
  /** The controlling worker's messages, or `null` for no controller. */
  controller: { postMessage(message: unknown): void; readonly messages: unknown[] } | null = {
    messages: [],
    postMessage(message: unknown) {
      this.messages.push(message);
    },
  };
  /** The registrations, in order. */
  readonly registered: { url: unknown; options: RegistrationOptions }[] = [];
  /** What a registration does: resolve, or reject. */
  refuse = false;
  readonly #listeners = new Set<() => void>();

  /** Records a registration. */
  register(url: never, options: RegistrationOptions): Promise<unknown> {
    this.registered.push({ url, options });
    return this.refuse ? Promise.reject(new DOMException('Refused.', 'SecurityError')) : Promise.resolve({});
  }

  /** Records a listener. */
  addEventListener(_type: 'controllerchange', listener: () => void): void {
    this.#listeners.add(listener);
  }

  /** Drops a listener. */
  removeEventListener(_type: 'controllerchange', listener: () => void): void {
    this.#listeners.delete(listener);
  }

  /** Changes the controlling worker, as a worker taking control does. */
  takeControl(): void {
    this.controller = {
      messages: [],
      postMessage(message: unknown) {
        this.messages.push(message);
      },
    };
    this.#listeners.forEach((listener) => listener());
  }
}

/** The pieces a spec drives. */
interface TmUpdatesRig {
  readonly updates: () => TmUpdates;
  readonly sw: TmScriptedSwUpdate;
  readonly container: TmRecordingContainer;
  readonly page: TmFakeApiHost;
  readonly telemetry: TmFakeTelemetry;
  readonly reloads: () => number;
}

/** Starts a test app with the update service over a scripted worker. */
async function setUp(options: { worker?: boolean; controlled?: boolean; trustedTypes?: TmTrustedTypes } = {}): Promise<TmUpdatesRig> {
  const sw = new TmScriptedSwUpdate();
  const container = new TmRecordingContainer();
  if (options.controlled === false) {
    container.controller = null;
  }
  const page = new TmFakeApiHost();
  let reloads = 0;
  await tmAppRig({
    providers: [
      { provide: ɵTM_API_HOST, useValue: page },
      // Read through the global, so a spec's fake clock moves it.
      { provide: TM_NOW, useValue: () => Date.now() },
      {
        provide: TM_UPDATES_HOST,
        useValue: {
          container,
          updates: options.worker === false ? null : sw,
          origin: 'https://acme.example',
          trustedTypes: options.trustedTypes,
          reload: () => reloads++,
        },
      },
    ],
  });
  return {
    updates: () => TestBed.inject(TmUpdates),
    sw,
    container,
    page,
    telemetry: TestBed.inject(TmFakeTelemetry),
    reloads: () => reloads,
  };
}

/** The names and properties of the worker events recorded. */
function events(telemetry: TmFakeTelemetry): { name: string; props: object }[] {
  return telemetry
    .recent()
    .filter((item) => item.name.startsWith('tm.sw.'))
    .map((item) => ({ name: item.name, props: item.props }));
}

describe('TmUpdates', () => {
  describe('check', () => {
    it('finds a newer version the worker downloads during the check', async () => {
      const rig = await setUp();
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'VERSION_DETECTED', version: version('1.1.0') });
        rig.sw.versionUpdates.next({ type: 'VERSION_READY', currentVersion: version(OURS), latestVersion: version('1.1.0') });
        return Promise.resolve(true);
      };
      expect(await rig.updates().check()).toBe('ready');
      expect(rig.updates().available()).toBe('1.1.0');
    });

    it('tells no newer version from a failed download and an unreachable server', async () => {
      const rig = await setUp();
      const updates = rig.updates();
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'NO_NEW_VERSION_DETECTED', version: version(OURS) });
        return Promise.resolve(false);
      };
      expect(await updates.check()).toBe('none');
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'VERSION_INSTALLATION_FAILED', version: version('1.1.0'), error: 'Hash mismatch.' });
        return Promise.resolve(false);
      };
      expect(await updates.check()).toBe('failed');
      // `false` with no event: the worker could not fetch the manifest.
      rig.sw.script = () => Promise.resolve(false);
      expect(await updates.check()).toBe('unreachable');
      rig.sw.script = () => Promise.reject(new Error('Service workers are disabled or not supported by this browser'));
      expect(await updates.check()).toBe('unreachable');
      expect(updates.available()).toBeNull();
    });

    it("takes the worker's own newer version as ready, though its download was announced before the tab listened", async () => {
      const rig = await setUp();
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'NO_NEW_VERSION_DETECTED', version: version('1.1.0') });
        return Promise.resolve(false);
      };
      expect(await rig.updates().check()).toBe('ready');
      expect(rig.updates().available()).toBe('1.1.0');
    });

    it("never takes a version without the build's version name for a newer one", async () => {
      const rig = await setUp();
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'NO_NEW_VERSION_DETECTED', version: version(null, 'f00d') });
        return Promise.resolve(false);
      };
      expect(await rig.updates().check()).toBe('none');
      expect(rig.updates().available()).toBeNull();
    });

    it('answers ready without asking once a newer version waits, and joins a check in progress', async () => {
      const rig = await setUp();
      let answer!: (found: boolean) => void;
      rig.sw.script = () => new Promise((resolve) => (answer = resolve));
      const updates = rig.updates();
      const first = updates.check();
      const second = updates.check();
      expect(rig.sw.checks).toBe(1);
      expect(updates.checking()).toBe(true);
      rig.sw.versionUpdates.next({ type: 'VERSION_READY', currentVersion: version(OURS), latestVersion: version('1.1.0') });
      answer(true);
      expect(await first).toBe('ready');
      expect(await second).toBe('ready');
      expect(updates.checking()).toBe(false);
      expect(await updates.check()).toBe('ready');
      expect(rig.sw.checks).toBe(1);
    });

    it('is disabled without a worker client, and while no worker controls the page', async () => {
      const without = await setUp({ worker: false });
      expect(await without.updates().check()).toBe('disabled');
      expect(without.updates().enabled).toBe(false);
      expect(without.updates().version()).toBeNull();
      expect(without.container.registered).toEqual([]);
      TestBed.resetTestingModule();

      const uncontrolled = await setUp({ controlled: false });
      const updates = uncontrolled.updates();
      expect(await updates.check()).toBe('disabled');
      expect(uncontrolled.sw.checks).toBe(0);
      // A worker that takes control starts initializing, and the checks run.
      uncontrolled.container.takeControl();
      expect(uncontrolled.container.controller?.messages).toEqual([{ action: 'INITIALIZE' }]);
      expect(updates.enabled).toBe(true);
      expect(updates.version()).toBe(OURS);
      await updates.check();
      expect(uncontrolled.sw.checks).toBe(1);
    });
  });

  describe('update', () => {
    it('reloads into a newer version, and as the browser would with no worker in the way', async () => {
      const rig = await setUp();
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'VERSION_READY', currentVersion: version(OURS), latestVersion: version('1.1.0') });
        return Promise.resolve(true);
      };
      expect(await rig.updates().update()).toBe('ready');
      expect(rig.reloads()).toBe(1);
      TestBed.resetTestingModule();

      const without = await setUp({ worker: false });
      expect(await without.updates().update()).toBe('disabled');
      expect(without.reloads()).toBe(1);
    });

    it('keeps the page when the download fails or the server is out of reach', async () => {
      const rig = await setUp();
      expect(await rig.updates().update()).toBe('unreachable');
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'NO_NEW_VERSION_DETECTED', version: version(OURS) });
        return Promise.resolve(false);
      };
      expect(await rig.updates().update()).toBe('none');
      expect(rig.reloads()).toBe(0);
    });
  });

  describe("the worker's events", () => {
    it('reports a newer version once, and shows the update banner, dismissible for that version, with Reload', async () => {
      const rig = await setUp();
      const updates = rig.updates();
      const banners = TestBed.inject(TmBanners);
      expect(banners.ɵhead()).toBeNull();
      rig.sw.versionUpdates.next({ type: 'VERSION_READY', currentVersion: version(OURS), latestVersion: version('1.1.0') });
      rig.sw.versionUpdates.next({ type: 'VERSION_READY', currentVersion: version(OURS), latestVersion: version('1.1.0') });
      expect(updates.available()).toBe('1.1.0');
      expect(events(rig.telemetry)).toEqual([{ name: 'tm.sw.update-ready', props: { fromVersion: OURS } }]);
      TestBed.tick();
      const banner = banners.ɵhead()?.options;
      expect(banner).toMatchObject({ id: 'tm.update', level: 3, tone: 'info', icon: 'refresh-cw', dismissible: true });
      expect(banner?.message()).toBe('A new version is available.');
      expect(banner?.action?.label()).toBe('Reload');
      banner?.action?.run();
      expect(rig.reloads()).toBe(1);
      // A dismissed banner stays dismissed for its version, and returns for the next.
      banners.ɵdismiss('tm.update');
      expect(banners.ɵhead()).toBeNull();
      rig.sw.versionUpdates.next({ type: 'VERSION_READY', currentVersion: version(OURS), latestVersion: version('1.2.0') });
      TestBed.tick();
      expect(banners.ɵhead()?.options.id).toBe('tm.update');
    });

    it('reports a failed download, and reloads at once when the cache is broken', async () => {
      const rig = await setUp();
      rig.updates();
      const flushes = rig.telemetry.flushes();
      rig.sw.versionUpdates.next({ type: 'VERSION_INSTALLATION_FAILED', version: version('1.1.0'), error: 'Hash mismatch.' });
      rig.sw.unrecoverable.next({ type: 'UNRECOVERABLE_STATE', reason: 'Missing asset.' });
      expect(events(rig.telemetry).map((event) => event.name)).toEqual(['tm.sw.install-failed', 'tm.sw.unrecoverable']);
      expect(rig.telemetry.flushes()).toBe(flushes + 1);
      expect(rig.reloads()).toBe(1);
      expect(rig.updates().available()).toBeNull();
    });
  });

  describe('registration', () => {
    it('registers the worker at the root once the app settles, bypassing the HTTP cache', async () => {
      const rig = await setUp();
      rig.updates();
      await TestBed.inject(ApplicationRef).whenStable();
      await vi.waitFor(() => expect(rig.container.registered).toHaveLength(1));
      expect(rig.container.registered[0]).toEqual({ url: '/ngsw-worker.js', options: { scope: '/', updateViaCache: 'none' } });
    });

    it('checks once the worker is registered, so a version announced before the service started shows', async () => {
      const rig = await setUp();
      // The worker downloaded 1.1.0 before this tab listened: its answer names it.
      rig.sw.script = () => {
        rig.sw.versionUpdates.next({ type: 'NO_NEW_VERSION_DETECTED', version: version('1.1.0') });
        return Promise.resolve(false);
      };
      const updates = rig.updates();
      await vi.waitFor(() => expect(updates.available()).toBe('1.1.0'));
      expect(rig.sw.checks).toBe(1);
      expect(rig.container.registered).toHaveLength(1);
    });

    it('registers after 30 s when the app never settles, and reports a refused registration', async () => {
      const rig = await setUp();
      vi.useFakeTimers();
      try {
        rig.container.refuse = true;
        vi.spyOn(TestBed.inject(ApplicationRef), 'whenStable').mockReturnValue(new Promise<void>(() => undefined));
        rig.updates();
        await vi.advanceTimersByTimeAsync(TM_REGISTER_WAIT_MS - 1);
        expect(rig.container.registered).toEqual([]);
        await vi.advanceTimersByTimeAsync(1);
        expect(rig.container.registered).toHaveLength(1);
        expect(events(rig.telemetry).map((event) => event.name)).toEqual(['tm.sw.register-failed']);
      } finally {
        vi.useRealTimers();
      }
    });

    it("registers through the 'tellma' policy, which admits only the same-origin worker script, once per document", async () => {
      const created: { name: string; rules: TmScriptUrlRules }[] = [];
      const trustedTypes: TmTrustedTypes = {
        createPolicy: (name, rules) => {
          created.push({ name, rules });
          return { createScriptURL: (input) => ({ trusted: rules.createScriptURL(input) }) };
        },
      };
      const rig = await setUp({ trustedTypes });
      rig.updates();
      await vi.waitFor(() => expect(rig.container.registered).toHaveLength(1));
      expect(rig.container.registered[0]?.url).toEqual({ trusted: 'https://acme.example/ngsw-worker.js' });
      expect(created.map((policy) => policy.name)).toEqual(['tellma']);
      const rules = created[0]!.rules;
      for (const refused of [
        'https://evil.example/ngsw-worker.js',
        '/other-worker.js',
        '/ngsw-worker.js?v=2',
        '/ngsw-worker.js#x',
        '//evil.example/ngsw-worker.js',
      ]) {
        expect(() => rules.createScriptURL(refused)).toThrow(TypeError);
      }
      // The policy's name is created once per document: a second URL reuses it.
      tmWorkerScriptUrl('https://acme.example', trustedTypes);
      expect(created).toHaveLength(1);
    });
  });

  describe('scheduled checks', () => {
    it('checks when the tab comes back into view after an hour hidden, and every 6 hours while visible', async () => {
      const rig = await setUp();
      vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
      try {
        rig.updates();
        // The check once the worker is registered.
        await vi.advanceTimersByTimeAsync(0);
        expect(rig.sw.checks).toBe(1);
        await vi.advanceTimersByTimeAsync(TM_UPDATE_INTERVAL_MS - 1);
        expect(rig.sw.checks).toBe(1);
        await vi.advanceTimersByTimeAsync(1);
        expect(rig.sw.checks).toBe(2);

        // Hidden less than an hour: no check on return, and the hidden time stops the 6-hour timer.
        rig.page.visibleNow = false;
        rig.page.fire('document', 'visibilitychange');
        await vi.advanceTimersByTimeAsync(TM_UPDATE_HIDDEN_MS - 1);
        rig.page.visibleNow = true;
        rig.page.fire('document', 'visibilitychange');
        expect(rig.sw.checks).toBe(2);

        // Hidden an hour: a check on return.
        rig.page.visibleNow = false;
        rig.page.fire('document', 'visibilitychange');
        await vi.advanceTimersByTimeAsync(TM_UPDATE_HIDDEN_MS);
        rig.page.visibleNow = true;
        rig.page.fire('document', 'visibilitychange');
        await vi.advanceTimersByTimeAsync(0);
        expect(rig.sw.checks).toBe(3);
      } finally {
        vi.useRealTimers();
      }
    });
  });
});
