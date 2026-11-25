// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Dialog } from '@angular/cdk/dialog';
import { Component, Injectable, Injector, isDevMode, signal, type Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';
import { TmToasts } from '@tellma/core-ui/toast';
import { tmTestEntitiesChunk, TmFakeLocks } from '@tellma/core-testing';

import { TmApiError } from '../api/tm-api-error';
import { TM_API_HOST, type TmApiHost } from '../api/tm-api-host';
import { TM_ERRORS_LOAD, type TmErrorsModule } from '../api/tm-api-seams';
import { TmInputIdle } from '../attention/tm-input-idle';
import { TM_METADATA_LOADERS } from '../metadata/tm-metadata-loaders';
import {
  TM_T17_DOCS,
  TM_TEST_SUB,
  tmAppRig,
  tmNavigate,
  tmRouteTestConfig,
  tmSettle,
  tmSettleUntil,
  type TmAppRig,
} from '../routes/tm-app.testing.util';
import type { TmMeLocaleResult, TmTenantFacts } from '../state/launch/tm-launch-docs';
import { TM_ZONE_CHECK_LOAD, TmDeviceZone, type TmZoneDoc } from './tm-device-zone';
import { TmLocalization } from './tm-localization';
import { TmZoneCheck } from './tm-zone-check';

// The device-zone check over the test app: tenant 17 shows times in
// Asia/Riyadh (+3), the browser's zone is scripted, and so are the page's
// visibility, the input-idle moment and the server's answers. Each step of
// the check, the automatic update with its failures, and the prompt, which
// one tab of the user's asks once.

/** The browser's own zone, which the scripted device zone stands in for. */
const BROWSER_ZONE = new Intl.DateTimeFormat().resolvedOptions().timeZone;

/** The zones the core.TimeZone value set holds, and the alias a browser may report. */
const TIME_ZONE = {
  name: 'core.TimeZone',
  values: ['Asia/Riyadh', 'Asia/Kuwait', 'Asia/Kolkata', 'Europe/London'].map((value) => ({ value, label: value })),
  aliases: { 'Asia/Calcutta': 'Asia/Kolkata' },
};

/** The device partition. */
const DEVICE = { kind: 'device' } as const;

/** The device zone the browser reports now. */
let deviceZone = 'Europe/London';

/** Whether the page is visible now. */
let visible = true;

/** The input-idle moment the prompt waits for; settled unless a test holds it. */
let idleGate: Promise<void> = Promise.resolve();

/** The browser behind the app: the real page's events, with the visibility scripted. */
function host(): TmApiHost {
  return {
    fetch: () => Promise.reject(new Error('This test sends nothing over the network.')),
    xhr: () => new XMLHttpRequest(),
    onLine: () => true,
    visible: () => visible,
    listen: (target, type, listener, options) => {
      const on = target === 'window' ? window : document;
      on.addEventListener(type, listener, options);
      return () => on.removeEventListener(type, listener, options);
    },
    random: (array) => crypto.getRandomValues(array),
    devMode: isDevMode,
  };
}

/** The write's answer once the user's zone is `zone`. */
function localeResult(zone: string): TmMeLocaleResult {
  return { preferredTimeZone: zone, modifiedAt: '2026-11-24T10:00:00.0000000+00:00', preferencesTag: 'P2' };
}

/** A failed write of a zone outside the server's set. */
function zoneUnknown(): TmApiError {
  return new TmApiError({
    kind: 'validation',
    status: 422,
    code: 'validation',
    problem: { code: 'validation', instance: 'users/me/locale', status: 422, errors: [{ path: 'timeZone', code: 'Users.TimeZoneUnknown', message: 'x' }] },
  });
}

/** What a spec sets up. */
interface TmZoneRigOptions {
  /** The device zone tenant 17 last handled; `null` for none, Asia/Riyadh when omitted. */
  readonly seen?: string | null;
  /** Whether the option is on. */
  readonly auto?: boolean;
  /** The browser's zone; Europe/London when omitted. */
  readonly device?: string;
  /** Tenant 17's state. */
  readonly state?: TmTenantFacts['state'];
  /** How the server answers the write; with the user's new zone when omitted. */
  readonly respond?: (call: { readonly body: { readonly timeZone: string } }) => unknown;
}

/** The rig, and the app's device-zone service. */
interface TmZoneRig extends TmAppRig {
  /** The device-zone service. */
  readonly zone: TmDeviceZone;
}

/** Starts the app in tenant 17, with the device's zone document and the write's answer scripted. */
async function setUp(options: TmZoneRigOptions = {}): Promise<TmZoneRig> {
  deviceZone = options.device ?? 'Europe/London';
  const loaders = tmRouteTestConfig().metadata;
  const rig = await tmAppRig({
    docs: options.state === undefined ? undefined : { ...TM_T17_DOCS, tenant: { ...TM_T17_DOCS.tenant, state: options.state } },
    providers: [
      { provide: TM_ZONE_CHECK_LOAD, useValue: () => import('./tm-zone-check') },
      { provide: TM_METADATA_LOADERS, useValue: { ...loaders, entities: () => Promise.resolve(tmTestEntitiesChunk({ enums: [TIME_ZONE] })) } },
      { provide: TM_API_HOST, useFactory: host },
      { provide: TmInputIdle, useValue: { whenIdle: () => idleGate } },
    ],
  });
  if (options.seen !== null) {
    await rig.store.put<TmZoneDoc>(DEVICE, 'zone', {
      tag: null,
      savedAt: Date.now(),
      data: { users: { [TM_TEST_SUB]: { auto: options.auto === true, seen: { 17: options.seen ?? 'Asia/Riyadh' }, at: 1 } } },
    });
  }
  rig.api.respond(
    'users/me/locale',
    options.respond ?? ((call: { readonly body: { readonly timeZone: string } }) => localeResult(call.body.timeZone)),
  );
  await tmNavigate('/17');
  return { ...rig, zone: TestBed.inject(TmDeviceZone) };
}

/** The device zone tenant 17 handled last, as the device's document holds it. */
function seen(rig: TmAppRig): string | undefined {
  return rig.store.doc<TmZoneDoc>(DEVICE, 'zone')()?.data.users[TM_TEST_SUB]?.seen['17'];
}

/** Replaces the device zone tenant 17 last handled, as another tab's answer or an earlier session leaves it. */
async function setSeen(rig: TmAppRig, zone: string): Promise<void> {
  await rig.store.patch<TmZoneDoc>(DEVICE, 'zone', (doc) => ({
    users: { [TM_TEST_SUB]: { ...doc!.users[TM_TEST_SUB]!, seen: { 17: zone } } },
  }));
}

/** The errors reported to telemetry with the API's source, as the error views report them. */
function apiErrors(rig: TmAppRig): readonly Readonly<Record<string, unknown>>[] {
  return rig.telemetry
    .recent()
    .filter((item) => item.name === 'tm.error.captured' && item.props['source'] === 'api')
    .map((item) => item.props);
}

/** The zone telemetry, by event, without the event names' common prefix. */
function zoneEvents(rig: TmAppRig): readonly (readonly [string, Readonly<Record<string, unknown>>])[] {
  return rig.telemetry
    .recent()
    .filter((item) => item.name.startsWith('tm.l10n.zone-'))
    .map((item) => [item.name.slice('tm.l10n.'.length), item.props] as const);
}

/** The writes of the user's zone. */
function writes(rig: TmAppRig): readonly unknown[] {
  return rig.api
    .calls()
    .filter((call) => call.route === 'users/me/locale')
    .map((call) => call.body);
}

/** The open prompt, or `null`. */
function prompt(): HTMLElement | null {
  return document.querySelector<HTMLElement>('tm-zone-prompt');
}

/** A button of the open prompt, by its text. */
function button(text: string): HTMLButtonElement {
  const found = [...document.querySelectorAll<HTMLButtonElement>('tm-modal-shell button')].find((candidate) =>
    candidate.textContent?.includes(text),
  );
  if (found === undefined) {
    throw new Error(`The prompt has no "${text}" button.`);
  }
  return found;
}

/** A page's own dialog, open under the prompt's wait. */
@Component({ selector: 'tm-spec-dialog', template: '<p>A page dialog</p>' })
class TmSpecDialog {}

/** The error views' presenter, as far as the prompt reads it. */
@Injectable({ providedIn: 'root' })
class TmSpecPresenter {
  /** The view of a failed save, with the server's errors. */
  describe(): { readonly title: Signal<string>; readonly message: Signal<string>; readonly items: Signal<readonly string[]> } {
    return {
      title: signal("Couldn't save"),
      message: signal('The server is busy. Try again in a moment.'),
      items: signal(['Time zones are locked for this user.']),
    };
  }
}

describe('TmZoneCheck', () => {
  beforeEach(() => {
    visible = true;
    idleGate = Promise.resolve();
    // The browser reports the scripted zone wherever it would report its own.
    const real = Intl.DateTimeFormat.prototype.resolvedOptions;
    vi.spyOn(Intl.DateTimeFormat.prototype, 'resolvedOptions').mockImplementation(function (this: Intl.DateTimeFormat) {
      const options = real.call(this);
      return options.timeZone === BROWSER_ZONE ? { ...options, timeZone: deviceZone } : options;
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    // A prompt a test left open closes with its app.
    TestBed.resetTestingModule();
  });

  describe('the steps', () => {
    it("records a first run's zone at idle after the first render, and asks nothing", async () => {
      const rig = await setUp({ seen: null });
      await tmSettleUntil(() => seen(rig) === 'Europe/London');
      expect(zoneEvents(rig)).toEqual([['zone-change', { action: 'first-run' }]]);
      expect(prompt()).toBeNull();
      expect(writes(rig)).toEqual([]);
    });

    it('stops when the device zone is the one the tenant last handled', async () => {
      const rig = await setUp({ seen: 'Europe/London' });
      await rig.zone.check();
      expect(zoneEvents(rig)).toEqual([]);
      expect(prompt()).toBeNull();
    });

    it('records a device zone that is the display zone already', async () => {
      const rig = await setUp({ device: 'Asia/Riyadh', seen: 'Europe/London' });
      await rig.zone.check();
      expect(seen(rig)).toBe('Asia/Riyadh');
      expect(zoneEvents(rig)).toEqual([['zone-change', { action: 'matches' }]]);
    });

    it('records the change in a frozen tenant, which would refuse the write', async () => {
      const rig = await setUp({ state: 'Frozen', auto: true });
      await rig.zone.check();
      expect(seen(rig)).toBe('Europe/London');
      expect(zoneEvents(rig)).toEqual([['zone-change', { action: 'frozen' }]]);
      expect(writes(rig)).toEqual([]);
    });

    it('asks in a read-only tenant, which accepts the write', async () => {
      const rig = await setUp({ state: 'ReadOnly' });
      void rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
      expect(zoneEvents(rig)).toEqual([['zone-change', { action: 'prompt' }]]);
    });

    it('records a zone that shows the same clock as the display zone now', async () => {
      const rig = await setUp({ device: 'Asia/Kuwait' });
      await rig.zone.check();
      expect(seen(rig)).toBe('Asia/Kuwait');
      expect(zoneEvents(rig)).toEqual([['zone-change', { action: 'same-offset' }]]);
      expect(prompt()).toBeNull();
    });

    it('does nothing in a hidden tab', async () => {
      const rig = await setUp({ seen: null });
      visible = false;
      await rig.zone.check();
      expect(seen(rig)).toBeUndefined();
      expect(zoneEvents(rig)).toEqual([]);
    });

    it('checks again when the tab comes into view, after the device zone is re-read', async () => {
      const rig = await setUp({ seen: null, device: 'Asia/Riyadh' });
      await tmSettleUntil(() => seen(rig) === 'Asia/Riyadh');
      await rig.zone.setAuto(true, 'settings');
      deviceZone = 'Europe/London';
      document.dispatchEvent(new Event('visibilitychange'));
      await tmSettleUntil(() => seen(rig) === 'Europe/London');
      expect(writes(rig)).toEqual([{ timeZone: 'Europe/London' }]);
    });
  });

  describe('the triggers', () => {
    it('checks on each entry into a tenant, and not on a navigation within it', async () => {
      const rig = await setUp({ seen: 'Europe/London', auto: true });
      await tmSettle();
      // The startup's check found nothing to do; the change below waits for a trigger.
      await setSeen(rig, 'Asia/Riyadh');
      await rig.router.navigateByUrl('/17/credit-review');
      await tmSettle();
      expect(writes(rig)).toEqual([]);
      await rig.router.navigateByUrl('/tenants');
      await tmSettle();
      expect(writes(rig)).toEqual([]);
      await rig.router.navigateByUrl('/17');
      await tmSettleUntil(() => writes(rig).length === 1);
      expect(seen(rig)).toBe('Europe/London');
      expect(zoneEvents(rig).filter(([name]) => name === 'zone-change')).toEqual([['zone-change', { action: 'auto' }]]);
    });

    it('runs one check for a burst of triggers, so one change is handled once', async () => {
      const rig = await setUp({ seen: 'Europe/London', auto: true });
      await tmSettle();
      await setSeen(rig, 'Asia/Riyadh');
      // A tenant entry, the tab coming into view and a caller's check, in one task.
      const entry = rig.router.navigateByUrl('/tenants').then(() => rig.router.navigateByUrl('/17'));
      document.dispatchEvent(new Event('visibilitychange'));
      const checked = rig.zone.check();
      await Promise.all([entry, checked]);
      await tmSettle();
      expect(writes(rig)).toEqual([{ timeZone: 'Europe/London' }]);
      expect(zoneEvents(rig).filter(([name]) => name === 'zone-change')).toEqual([['zone-change', { action: 'auto' }]]);
    });
  });

  describe('the automatic update', () => {
    it('writes the zone in the background, adopts it, records it and says so in a toast with Change', async () => {
      const rig = await setUp({ auto: true });
      const toast = vi.spyOn(TestBed.inject(TmToasts), 'show');
      await rig.zone.check();
      expect(writes(rig)).toEqual([{ timeZone: 'Europe/London' }]);
      expect(rig.api.calls().find((call) => call.route === 'users/me/locale')?.options).toEqual({ tenantId: 17 });
      expect(TestBed.inject(TmLocalization).timeZone()).toBe('Europe/London');
      expect(seen(rig)).toBe('Europe/London');
      expect(zoneEvents(rig)).toEqual([
        ['zone-change', { action: 'auto' }],
        ['zone-auto', { outcome: 'ok' }],
      ]);
      expect(toast).toHaveBeenCalledOnce();
      const shown = toast.mock.calls[0]![0];
      expect(shown).toMatchObject({ tone: 'neutral', duration: 4000, action: { label: 'Change' } });
      expect(shown.title).toMatch(/^Times now show in .+ \(GMT.*\)\.$/);
      shown.action!.run();
      await tmSettleUntil(() => rig.router.url === '/17/me/region');
    });

    it('maps the reported zone through the aliases before it compares, records and writes', async () => {
      const rig = await setUp({ auto: true, device: 'Asia/Calcutta' });
      await rig.zone.check();
      expect(writes(rig)).toEqual([{ timeZone: 'Asia/Kolkata' }]);
      expect(seen(rig)).toBe('Asia/Kolkata');
    });

    it('tries an offline write once more when the server is reachable again, and only once', async () => {
      const rig = await setUp({
        auto: true,
        respond: () => {
          throw new TmApiError({ kind: 'offline' });
        },
      });
      rig.api.setOnline(false);
      // The startup's check, at idle after the first render, finds the server unreachable.
      await tmSettleUntil(() => writes(rig).length === 1);
      // A later check while still offline waits for the same retry.
      await rig.zone.check();
      expect(writes(rig)).toHaveLength(2);
      expect(seen(rig)).toBe('Asia/Riyadh');
      rig.api.setOnline(true);
      await tmSettleUntil(() => writes(rig).length === 3);
      rig.api.setOnline(false);
      await tmSettle();
      rig.api.setOnline(true);
      await tmSettle();
      expect(writes(rig)).toHaveLength(3);
      expect(seen(rig)).toBe('Asia/Riyadh');
      expect(zoneEvents(rig).filter(([name]) => name === 'zone-auto')).toEqual([
        ['zone-auto', { outcome: 'offline' }],
        ['zone-auto', { outcome: 'offline' }],
        ['zone-auto', { outcome: 'offline' }],
      ]);
    });

    it("records a zone outside the server's set, so it is never tried again until the device moves", async () => {
      const rig = await setUp({ auto: true });
      rig.api.respond('users/me/locale', () => {
        throw zoneUnknown();
      });
      await rig.zone.check();
      expect(seen(rig)).toBe('Europe/London');
      expect(zoneEvents(rig).at(-1)).toEqual(['zone-auto', { outcome: 'rejected' }]);
      await rig.zone.check();
      expect(writes(rig)).toHaveLength(1);
    });

    it('records nothing after any other failure, so the next check tries again', async () => {
      const rig = await setUp({ auto: true });
      rig.api.respond('users/me/locale', () => {
        throw new TmApiError({ kind: 'server', status: 500, code: 'server' });
      });
      await rig.zone.check();
      expect(seen(rig)).toBe('Asia/Riyadh');
      expect(zoneEvents(rig).at(-1)).toEqual(['zone-auto', { outcome: 'rejected' }]);
      // A server failure is reported, as the error views would report it.
      expect(apiErrors(rig)).toEqual([{ source: 'api', errorClass: 'TmApiError' }]);
      await rig.zone.check();
      expect(writes(rig)).toHaveLength(2);
    });

    it('reports a write whose outcome is unknown, and tries it again once the server is reachable', async () => {
      const rig = await setUp({ seen: 'Europe/London', auto: true });
      await tmSettle();
      await setSeen(rig, 'Asia/Riyadh');
      rig.api.respond('users/me/locale', () => {
        throw new TmApiError({ kind: 'offline', code: 'outcome-unknown' });
      });
      await rig.zone.check();
      expect(zoneEvents(rig).at(-1)).toEqual(['zone-auto', { outcome: 'offline' }]);
      expect(apiErrors(rig)).toHaveLength(1);
    });

    it('reports nothing for a refusal the error views would not report', async () => {
      const rig = await setUp({ auto: true });
      rig.api.respond('users/me/locale', () => {
        throw new TmApiError({ kind: 'forbidden', status: 403, code: 'forbidden' });
      });
      await rig.zone.check();
      expect(zoneEvents(rig).at(-1)).toEqual(['zone-auto', { outcome: 'rejected' }]);
      expect(apiErrors(rig)).toEqual([]);
    });
  });

  describe('the prompt', () => {
    it('changes the zone, records it, and turns the option on when the box is ticked', async () => {
      const rig = await setUp();
      const done = rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
      expect(prompt()!.textContent).toContain('Your device is now on');
      prompt()!.querySelector<HTMLInputElement>('tm-checkbox input')!.click();
      button('Change time zone').click();
      await done;
      expect(prompt()).toBeNull();
      expect(writes(rig)).toEqual([{ timeZone: 'Europe/London' }]);
      expect(TestBed.inject(TmLocalization).timeZone()).toBe('Europe/London');
      expect(seen(rig)).toBe('Europe/London');
      expect(rig.zone.auto()).toBe(true);
      expect(zoneEvents(rig)).toEqual([
        ['zone-change', { action: 'prompt' }],
        ['zone-option', { on: true, source: 'prompt' }],
        ['zone-prompt', { answer: 'change', auto: true }],
      ]);
    });

    it('keeps the current zone, never turning the option on, even with the box ticked', async () => {
      const rig = await setUp();
      const done = rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
      prompt()!.querySelector<HTMLInputElement>('tm-checkbox input')!.click();
      button('Keep current').click();
      await done;
      expect(writes(rig)).toEqual([]);
      expect(seen(rig)).toBe('Europe/London');
      expect(rig.zone.auto()).toBe(false);
      expect(zoneEvents(rig).at(-1)).toEqual(['zone-prompt', { answer: 'keep', auto: true }]);
      // Asked once: the next check finds the change handled.
      await rig.zone.check();
      expect(prompt()).toBeNull();
    });

    it('records a dismissal by Escape as handled', async () => {
      const rig = await setUp();
      const done = rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
      prompt()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      await done;
      expect(seen(rig)).toBe('Europe/London');
      expect(zoneEvents(rig).at(-1)).toEqual(['zone-prompt', { answer: 'dismiss', auto: false }]);
    });

    it("shows a zone outside the server's set inline, and stays open", async () => {
      const rig = await setUp();
      rig.api.respond('users/me/locale', () => {
        throw zoneUnknown();
      });
      void rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
      button('Change time zone').click();
      await tmSettleUntil(() => prompt()!.querySelector('tm-alert') !== null);
      expect(prompt()!.querySelector('tm-alert')!.textContent).toContain("Acme doesn't support your device's time zone. Choose one in My settings.");
      expect(seen(rig)).toBe('Asia/Riyadh');
    });

    it("shows any other failure as the error views describe a failed save, and stays open", async () => {
      TestBed.configureTestingModule({
        providers: [
          {
            provide: TM_ERRORS_LOAD,
            useValue: () => Promise.resolve({ TmErrorPresenter: TmSpecPresenter } as unknown as TmErrorsModule),
          },
        ],
      });
      const rig = await setUp();
      rig.api.respond('users/me/locale', () => {
        throw new TmApiError({ kind: 'server', status: 500, code: 'server' });
      });
      void rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
      button('Change time zone').click();
      await tmSettleUntil(() => prompt()!.querySelector('tm-alert') !== null);
      const alert = prompt()!.querySelector('tm-alert')!;
      expect(alert.textContent).toContain("Couldn't save");
      expect(alert.textContent).toContain('The server is busy. Try again in a moment.');
      expect([...alert.querySelectorAll('li')].map((item) => item.textContent?.trim())).toEqual(['Time zones are locked for this user.']);
      expect(apiErrors(rig)).toEqual([{ source: 'api', errorClass: 'TmApiError' }]);
      expect(seen(rig)).toBe('Asia/Riyadh');
    });

    it("waits for the user's hands to come off the keyboard and pointer, and for the page's dialog to close", async () => {
      let idle!: () => void;
      idleGate = new Promise((resolve) => (idle = resolve));
      const rig = await setUp();
      const page: TmModalRef = TestBed.inject(TmModal).open(TmSpecDialog, { title: 'A page dialog' });
      void rig.zone.check();
      await tmSettle();
      expect(prompt()).toBeNull();
      idle();
      await tmSettle();
      expect(prompt()).toBeNull();
      page.close();
      await tmSettleUntil(() => prompt() !== null);
    });

    it('asks nothing when the change is handled while it waits, as in another tab', async () => {
      let idle!: () => void;
      idleGate = new Promise((resolve) => (idle = resolve));
      const rig = await setUp();
      const done = rig.zone.check();
      await tmSettle();
      // Another tab answered meanwhile.
      await rig.store.patch<TmZoneDoc>(DEVICE, 'zone', (doc) => ({
        users: { [TM_TEST_SUB]: { ...doc!.users[TM_TEST_SUB]!, seen: { 17: 'Europe/London' } } },
      }));
      idle();
      await done;
      expect(prompt()).toBeNull();
      expect(zoneEvents(rig)).toEqual([]);
    });

    it("asks nothing while another tab holds the user's prompt lock, and asks once that tab gives up without asking", async () => {
      const rig = await setUp({ seen: 'Europe/London' });
      await tmSettle();
      let release!: () => void;
      const held = TestBed.inject(TmFakeLocks).run(`tm:acme:${TM_TEST_SUB}:zone-prompt`, () => new Promise<void>((resolve) => (release = resolve)));
      await setSeen(rig, 'Asia/Riyadh');
      await rig.zone.check();
      await tmSettle();
      expect(prompt()).toBeNull();
      // The other tab turned hidden before the user was idle, and let the lock go unanswered.
      release();
      await held;
      await tmSettleUntil(() => prompt() !== null);
      expect(zoneEvents(rig).filter(([name]) => name === 'zone-change')).toEqual([['zone-change', { action: 'prompt' }]]);
    });

    it('asks nothing once the tab that held the lock answered, and waits for the lock once however often it checks', async () => {
      const rig = await setUp({ seen: 'Europe/London' });
      await tmSettle();
      const locks = TestBed.inject(TmFakeLocks);
      let release!: () => void;
      const held = locks.run(`tm:acme:${TM_TEST_SUB}:zone-prompt`, () => new Promise<void>((resolve) => (release = resolve)));
      await setSeen(rig, 'Asia/Riyadh');
      const run = vi.spyOn(locks, 'run');
      await rig.zone.check();
      await rig.zone.check();
      expect(run).toHaveBeenCalledOnce();
      // The other tab answered, and recorded the change, before letting the lock go.
      await setSeen(rig, 'Europe/London');
      release();
      await held;
      await tmSettleUntil(() => locks.held().length === 0);
      await tmSettle();
      expect(prompt()).toBeNull();
      expect(zoneEvents(rig)).toEqual([]);
    });

    it('opens one dialog for two tabs of the user, and the other asks nothing once it is answered', async () => {
      const rig = await setUp();
      // A second tab's check, over the same device document and locks.
      const other = Injector.create({ providers: [TmZoneCheck], parent: TestBed.inject(Injector) }).get(TmZoneCheck);
      const first = rig.zone.check();
      const second = other.check();
      await tmSettleUntil(() => prompt() !== null);
      await tmSettle();
      expect(document.querySelectorAll('tm-zone-prompt')).toHaveLength(1);
      button('Keep current').click();
      await Promise.all([first, second]);
      await other.check();
      expect(prompt()).toBeNull();
      expect(zoneEvents(rig).filter(([name]) => name === 'zone-prompt')).toHaveLength(1);
    });

    it('records nothing when the dialog closes without an answer, as history navigation closes it, so the next check asks again', async () => {
      const rig = await setUp();
      const done = rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
      // What closing on navigation does: the dialog closes under the modal's own paths.
      TestBed.inject(Dialog).closeAll();
      await done;
      expect(seen(rig)).toBe('Asia/Riyadh');
      expect(zoneEvents(rig).some(([name]) => name === 'zone-prompt')).toBe(false);
      void rig.zone.check();
      await tmSettleUntil(() => prompt() !== null);
    });
  });
});
