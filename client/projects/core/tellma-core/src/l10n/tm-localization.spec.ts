// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { provideLocationMocks } from '@angular/common/testing';
import { ApplicationInitStatus, Component, effect, inject, type EnvironmentProviders, type Provider } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import {
  provideTellmaStateTesting,
  provideTellmaTesting,
  TmFakeApi,
  TmFakeTabBus,
  TmFakeTelemetry,
  tmTestMetadata,
  tmWarmTenant,
} from '@tellma/core-testing';
import { TM_ACTIVE_LOCALE, TM_CALENDAR, TM_TIME_ZONE, TmTheme } from '@tellma/core-ui';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import { tmFormatDate } from '@tellma/core-ui/l10n';

import { TmApiError, type TmProblem, type TmProblemError } from '../api/tm-api-error';
import { TM_API_LOCALE, TM_API_UPDATES } from '../api/tm-api-seams';
import { provideTellma } from '../app/provide-tellma';
import type { TmAppConfig, TmCalendarDefinition, TmLanguageDefinition } from '../app/tm-app-config';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import type { TmClientMetadata } from '../metadata/tm-client-metadata';
import { TM_T17, tmLoadTestPage, tmNavigate, tmRouteTestConfig, tmSettle } from '../routes/tm-app.testing.util';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import type { TmLaunchDocs, TmMeLocaleResult, TmMeResult, TmTenantSettingsForClient } from '../state/launch/tm-launch-docs';
import { TmClientStore } from '../state/store/tm-client-store';
import { TM_STORE_HOST, type TmStoreHost } from '../state/store/tm-store-host';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';
import { tmEnglish } from './tm-english';
import { tmBrowserResolves } from './tm-locale-resolution';
import { TmLocalization } from './tm-localization';
import { TmServerText } from './tm-server-text';

// The localization service in a whole app over the fake API: Acme's two
// languages (English, default region GB; Arabic, default region SA), tenant
// 17 with both as content languages and the Gregorian and Umm al-Qura
// calendars, and a user whose preferences each test sets.

/** Acme's languages, as the client metadata lists them. */
const METADATA: TmClientMetadata = tmTestMetadata({
  languages: [
    // ZZ is a region no browser has data for, so the browser check falls back on it.
    { code: 'en', nativeName: 'English', dir: 'ltr', region: 'GB', regions: ['GB', 'AE', 'US', 'ZZ'], nativeDigits: null },
    { code: 'ar', nativeName: 'العربية', dir: 'rtl', region: 'SA', regions: ['SA', 'AE', 'EG'], nativeDigits: 'arab' },
  ],
  calendars: ['gc', 'uq'],
});

/** Arabic, with a few of the platform's strings, one of them carrying a pasted direction mark. */
const ARABIC: TmLanguageDefinition = {
  code: 'ar',
  dir: 'rtl',
  strings: () => Promise.resolve({ tm: { errors: { retry: 'حاول‏ مرة أخرى' } }, tmUi: {} }),
};

/** Tenant 17's settings: English and Arabic, both calendars, Riyadh's zone and the UAE's formats. */
const SETTINGS: TmTenantSettingsForClient = {
  tenantId: 17,
  names: ['Acme', 'أكمي'],
  languages: [
    { code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false },
    { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true },
  ],
  calendars: ['gc', 'uq'],
  timeZone: 'Asia/Riyadh',
  region: 'AE',
  entries: {},
};

/** Tenant 17's envelope for a user with the given preferences. */
function me(preferences: Partial<TmMeResult['user']> = {}): TmMeResult {
  return {
    user: { id: 42, kind: 'Individual', state: 'Joined', name: 'Farah', modifiedAt: '2026-11-01T00:00:00.0000000+00:00', ...preferences },
    preferencesTag: 'P1',
    preferences: {},
    access: { tag: 'A1', formatVersion: 1, isSystem: false, securables: [], problems: [] },
    tags: { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1' },
  };
}

/** What a test drives and inspects. */
interface Rig {
  readonly l10n: TmLocalization;
  readonly api: TmFakeApi;
  readonly store: TmClientStore;
  readonly bus: TmFakeTabBus;
  readonly telemetry: TmFakeTelemetry;
  readonly warnings: { readonly mock: { readonly calls: readonly unknown[][] } };
}

/** A device's local storage and its events, which a test writes as another tab would. */
interface Device {
  /** The local storage's items. */
  readonly items: Map<string, string>;
  /** Where the storage events fire. */
  readonly events: EventTarget;
  /** The client store's browser over them. */
  readonly host: TmStoreHost;
}

/** A device whose `<html>` carries a language: the boot record starts from it. */
function device(lang = 'en'): Device {
  const items = new Map<string, string>();
  const events = new EventTarget();
  const host: TmStoreHost = {
    memory: true,
    indexedDB: null,
    localStorage: {
      getItem: (key) => items.get(key) ?? null,
      setItem: (key, value) => void items.set(key, value),
      removeItem: (key) => void items.delete(key),
    },
    caches: null,
    events,
    documentLanguage: () => ({ lang, dir: 'ltr' }),
    persist: () => undefined,
  };
  return { items, events, host };
}

/** Writes the boot record as another tab does, and fires the storage event this tab hears. */
function writeBootElsewhere(target: Device, lang: string, dir: 'ltr' | 'rtl'): void {
  const record = JSON.parse(target.items.get('tm.boot') ?? 'null') as Record<string, unknown> | null;
  target.items.set('tm.boot', JSON.stringify({ v: 1, theme: 'system', sub: null, lastTenantId: null, ...record, lang, dir }));
  target.events.dispatchEvent(new StorageEvent('storage', { key: 'tm.boot' }));
}

/** What a test changes in the app's configuration. */
interface ConfigOptions {
  /** The offered languages, English and {@link ARABIC} when omitted. */
  readonly languages?: readonly TmLanguageDefinition[];
  /** The distribution's calendars. */
  readonly calendars?: readonly TmCalendarDefinition[];
  /** The Core chunk's loader. */
  readonly core?: () => Promise<TmClientMetadata>;
}

/** The test app's configuration: Acme's languages, with string packs for both. */
function config(options: ConfigOptions = {}): TmAppConfig {
  const base = tmRouteTestConfig();
  return {
    ...base,
    metadata: {
      ...base.metadata,
      core: options.core ?? (() => Promise.resolve(METADATA)),
      strings: {
        en: () =>
          Promise.resolve({
            Distribution_DisplayName: 'Acme',
            Problem_forbidden: "You don't have permission for that",
            Problem_forbidden_Detail: 'You need {permission}.',
            Tree_Cycle: '{property} would make a cycle.',
            Gl_Center_Parent: 'Parent',
            Due: 'Due {d, date, long}',
            Named: 'Named {name}',
            'tm.home.title': 'Collides',
          }),
        ar: () =>
          Promise.resolve({
            Distribution_DisplayName: 'أكمي',
            Problem_forbidden: 'ليست لديك صلاحية لذلك',
            Gl_Center_Parent: 'الأصل',
          }),
      },
    },
    languages: options.languages ?? [tmEnglish(), ARABIC],
    ...(options.calendars && { calendars: options.calendars }),
  };
}

/** The providers every test app has beside its configuration. */
const PROVIDERS: (Provider | EnvironmentProviders)[] = [
  provideTellmaTesting(),
  provideTellmaStateTesting(),
  provideLocationMocks(),
  { provide: TM_API_UPDATES, useValue: { check: () => Promise.resolve('none') } },
  { provide: TM_DEV_MODE, useValue: () => true },
];

/** Tenant 17's launch documents for a user with the given preferences. */
function launchDocs(preferences: Partial<TmMeResult['user']> = {}, settings = SETTINGS): TmLaunchDocs {
  return { settings, me: me(preferences), tenant: { id: 17, category: 'Live', state: 'Active', realtime: true } };
}

/** A promise a test settles when it chooses. */
function deferred(): { readonly promise: Promise<void>; readonly resolve: () => void } {
  let resolve!: () => void;
  const promise = new Promise<void>((settle) => (resolve = settle));
  return { promise, resolve };
}

/** What a test changes in the rig beyond the app's configuration. */
interface RigOptions extends ConfigOptions {
  /** The user's stored preferences in tenant 17. */
  readonly preferences?: Partial<TmMeResult['user']>;
  /** Tenant 17's settings, {@link SETTINGS} when omitted. */
  readonly settings?: TmTenantSettingsForClient;
  /** Whether the rig routes into tenant 17; it does when omitted. */
  readonly tenant?: boolean;
  /** The device, an in-memory one whose `<html>` is English when omitted. */
  readonly device?: Device;
}

/** Starts the app, warms tenant 17 with the user's preferences and, unless told otherwise, routes into it. */
async function rig(options: RigOptions = {}): Promise<Rig> {
  const warnings = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
  const host: Provider[] = options.device ? [{ provide: TM_STORE_HOST, useValue: options.device.host }] : [];
  TestBed.configureTestingModule({ providers: [provideTellma(config(options)), ...PROVIDERS, ...host] });
  await TestBed.inject(ApplicationInitStatus).donePromise;
  const store = TestBed.inject(TmClientStore);
  const docs = launchDocs(options.preferences, options.settings);
  await tmWarmTenant(store, TM_T17, docs);
  const api = TestBed.inject(TmFakeApi);
  api.respond('client/sync', { tenant: docs.tenant, tags: me().tags, settings: docs.settings, me: docs.me, staleEntities: [] });
  if (options.tenant !== false) {
    await tmNavigate('/17');
  }
  await TestBed.inject(TmLocalization).ɵready();
  await tmSettle();
  const bus = TestBed.inject(TmFakeTabBus);
  return { l10n: TestBed.inject(TmLocalization), api, store, bus, telemetry: TestBed.inject(TmFakeTelemetry), warnings };
}

/** The write result of a locale write that stored the given members. */
function written(members: Partial<TmMeLocaleResult>): TmMeLocaleResult {
  return { modifiedAt: '2026-11-02T00:00:00.0000000+00:00', preferencesTag: 'P2', ...members };
}

/** A deferred Arabic: its strings arrive when the test resolves the promise. */
function deferredArabic(strings: Promise<void>): TmLanguageDefinition {
  return { ...ARABIC, strings: () => strings.then(() => ARABIC.strings()) };
}

/** A deferred Umm al-Qura calendar: its entry point arrives when the test resolves the promise. */
function deferredUmalqura(load: Promise<void>): TmCalendarDefinition {
  return { code: 'uq', load: () => load.then(() => tmUmalquraCalendar()) };
}

/** The switches the tab announced to the other tabs. */
function switches(bus: TmFakeTabBus): unknown[] {
  return bus.posted().filter((message) => message.type === 'l10n.switched');
}

/** Waits past the locale write's coalescing delay, and lets its answer land. */
async function afterTheWrite(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 700));
  await tmSettle();
}

/** The events the fake telemetry recorded under a name. */
function events(telemetry: TmFakeTelemetry, name: string): unknown[] {
  return telemetry
    .recent()
    .filter((item) => item.name === name)
    .map((item) => item.props);
}

afterEach(() => {
  vi.restoreAllMocks();
  document.documentElement.lang = 'en';
  document.documentElement.dir = 'ltr';
});

describe('TmLocalization', () => {
  describe('in a tenant', () => {
    it("resolves every member from the user's preferences, the tenant's settings and the metadata", async () => {
      // The UAE writes Latin digits by default, so the native digits take the extension.
      const { l10n, store } = await rig({ preferences: { preferredLanguage: 'ar', preferredCalendar: 'uq', preferredDigits: 'Native' } });

      expect(l10n.language()).toBe('ar');
      expect(l10n.dir()).toBe('rtl');
      expect(l10n.region()).toBe('AE');
      expect(l10n.digits()).toBe('Native');
      expect(l10n.locale()).toBe('ar-AE-u-nu-arab');
      expect(l10n.calendarCode()).toBe('uq');
      expect(l10n.calendar().id).toBe('islamic-umalqura');
      expect(l10n.contentSlot()).toBe(2);
      expect(l10n.timeZone()).toBe('Asia/Riyadh');
      expect(l10n.displayName()).toBe('أكمي');
      expect(l10n.languages().map((language) => language.code)).toEqual(['en', 'ar']);
      expect(l10n.calendars()).toEqual(['gc', 'uq']);
      // The stored preference settled the language, so the next warm launch starts in it.
      expect(store.boot.read()).toEqual(expect.objectContaining({ lang: 'ar', dir: 'rtl' }));
    });

    it("keeps Accept-Language at the user's locale when the browser check falls back, and reports the fallback once", async () => {
      const { l10n, telemetry } = await rig({ preferences: { preferredRegion: 'ZZ' } });

      expect(l10n.region()).toBe('ZZ');
      expect(l10n.locale()).toBe('en-GB');
      expect(TestBed.inject(TM_API_LOCALE).acceptLanguage()).toBe('en-ZZ');
      // A second resolution of the same language reports nothing more.
      l10n.setLocale({ digits: 'Latin' });
      await tmSettle();
      expect(l10n.locale()).toBe('en-GB');
      expect(events(telemetry, 'tm.l10n.locale-fallback')).toEqual([{ language: 'en' }]);
    });

    it('points the document, Transloco, the library and the requests at the resolved members', async () => {
      const { l10n } = await rig({ preferences: { preferredLanguage: 'ar', preferredCalendar: 'uq', preferredTimeZone: 'Africa/Cairo' } });

      expect(document.documentElement.lang).toBe('ar');
      expect(document.documentElement.dir).toBe('rtl');
      expect(TestBed.inject(TranslocoService).getActiveLang()).toBe('ar');
      expect(TestBed.inject(TM_ACTIVE_LOCALE)()).toBe(l10n.locale());
      expect(TestBed.inject(TM_CALENDAR)().id).toBe('islamic-umalqura');
      expect(TestBed.inject(TM_TIME_ZONE)()).toBe('Africa/Cairo');
      const requests = TestBed.inject(TM_API_LOCALE);
      expect(requests.acceptLanguage()).toBe('ar-AE');
      expect(requests.calendarCode()).toBe('uq');
    });

    it('takes the tenant primary when the user has no language and the boot record names none offered', async () => {
      // The device's boot record names French, which the distribution does not offer.
      const arabicFirst: TmTenantSettingsForClient = { ...SETTINGS, languages: [...SETTINGS.languages].reverse() };
      const { l10n } = await rig({ device: device('fr'), settings: arabicFirst });

      // The default language is English: Arabic comes from the tenant's primary alone.
      expect(l10n.language()).toBe('ar');
      expect(l10n.locale()).toBe('ar-AE');
      expect(l10n.calendarCode()).toBe('gc');
      expect(l10n.contentSlot()).toBe(1);
    });

    it("takes the boot record's language over the tenant primary when the user has none", async () => {
      const arabicFirst: TmTenantSettingsForClient = { ...SETTINGS, languages: [...SETTINGS.languages].reverse() };
      const { l10n } = await rig({ settings: arabicFirst });

      expect(l10n.language()).toBe('en');
      expect(l10n.contentSlot()).toBe(2);
    });

    it('changes nothing until what an adopted preference needs loads, then each part in one pass', async () => {
      const strings = deferred();
      const load = deferred();
      const { l10n } = await rig({ languages: [tmEnglish(), deferredArabic(strings.promise)], calendars: [deferredUmalqura(load.promise)] });
      const transloco = TestBed.inject(TranslocoService);
      const requests = TestBed.inject(TM_API_LOCALE);
      const retry = l10n.ɵtext('tm.errors.retry');
      const root = document.documentElement;
      const sample = (): string =>
        [l10n.language(), l10n.dir(), root.dir, transloco.getActiveLang(), retry(), l10n.calendarCode(), l10n.calendar().id, requests.calendarCode()].join(' | ');
      const seen: string[] = [];
      const watch = TestBed.runInInjectionContext(() => effect(() => seen.push(sample())));
      await tmSettle();
      const start = 'en | ltr | ltr | en | Try again | gc | gregory | gc';
      expect(sample()).toBe(start);

      // Another device's change arrives as an adoption: the inputs move, not a setter.
      TestBed.inject(TmCacheRefresher).adopt(17, { locale: written({ preferredLanguage: 'ar', preferredCalendar: 'uq' }) });
      await tmSettle();
      expect(sample()).toBe(start);

      load.resolve();
      await tmSettle();
      const hijri = 'en | ltr | ltr | en | Try again | uq | islamic-umalqura | uq';
      expect(sample()).toBe(hijri);

      strings.resolve();
      await tmSettle();
      const arabic = 'ar | rtl | rtl | ar | حاول مرة أخرى | uq | islamic-umalqura | uq';
      expect(sample()).toBe(arabic);
      expect(new Set(seen)).toEqual(new Set([start, hijri, arabic]));
      watch.destroy();
    });

    it("formats a day-first language's numeric dates day-first on the default language's locale too", async () => {
      // A distribution whose default language is US English, and Oromo, which Chromium lacks.
      const metadata = tmTestMetadata({
        languages: [
          { code: 'en', nativeName: 'English', dir: 'ltr', region: 'US', regions: ['US'], nativeDigits: null },
          { code: 'om', nativeName: 'Afaan Oromoo', dir: 'ltr', region: 'ET', regions: ['ET'], nativeDigits: null },
        ],
        calendars: ['gc'],
      });
      const oromo: TmLanguageDefinition = { code: 'om', dir: 'ltr', strings: () => Promise.resolve({ tm: {}, tmUi: {} }) };
      const settings: TmTenantSettingsForClient = {
        ...SETTINGS,
        languages: [SETTINGS.languages[0], { code: 'om', nativeName: 'Afaan Oromoo', symbol: 'Om', isRightToLeft: false }],
        calendars: ['gc'],
      };
      const { l10n } = await rig({ core: () => Promise.resolve(metadata), languages: [tmEnglish(), oromo], settings, preferences: { preferredLanguage: 'om' } });

      expect(l10n.locale()).toBe(tmBrowserResolves('om-ET') ? 'om-ET' : 'en-US-x-om');
      expect(TestBed.inject(TM_API_LOCALE).acceptLanguage()).toBe('om-ET');
      expect(tmFormatDate('2026-03-05', l10n.locale())).toBe('05/03/2026');
      expect(tmFormatDate('2026-03-05', TestBed.inject(TM_ACTIVE_LOCALE)())).toBe('05/03/2026');
    });
  });

  describe('at startup', () => {
    it("resolves the address's tenant before the first render, so its first frame and calls are in the user's language and calendar", async () => {
      const address = location.href;
      history.replaceState(history.state, '', '/17');
      try {
        const core = deferred();
        TestBed.configureTestingModule({
          providers: [provideTellma(config({ core: () => core.promise.then(() => METADATA) })), ...PROVIDERS],
        });
        // The initializers start, and wait for the Core chunk while the test warms the tenant.
        const status = TestBed.inject(ApplicationInitStatus);
        const store = TestBed.inject(TmClientStore);
        await vi.waitFor(() => expect(store.boot.read().sub).not.toBeNull());
        await tmWarmTenant(store, TM_T17, launchDocs({ preferredLanguage: 'ar', preferredCalendar: 'uq' }));
        core.resolve();
        await status.donePromise;

        // The first frame: nothing has navigated yet.
        const l10n = TestBed.inject(TmLocalization);
        const requests = TestBed.inject(TM_API_LOCALE);
        expect(l10n.calendarCode()).toBe('uq');
        expect(TestBed.inject(TM_CALENDAR)().id).toBe('islamic-umalqura');
        expect(requests.calendarCode()).toBe('uq');
        expect(l10n.language()).toBe('ar');
        expect(document.documentElement.dir).toBe('rtl');

        // Nothing flips once the router enters the tenant.
        const seen: string[] = [];
        const watch = TestBed.runInInjectionContext(() => effect(() => seen.push(`${l10n.language()} ${requests.calendarCode()}`)));
        await tmNavigate('/17');
        await tmSettle();
        expect(new Set(seen)).toEqual(new Set(['ar uq']));
        watch.destroy();
      } finally {
        history.replaceState(history.state, '', address);
      }
    });
  });

  describe('entering a tenant', () => {
    it("creates the page only once the tenant's language and calendar have loaded, so its first frame and read use them", async () => {
      const strings = deferred();
      const load = deferred();
      const { l10n } = await rig({
        tenant: false,
        preferences: { preferredLanguage: 'ar', preferredCalendar: 'uq' },
        languages: [tmEnglish(), deferredArabic(strings.promise)],
        calendars: [deferredUmalqura(load.promise)],
      });
      expect(l10n.language()).toBe('en');
      const seen: string[] = [];
      // The service itself never shows a part ahead of its load either.
      const transloco = TestBed.inject(TranslocoService);
      const retry = l10n.ɵtext('tm.errors.retry');
      const states: string[] = [];
      const sample = (): string =>
        [l10n.language(), l10n.dir(), transloco.getActiveLang(), retry(), l10n.calendarCode(), l10n.calendar().id].join(' | ');
      const watch = TestBed.runInInjectionContext(() => effect(() => states.push(sample())));

      /** A page that records what it sees from its creation on. */
      @Component({ selector: 'tm-sampling-page', template: '' })
      class SamplingPage {
        /** Records the language, the calendar and what a read would send, at creation and on every change. */
        constructor() {
          const requests = inject(TM_API_LOCALE);
          effect(() => seen.push(`${l10n.language()} ${l10n.calendarCode()} ${requests.acceptLanguage()} ${requests.calendarCode()}`));
          seen.push(`${l10n.language()} ${l10n.calendarCode()} ${requests.acceptLanguage()} ${requests.calendarCode()}`);
        }
      }
      tmLoadTestPage.mockImplementationOnce(() => Promise.resolve(SamplingPage));

      await tmNavigate('/17/credit-review');
      await tmSettle();
      expect(seen).toEqual([]);
      load.resolve();
      await tmSettle();
      expect(seen).toEqual([]);

      strings.resolve();
      await tmSettle();
      expect(seen.length).toBeGreaterThan(0);
      expect(new Set(seen)).toEqual(new Set(['ar uq ar-AE uq']));
      expect(new Set(states)).toEqual(
        new Set([
          'en | ltr | en | Try again | gc | gregory',
          'en | ltr | en | Try again | uq | islamic-umalqura',
          'ar | rtl | ar | حاول مرة أخرى | uq | islamic-umalqura',
        ]),
      );
      watch.destroy();
    });
  });

  describe('the other tabs', () => {
    it("announces a switch to the other tabs, never a preference that settles the language", async () => {
      const { l10n, store, bus } = await rig({ preferences: { preferredLanguage: 'ar' } });
      // The stored preference settled Arabic: the boot record keeps it for this tab's next launch.
      expect(store.boot.read().lang).toBe('ar');
      expect(switches(bus)).toEqual([]);

      l10n.setLanguage('en');
      await tmSettle();

      expect(switches(bus)).toEqual([{ type: 'l10n.switched', lang: 'en' }]);
    });

    it("leaves a tenantless tab and a tenant tab with no preference as they are when another tab settles a preference, and follows a switch", async () => {
      for (const tenant of [false, true]) {
        const here = device();
        const { l10n } = await rig({ tenant, device: here });
        expect(l10n.language()).toBe('en');

        // Another tab opens an Arabic-preferring tenant: its settled language reaches the boot record.
        writeBootElsewhere(here, 'ar', 'rtl');
        await tmSettle();
        expect(l10n.language(), `tenant: ${tenant}`).toBe('en');
        expect(document.documentElement.dir).toBe('ltr');

        // Another tab's switch is followed.
        TestBed.inject(TmFakeTabBus).receive({ type: 'l10n.switched', lang: 'ar' });
        await tmSettle();
        expect(l10n.language(), `tenant: ${tenant}`).toBe('ar');
        TestBed.resetTestingModule();
      }
    });
  });

  describe('outside a tenant', () => {
    it("takes the boot record's language, its default region, gc and the device's zone", async () => {
      const { l10n, store } = await rig({ tenant: false });
      l10n.setLanguage('ar');
      await tmSettle();

      expect(l10n.language()).toBe('ar');
      expect(l10n.locale()).toBe('ar-SA');
      expect(l10n.calendarCode()).toBe('gc');
      expect(l10n.timeZone()).toBe(new Intl.DateTimeFormat().resolvedOptions().timeZone);
      expect(l10n.calendars()).toEqual(['gc']);
      expect(store.boot.read().lang).toBe('ar');
    });
  });

  describe('a switch', () => {
    it('changes nothing until the strings land, then switches every member in one pass and keeps the language in the boot record', async () => {
      const strings = deferred();
      const arabic: TmLanguageDefinition = { ...ARABIC, strings: () => strings.promise.then(() => ARABIC.strings()) };
      const { l10n, store, telemetry, api } = await rig({ languages: [tmEnglish(), arabic] });
      api.respond('users/me/locale', written({ preferredLanguage: 'ar' }));
      const transloco = TestBed.inject(TranslocoService);
      const retry = l10n.ɵtext('tm.errors.retry');
      // Every pass of change detection sees one whole state, never a mix.
      const seen: string[] = [];
      const root = document.documentElement;
      const sample = (): string =>
        [l10n.language(), l10n.dir(), l10n.locale(), l10n.contentSlot(), retry(), root.lang, root.dir, transloco.getActiveLang()].join(' | ');
      const watch = TestBed.runInInjectionContext(() => effect(() => seen.push(sample())));
      await tmSettle();
      const english = 'en | ltr | en-AE | 1 | Try again | en | ltr | en';
      expect(sample()).toBe(english);

      l10n.setLanguage('ar');
      await tmSettle();
      // The strings are still loading: nothing has moved.
      expect(sample()).toBe(english);
      expect(store.boot.read().lang).toBe('en');

      strings.resolve();
      await tmSettle();
      const arabicState = 'ar | rtl | ar-AE | 2 | حاول مرة أخرى | ar | rtl | ar';
      expect(sample()).toBe(arabicState);
      expect(new Set(seen)).toEqual(new Set([english, arabicState]));
      expect(store.boot.read()).toEqual(expect.objectContaining({ lang: 'ar', dir: 'rtl' }));
      expect(events(telemetry, 'tm.l10n.language-changed')).toEqual([{ from: 'en', to: 'ar' }]);
      // Entering the tenant took its formats, which is no change.
      expect(events(telemetry, 'tm.l10n.locale-changed')).toEqual([{ from: 'en-AE', to: 'ar-AE' }]);
      watch.destroy();
    });

    it('changes no date until the calendar loads, then every date in one pass', async () => {
      const load = deferred();
      const { l10n, api } = await rig({ calendars: [{ code: 'uq', load: () => load.promise.then(() => tmUmalquraCalendar()) }] });
      api.respond('users/me/locale', written({ preferredCalendar: 'uq' }));
      const due = l10n.ɵtext('Due', { d: '2026-03-05' });
      const requests = TestBed.inject(TM_API_LOCALE);
      const seen: string[] = [];
      const sample = (): string => [l10n.calendarCode(), l10n.calendar().id, requests.calendarCode(), due()].join(' | ');
      const watch = TestBed.runInInjectionContext(() => effect(() => seen.push(sample())));
      await tmSettle();
      const gregorian = 'gc | gregory | gc | Due 5 March 2026';
      expect(sample()).toBe(gregorian);

      l10n.setCalendar('uq');
      await tmSettle();
      expect(sample()).toBe(gregorian);

      load.resolve();
      await tmSettle();
      const hijri = 'uq | islamic-umalqura | uq | Due Ramadan 16, 1447';
      expect(sample()).toBe(hijri);
      expect(new Set(seen)).toEqual(new Set([gregorian, hijri]));
      watch.destroy();
    });

    it('re-renders every message on a language switch, with English for the keys a language lacks and no bidi control', async () => {
      const { l10n } = await rig();
      const retry = l10n.ɵtext('tm.errors.retry');
      const reload = l10n.ɵtext('tm.errors.reload');
      expect(retry()).toBe('Try again');

      l10n.setLanguage('ar');
      await tmSettle();

      expect(retry()).toBe('حاول مرة أخرى');
      expect(reload()).toBe('Reload');
    });

    it("switches the calendar once the platform's entry point loads", async () => {
      const { l10n, telemetry, api } = await rig();
      api.respond('users/me/locale', written({ preferredCalendar: 'uq' }));
      const due = l10n.ɵtext('Due', { d: '2026-03-05' });
      expect(due()).toBe('Due 5 March 2026');

      l10n.setCalendar('uq');
      await tmSettle();

      expect(l10n.calendar().id).toBe('islamic-umalqura');
      expect(due()).toBe('Due Ramadan 16, 1447');
      expect(events(telemetry, 'tm.l10n.calendar-changed')).toEqual([{ from: 'gc', to: 'uq' }]);
    });

    it('refuses a language or a calendar the tenant does not offer, in development', async () => {
      const { l10n } = await rig();

      expect(() => l10n.setLanguage('fr')).toThrowError(/"fr" is not offered/);
      expect(() => l10n.setCalendar('et')).toThrowError(/"et" is not offered/);
    });

    it("returns a member set to null to the tenant's default, and the boot record follows the language", async () => {
      const { l10n, store, api } = await rig({ preferences: { preferredLanguage: 'ar', preferredCalendar: 'uq' } });
      api.respond('users/me/locale', written({}));

      l10n.setLocale({ language: null, calendar: null });
      await tmSettle();

      expect(l10n.language()).toBe('en');
      expect(l10n.calendarCode()).toBe('gc');
      expect(store.boot.read().lang).toBe('en');
    });
  });

  describe('the locale write', () => {
    it('sends the changes of one burst in one write, with the reset members, and adopts the result', async () => {
      const { l10n, api } = await rig({ preferences: { preferredDigits: 'Native' } });
      const result = written({ preferredLanguage: 'ar', preferredRegion: 'EG' });
      api.respond('users/me/locale', result);
      const adopt = vi.spyOn(TestBed.inject(TmCacheRefresher), 'adopt');

      l10n.setLanguage('ar');
      l10n.setLocale({ region: 'EG' });
      l10n.setLocale({ digits: null });
      await tmSettle();
      expect(l10n.localeSave()).toBe('saving');
      expect(l10n.locale()).toBe('ar-EG');
      await afterTheWrite();

      const writes = api.calls().filter((call) => call.route === 'users/me/locale');
      expect(writes.map((call) => call.body)).toEqual([{ language: 'ar', region: 'EG', reset: ['Digits'] }]);
      expect(l10n.localeSave()).toBe('saved');
      // The result is adopted: the stored envelope moves, and the change stands on it.
      expect(adopt).toHaveBeenCalledWith(17, { locale: result });
      const user = TestBed.inject(TmLaunchCache).docs.me()?.user;
      expect(user?.preferredLanguage).toBe('ar');
      expect(user?.preferredRegion).toBe('EG');
      expect(user?.preferredDigits).toBeUndefined();
      expect(l10n.locale()).toBe('ar-EG');
      expect(l10n.digits()).toBeNull();
    });

    it('never lets an earlier write that lands undo a later change of the member, and the change stands when its own write is refused', async () => {
      const { l10n, api, store } = await rig();
      const answers: { readonly land: (result: TmMeLocaleResult) => void; readonly fail: (error: unknown) => void }[] = [];
      api.respond(
        'users/me/locale',
        () => new Promise<TmMeLocaleResult>((resolve, reject) => void answers.push({ land: resolve, fail: reject })),
      );
      l10n.setLanguage('ar');
      await afterTheWrite();
      expect(answers).toHaveLength(1);

      // Back to English while the Arabic write is in flight.
      l10n.setLanguage('en');
      await tmSettle();
      const seen: string[] = [];
      const watch = TestBed.runInInjectionContext(() => effect(() => seen.push(l10n.language())));
      await tmSettle();

      answers[0].land(written({ preferredLanguage: 'ar' }));
      await tmSettle();
      expect(TestBed.inject(TmLaunchCache).docs.me()?.user.preferredLanguage).toBe('ar');
      await afterTheWrite();
      expect(answers).toHaveLength(2);
      answers[1].fail(new TmApiError({ kind: 'validation' }));
      await tmSettle();

      expect(l10n.localeSave()).toBe('failed');
      expect(new Set(seen)).toEqual(new Set(['en']));
      expect(l10n.language()).toBe('en');
      expect(store.boot.read().lang).toBe('en');
      watch.destroy();
    });

    it('keeps an offline write queued and retries it once the server is reachable', async () => {
      const { l10n, api } = await rig();
      let offline = true;
      api.respond('users/me/locale', () => {
        if (offline) {
          throw new TmApiError({ kind: 'offline' });
        }
        return written({ preferredRegion: 'US' });
      });

      l10n.setLocale({ region: 'US' });
      await afterTheWrite();
      expect(l10n.localeSave()).toBe('saving');
      expect(l10n.locale()).toBe('en-US');

      offline = false;
      api.setOnline(false);
      await tmSettle();
      api.setOnline(true);
      await tmSettle();

      const writes = api.calls().filter((call) => call.route === 'users/me/locale');
      expect(writes.map((call) => call.body)).toEqual([{ region: 'US' }, { region: 'US' }]);
      expect(l10n.localeSave()).toBe('saved');
    });

    it('reports a refusal, and the applied change stands', async () => {
      const { l10n, api } = await rig();
      api.respond('users/me/locale', () => {
        throw new TmApiError({ kind: 'validation' });
      });

      l10n.setLocale({ timeZone: 'Africa/Cairo' });
      await afterTheWrite();

      expect(l10n.localeSave()).toBe('failed');
      expect(l10n.timeZone()).toBe('Africa/Cairo');
    });

    it('sends a queued change at once when the page hides, never reading the answer', async () => {
      const { l10n, api } = await rig();

      l10n.setLocale({ region: 'US' });
      await tmSettle();
      window.dispatchEvent(new Event('pagehide'));

      expect(api.calls().filter((call) => call.route === 'users/me/locale')).toEqual([
        expect.objectContaining({ kind: 'send', body: { region: 'US' } }),
      ]);
      // Nothing is left saving for a return from the back-forward cache.
      expect(l10n.localeSave()).toBe('idle');
    });
  });

  describe('the strings', () => {
    it("drops a string pack's key in a client namespace, with a development warning", async () => {
      const { l10n, warnings } = await rig();

      expect(l10n.ɵtext('tm.home.title')()).toBe('Home');
      expect(warnings).toHaveBeenCalledWith(expect.stringContaining('string-key-collision'));
    });

    it('renders a missing key as itself, with one development warning', async () => {
      const { l10n, warnings } = await rig();

      expect(l10n.ɵtext('Nowhere_Key')()).toBe('Nowhere_Key');
      expect(l10n.ɵtext('Nowhere_Key')()).toBe('Nowhere_Key');
      expect(warnings.mock.calls.filter(([message]: readonly unknown[]) => String(message).includes('Nowhere_Key'))).toHaveLength(1);
    });
  });

  describe('the theme', () => {
    it("starts from the boot record's theme, writes its changes back and points theme-color at the scheme's colour", async () => {
      document.documentElement.style.setProperty('--tm-theme-color', '#001722');
      const { store } = await rig();
      const theme = TestBed.inject(TmTheme);
      expect(theme.setting()).toBe(store.boot.read().theme);

      theme.set('dark');
      await tmSettle();

      expect(store.boot.read().theme).toBe('dark');
      expect(document.head.querySelector<HTMLMetaElement>('meta[name="theme-color"]')?.content).toBe('#001722');
      document.documentElement.style.removeProperty('--tm-theme-color');
    });
  });
});

describe('TmServerText', () => {
  it("renders a problem's title and detail in the active language, else the server's own text", async () => {
    const { l10n } = await rig();
    const text = TestBed.inject(TmServerText);
    const problem = { code: 'forbidden', instance: 't', status: 403, arguments: { permission: 'Read' }, title: 'Forbidden' } as TmProblem;
    const rendered = text.problem(problem);
    expect(rendered()).toEqual({ title: "You don't have permission for that", detail: 'You need Read.' });

    l10n.setLanguage('ar');
    await tmSettle();

    expect(rendered()).toEqual({ title: 'ليست لديك صلاحية لذلك', detail: 'You need Read.' });
  });

  it("renders a validation item with its property's label, else the server's message", async () => {
    await rig();
    const text = TestBed.inject(TmServerText);
    const item = { code: 'Tree.Cycle', message: 'server', arguments: { property: 'parent' } } as TmProblemError;

    expect(text.error(item, 'gl.Center')()).toBe('Parent would make a cycle.');
    expect(text.error({ code: 'Nowhere.Code', message: 'The server says so.' } as TmProblemError)()).toBe('The server says so.');
  });

  it("renders a localized text argument in the active content slot, else slot 1, and knows the packs' keys", async () => {
    const { l10n } = await rig();
    const text = TestBed.inject(TmServerText);
    const named = text.message('Named', { name: ['Cash', 'النقد', null] });
    expect(named()).toBe('Named Cash');

    l10n.setLanguage('ar');
    await tmSettle();

    expect(named()).toBe('Named النقد');
    expect(text.has('Problem_forbidden')).toBe(true);
    expect(text.has('Due')).toBe(true);
    expect(text.has('Nowhere_Key')).toBe(false);
  });
});
