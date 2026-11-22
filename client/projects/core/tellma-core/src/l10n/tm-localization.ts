// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  computed,
  DestroyRef,
  DOCUMENT,
  effect,
  inject,
  Injectable,
  Injector,
  linkedSignal,
  signal,
  untracked,
  type Signal,
} from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { TM_UI_MESSAGE_CONTEXT } from '@tellma/core-ui';
import { tmGregorianCalendar, tmStripBidiControls, type TmCalendar } from '@tellma/core-ui/l10n';
import { firstValueFrom } from 'rxjs';

import { TM_API_HOST } from '../api/tm-api-host';
import { ɵTM_APP_CONFIG, type TmLanguageDefinition } from '../app/tm-app-config';
import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmLaunchCache } from '../state/launch/tm-launch-cache';
import type { TmDigitStyle, TmUserProfileView } from '../state/launch/tm-launch-docs';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmCacheRefresher } from '../state/sync/tm-cache-refresher';
import {
  tmContentSlot,
  tmFormattingLocale,
  tmLocaleString,
  tmOfferedLanguage,
  tmResolveCalendarCode,
  tmResolveLanguage,
  tmResolveRegion,
  tmSwitchLanguages,
} from './tm-locale-resolution';
import { TmLocaleWrite, type TmLocaleMember, type TmLocaleQueue, type TmLocaleSave } from './tm-locale-write';

// The localization service (spec 0024 §7.1–§7.4, §7.7): the active language,
// region, digits, locale, direction, calendar, content slot and display zone,
// resolved from the launch documents, the boot record and the client
// metadata, and switched in place with no reload and no request beyond the
// locale write. A switch loads what it needs first (the language's strings,
// the calendar's entry point), then applies everything in one change-detection
// pass. A change the user makes stands at once, ahead of the write that saves
// it: a local override of the member covers the stored preference until the
// stored value moves, which the write's adopted result or another tab's
// change does. The service is also the app's message resolver: platform
// (`tm.`) keys and server keys resolve through Transloco, fall back to
// English, and carry no bidi control character (§7.3, §7.9).

/**
 * A change of the user's language and regional formats, as the Language &
 * region section makes it. A member set to `null` returns to the tenant's
 * default; an absent member is unchanged.
 */
export interface TmLocaleChange {
  /** The language, such as `ar`. */
  readonly language?: string | null;
  /** The calendar, such as `uq`. */
  readonly calendar?: string | null;
  /** The display time zone, an IANA id such as `Asia/Riyadh`. */
  readonly timeZone?: string | null;
  /** The region of the regional formats, such as `SA`. */
  readonly region?: string | null;
  /** The digits of the regional formats. */
  readonly digits?: TmDigitStyle | null;
}

/** A change the user made, standing until the stored preference it was made over moves. */
interface TmOverride {
  /** The member's new value; `null` for the tenant's default. */
  readonly value: string | null;
  /** The stored preference when the change was made. */
  readonly base: string | undefined;
}

/** The user's effective preferences: the stored ones under the changes standing over them. */
type TmPreferences = Partial<Record<TmLocaleMember, string>>;

/** Angular's development-mode flag, which a production build defines as false. */
declare const ngDevMode: unknown;

/** The members of the locale write, and the profile fields that store them. */
const FIELDS: Readonly<Record<TmLocaleMember, keyof TmUserProfileView>> = {
  language: 'preferredLanguage',
  calendar: 'preferredCalendar',
  timeZone: 'preferredTimeZone',
  region: 'preferredRegion',
  digits: 'preferredDigits',
};

/** The platform's calendars that load from entry points of their own. */
const CALENDAR_LOADERS: Readonly<Record<string, () => Promise<TmCalendar>>> = {
  uq: () => import('@tellma/core-ui/calendar-umalqura').then((module) => module.tmUmalquraCalendar()),
  et: () => import('@tellma/core-ui/calendar-ethiopic').then((module) => module.tmEthiopicCalendar()),
};

/** The browser's time zone now. */
function browserZone(): string {
  return new Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/**
 * The app's language, regional formats, calendar and display time zone, and
 * the switches between them.
 *
 * @example
 * ```ts
 * const l10n = inject(TmLocalization);
 * const label = computed(() => (l10n.dir() === 'rtl' ? 'right' : 'left'));
 * l10n.setLanguage('ar'); // the strings load, then everything switches in one pass
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmLocalization {
  readonly #config = inject(ɵTM_APP_CONFIG);
  readonly #metadata = inject(TmClientMetadataStore);
  readonly #store = inject(TmClientStore);
  readonly #transloco = inject(TranslocoService);
  readonly #context = inject(TM_UI_MESSAGE_CONTEXT);
  readonly #devMode = inject(TM_DEV_MODE);
  readonly #root = inject(DOCUMENT).documentElement;
  readonly #injector = inject(Injector);

  // The services that reach the router are resolved at first use: the
  // router's own title strategy reads this service, so its construction
  // must not need the router.
  readonly #lazy: { launch?: TmLaunchCache; refresher?: TmCacheRefresher; telemetry?: TmTelemetry; write?: TmLocaleWrite } = {};

  /** The launch documents. */
  get #launch(): TmLaunchCache {
    return (this.#lazy.launch ??= this.#injector.get(TmLaunchCache));
  }

  /** The cache refresher, which knows the routed tenant. */
  get #refresher(): TmCacheRefresher {
    return (this.#lazy.refresher ??= this.#injector.get(TmCacheRefresher));
  }

  /** The telemetry. */
  get #telemetry(): TmTelemetry {
    return (this.#lazy.telemetry ??= this.#injector.get(TmTelemetry));
  }

  /** The locale write, created with the first change it saves. */
  get #write(): TmLocaleWrite {
    return (this.#lazy.write ??= this.#injector.get(TmLocaleWrite));
  }

  /** The offered languages' codes; the first is the distribution's default. */
  readonly #offered: readonly string[] = this.#config.languages.map((language) => language.code);

  /** The boot record's language, which another tab's switch moves too. */
  readonly #bootLang = signal(this.#store.boot.read().lang);

  /** The changes standing over the stored preferences, by tenant. */
  readonly #overrides = signal<ReadonlyMap<number, Partial<Record<TmLocaleMember, TmOverride>>>>(new Map());

  /** The languages whose strings have loaded. */
  readonly #loadedLanguages = signal<ReadonlySet<string>>(new Set());

  /** The calendars that have loaded, by code. */
  readonly #loadedCalendars = signal<ReadonlyMap<string, TmCalendar>>(new Map([['gc', tmGregorianCalendar()]]));

  /** The loads started, by language or calendar code, so each loads once. */
  readonly #loads = new Map<string, Promise<void>>();

  /** Whether a change was queued, so the locale write exists. */
  readonly #writing = signal(false);

  /** Bumped by every language switch and string load, so messages resolve again. */
  readonly #version = signal(0);

  /** The browser's time zone, read again when the page shows. */
  readonly #deviceZone = signal(browserZone());

  /** The switches, applied one after another in the order they were made. */
  #applying: Promise<void> = Promise.resolve();

  /** The keys whose missing message was reported, so each is reported once. */
  readonly #missing = new Set<string>();

  /** The languages whose locale fell back, so each is reported once per launch. */
  readonly #fellBack = new Set<string>();

  /** The routed tenant, or `null` outside a tenant. */
  readonly #tenantId = computed(() => this.#refresher.ɵpartition()?.tenantId ?? null);

  /** The routed tenant's settings, or `undefined` outside a tenant and while they are absent. */
  readonly #settings = computed(() => (this.#tenantId() === null ? undefined : this.#launch.docs.settings()));

  /** The tenant's content languages, primary first, or `undefined` outside a tenant. */
  readonly #contentLanguages = computed(() => this.#settings()?.languages.map((language) => language.code));

  /** The user's effective preferences in the routed tenant; empty outside one. */
  readonly #preferences = computed<TmPreferences>(() => {
    const tenantId = this.#tenantId();
    if (tenantId === null || this.#settings() === undefined) {
      return {};
    }
    const user = this.#launch.docs.me()?.user;
    const overrides = this.#overrides().get(tenantId) ?? {};
    const preferences: TmPreferences = {};
    for (const member of Object.keys(FIELDS) as TmLocaleMember[]) {
      const stored = user?.[FIELDS[member]] as string | undefined;
      const override = overrides[member];
      const value = override !== undefined && override.base === stored ? (override.value ?? undefined) : stored;
      if (value !== undefined) {
        preferences[member] = value;
      }
    }
    return preferences;
  });

  /** The language the inputs resolve to, whether or not its strings have loaded. */
  readonly #wantedLanguage = computed(() => {
    const contentLanguages = this.#contentLanguages();
    return tmResolveLanguage({
      offered: this.#offered,
      bootLang: this.#bootLang(),
      tenant: contentLanguages && { preferred: this.#preferences().language, contentLanguages },
    });
  });

  /** The active language: the resolved one once its strings have loaded, the previous one until then. */
  readonly #language = linkedSignal<{ readonly wanted: string; readonly loaded: ReadonlySet<string> }, string>({
    source: () => ({ wanted: this.#wantedLanguage(), loaded: this.#loadedLanguages() }),
    computation: ({ wanted, loaded }, previous) => (loaded.has(wanted) || previous === undefined ? wanted : previous.value),
  });

  /** The calendar code the inputs resolve to, whether or not its entry point has loaded. */
  readonly #wantedCalendar = computed(() =>
    tmResolveCalendarCode(this.#settings()?.calendars, this.#preferences().calendar),
  );

  /** The active calendar's code: the resolved one once it has loaded, the previous one (at first `gc`) until then. */
  readonly #calendarCode = linkedSignal<{ readonly wanted: string; readonly loaded: ReadonlyMap<string, TmCalendar> }, string>({
    source: () => ({ wanted: this.#wantedCalendar(), loaded: this.#loadedCalendars() }),
    computation: ({ wanted, loaded }, previous) => (loaded.has(wanted) ? wanted : (previous?.value ?? 'gc')),
  });

  /** The active language's metadata, or `undefined` before the Core chunk loads. */
  readonly #languageMetadata = computed(() =>
    this.#metadata.current()?.languages.find((language) => language.code === this.#language()),
  );

  /** The active language, such as `ar`. */
  readonly language: Signal<string> = this.#language.asReadonly();

  /** The active language's text direction. */
  readonly dir: Signal<'ltr' | 'rtl'> = computed(() => this.#definition(this.#language())?.dir ?? 'ltr');

  /** The region of the regional formats, such as `SA`; empty before the client metadata loads. */
  readonly region: Signal<string> = computed(() => {
    const settings = this.#settings();
    const candidates = settings === undefined ? [] : [this.#preferences().region, settings.region];
    return tmResolveRegion(this.#languageMetadata(), candidates) ?? '';
  });

  /** The digits of the regional formats; `null` for the region's default. */
  readonly digits: Signal<TmDigitStyle | null> = computed(() => {
    const settings = this.#settings();
    return settings === undefined ? null : ((this.#preferences().digits as TmDigitStyle | undefined) ?? settings.digits ?? null);
  });

  /** The locale string requests state in `Accept-Language`: the language, region and digits, never the browser check's fallback. */
  readonly #requestLocale = computed(() => {
    const language = this.#language();
    const region = this.region();
    return region === '' ? language : tmLocaleString(`${language}-${region}`, this.digits(), this.#languageMetadata()?.nativeDigits ?? null);
  });

  /** The locale to format with, after the browser check. */
  readonly #formatting = computed(() => {
    const metadata = this.#languageMetadata();
    const fallback = this.#metadata.current()?.languages[0];
    const region = this.region();
    return metadata && fallback && region !== ''
      ? tmFormattingLocale(metadata, region, this.digits(), fallback)
      : { locale: this.#requestLocale(), fellBack: false };
  });

  /** The locale the app formats with, such as `ar-SA` or `ar-SA-u-nu-latn`. */
  readonly locale: Signal<string> = computed(() => this.#formatting().locale);

  /** The active calendar's code, such as `gc` or `uq`. */
  readonly calendarCode: Signal<string> = this.#calendarCode.asReadonly();

  /** The active display calendar. */
  readonly calendar: Signal<TmCalendar> = computed(
    () => this.#loadedCalendars().get(this.#calendarCode()) ?? tmGregorianCalendar(),
  );

  /** Which name slot of a record the active language reads: its position among the tenant's content languages, else 1. */
  readonly contentSlot: Signal<1 | 2 | 3> = computed(() => tmContentSlot(this.#language(), this.#contentLanguages()));

  /** The display time zone: the user's in a tenant, else the tenant's; the browser's outside a tenant. */
  readonly timeZone: Signal<string> = computed(() => {
    const settings = this.#settings();
    return settings === undefined ? this.#deviceZone() : (this.#preferences().timeZone ?? settings.timeZone);
  });

  /** The browser's time zone, read again whenever the page shows. */
  readonly deviceTimeZone: Signal<string> = this.#deviceZone.asReadonly();

  /** The distribution's display name in the active language: its `Distribution_DisplayName`, else the build's. */
  readonly displayName: Signal<string> = computed(() => {
    this.#version();
    const name = this.#transloco.translate<string>('Distribution_DisplayName');
    return name && name !== 'Distribution_DisplayName'
      ? tmStripBidiControls(name)
      : (this.#metadata.current()?.distribution.displayName ?? '');
  });

  /** The languages the language switch offers here. */
  readonly languages: Signal<readonly TmLanguageDefinition[]> = computed(() =>
    tmSwitchLanguages(this.#offered, this.#language(), this.#contentLanguages()).map((code) => this.#definition(code)!),
  );

  /** The calendars the calendar switch offers here: the tenant's pair, or `gc` outside a tenant. */
  readonly calendars: Signal<readonly string[]> = computed(() => this.#settings()?.calendars ?? ['gc']);

  /** The locale write's state: `saving` from the first queued change, `saved` once it lands, `failed` after a refusal. */
  readonly localeSave: Signal<TmLocaleSave> = computed(() => (this.#writing() ? this.#write.state() : 'idle'));

  /** Creates the service, which follows its inputs, the boot record and the page. */
  constructor() {
    const destroyRef = inject(DestroyRef);

    // --- Messages resolve again on a language switch and on every string load ---
    const bump = (): void => this.#version.update((version) => version + 1);
    const languageChanges = this.#transloco.langChanges$.subscribe(bump);
    const loads = this.#transloco.events$.subscribe((event) => {
      if (event.type === 'translationLoadSuccess') {
        bump();
      }
    });

    // --- Another tab's language switch moves the boot record's language here too ---
    const bootChanges = this.#store.boot.changes.subscribe((record) => this.#bootLang.set(record.lang));

    // --- The device's zone, read again when the page shows: no browser event reports a change ---
    const host = inject(TM_API_HOST);
    const reread = (): void => this.#deviceZone.set(browserZone());
    const stops = [
      host.listen('document', 'visibilitychange', () => host.visible() && reread()),
      host.listen('window', 'pageshow', reread),
    ];
    destroyRef.onDestroy(() => {
      languageChanges.unsubscribe();
      loads.unsubscribe();
      bootChanges.unsubscribe();
      stops.forEach((stop) => stop());
    });

    // --- What the inputs resolve to loads when it is missing; the switch follows the load ---
    effect(() => {
      const language = this.#wantedLanguage();
      untracked(() => void this.#loadLanguage(language).catch(() => undefined));
    });
    effect(() => {
      const code = this.#wantedCalendar();
      untracked(() => void this.#loadCalendar(code).catch(() => undefined));
    });

    // --- The document and Transloco follow the active language ---
    effect(() => {
      const language = this.#language();
      const dir = this.dir();
      untracked(() => this.#applyDocument(language, dir));
    });

    // --- Telemetry: the locale's changes, and a browser that lacks the locale ---
    // Entering another tenant takes its formats; only a change within one place is a change.
    let previous: { readonly locale: string; readonly tenantId: number | null } | null = null;
    effect(() => {
      const { locale, fellBack } = this.#formatting();
      const language = this.#language();
      const tenantId = this.#tenantId();
      untracked(() => {
        if (previous !== null && previous.tenantId === tenantId && previous.locale !== locale) {
          this.#telemetry.event('tm.l10n.locale-changed', { from: previous.locale, to: locale });
        }
        previous = { locale, tenantId };
        if (fellBack && !this.#fellBack.has(language)) {
          this.#fellBack.add(language);
          this.#telemetry.event('tm.l10n.locale-fallback', { language });
        }
      });
    });
  }

  /**
   * Switches the language: its strings load, then the language, the
   * direction, the locale and the content slot switch in one pass, the boot
   * record keeps it and, in a tenant, the locale write saves it.
   *
   * @param code - The language, one of {@link TmLocalization.languages}.
   * @throws Error in development when the language is not offered here.
   */
  setLanguage(code: string): void {
    this.setLocale({ language: code });
  }

  /**
   * Switches the calendar: its entry point loads, then every date switches
   * in one pass and, in a tenant, the locale write saves it.
   *
   * @param code - The calendar, one of {@link TmLocalization.calendars}.
   * @throws Error in development when the calendar is not offered here.
   */
  setCalendar(code: string): void {
    this.setLocale({ calendar: code });
  }

  /**
   * Applies a change of the language and regional formats: what it needs
   * loads, then every member switches in one pass and, in a tenant, the
   * locale write saves the members it carries. A member set to `null`
   * returns to the tenant's default. Outside a tenant only the language
   * applies.
   *
   * @param change - The members to change.
   * @throws Error in development when the language or the calendar is not offered here.
   */
  setLocale(change: TmLocaleChange): void {
    if (typeof change.language === 'string' && !this.languages().some((language) => language.code === change.language)) {
      this.#refuse('language', change.language, this.languages().map((language) => language.code));
      return;
    }
    if (typeof change.calendar === 'string' && !this.calendars().includes(change.calendar)) {
      this.#refuse('calendar', change.calendar, this.calendars());
      return;
    }
    this.#applying = this.#applying.then(() => this.#apply(change)).catch((error: unknown) => this.#telemetry.error(error, { where: 'tm.l10n.switch' }));
  }

  /**
   * A platform or server message in the active language, else in English,
   * with its parameters formatted in the active locale, calendar and zone,
   * and no bidi control character. A key missing everywhere gives the
   * fallback, else the key itself, with a development warning. Internal:
   * the platform's own views and the server text read it.
   *
   * @param key - The key, such as `tm.errors.offline.title` or `Problem_forbidden`.
   * @param params - The message's parameters.
   * @param fallback - The text when no loaded strings hold the key.
   * @returns The message, which follows the language and the formats.
   */
  ɵtext(key: string, params?: Readonly<Record<string, unknown>>, fallback?: string): Signal<string> {
    return computed(() => {
      this.#version();
      // The message's numbers and dates format in the locale, calendar and
      // zone, which switch apart from the language.
      this.locale();
      this.calendar();
      this.timeZone();
      const text = this.#transloco.translate<string>(key, { ...this.#context(), ...params });
      if (typeof text === 'string' && text !== key && text !== '') {
        return tmStripBidiControls(text);
      }
      if (fallback !== undefined) {
        return fallback;
      }
      if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode() && !this.#missing.has(key)) {
        this.#missing.add(key);
        console.warn(`No message has the key "${key}" in "${untracked(this.#language)}" or in English.`);
      }
      return key;
    });
  }

  /**
   * Whether the active language's strings or English's hold a key.
   * Internal: the server text reads it.
   *
   * @param key - The key.
   * @returns Whether a message exists.
   */
  ɵhas(key: string): boolean {
    this.#version();
    const has = (language: string): boolean => {
      const value = (this.#transloco.getTranslation(language) as Readonly<Record<string, unknown>>)[key];
      return typeof value === 'string' && value !== '';
    };
    return has(this.#language()) || has('en');
  }

  /**
   * Loads a language's strings, once. Internal: the startup sequence loads
   * the boot language and English before the first render.
   *
   * @param code - The language.
   * @returns A promise that settles once the strings have loaded.
   */
  ɵloadLanguage(code: string): Promise<void> {
    return this.#loadLanguage(code);
  }

  /**
   * Loads what the inputs resolve to, the language's strings and the
   * calendar's entry point, once the store's boot step has filled the launch
   * documents. Internal: the startup sequence awaits it before the first
   * render, so a cached calendar or language never flashes another first. A
   * load that fails leaves the boot language or `gc` in place.
   *
   * @returns A promise that settles once both loads have.
   */
  ɵready(): Promise<void> {
    return Promise.all([
      this.#loadLanguage(this.#wantedLanguage()).catch(() => undefined),
      this.#loadCalendar(this.#wantedCalendar()).catch(() => undefined),
    ]).then(() => undefined);
  }

  /**
   * The `Accept-Language` value of every request: the language, region and
   * digits, never the browser check's fallback. Internal: the HTTP layer
   * reads it on every call.
   *
   * @returns The locale string, such as `ar-SA-u-nu-latn`.
   */
  ɵacceptLanguage(): string {
    return this.#requestLocale();
  }

  /** Applies a change: the loads, then every member in one pass, then the boot record and the write. */
  async #apply(change: TmLocaleChange): Promise<void> {
    const tenantId = this.#tenantId();
    const settings = this.#settings();
    const inTenant = tenantId !== null && settings !== undefined;

    // --- 1. Load: nothing changes until the language's strings and the calendar arrive ---
    const primary = tmOfferedLanguage(this.#offered, settings?.languages[0]?.code) ?? this.#offered[0];
    const language = change.language === undefined ? undefined : (change.language ?? primary);
    const calendar = !inTenant || change.calendar === undefined ? undefined : (change.calendar ?? settings.calendars[0] ?? 'gc');
    await Promise.all([
      language === undefined ? undefined : this.#loadLanguage(language),
      calendar === undefined ? undefined : this.#loadCalendar(calendar),
    ]);

    // --- 2. Apply, in one pass: the members, then the document and Transloco ---
    const from = { language: this.#language(), calendar: this.#calendarCode() };
    if (language !== undefined) {
      this.#bootLang.set(language);
    }
    const queue: TmLocaleQueue = {};
    if (inTenant) {
      const user = this.#launch.docs.me()?.user;
      const overrides = { ...this.#overrides().get(tenantId) };
      for (const member of Object.keys(FIELDS) as TmLocaleMember[]) {
        const value = change[member];
        if (value !== undefined) {
          overrides[member] = { value, base: user?.[FIELDS[member]] as string | undefined };
          queue[member] = value;
        }
      }
      this.#overrides.update((all) => new Map(all).set(tenantId, overrides));
    }
    this.#applyDocument(this.#language(), this.dir());

    // --- 3. Save: the boot record keeps the language, the locale write the members ---
    if (language !== undefined) {
      this.#store.boot.patch({ lang: this.#language(), dir: this.dir() });
    }
    if (inTenant && Object.keys(queue).length > 0) {
      this.#write.queue(tenantId, queue);
      this.#writing.set(true);
    }
    if (from.language !== this.#language()) {
      this.#telemetry.event('tm.l10n.language-changed', { from: from.language, to: this.#language() });
    }
    if (from.calendar !== this.#calendarCode()) {
      this.#telemetry.event('tm.l10n.calendar-changed', { from: from.calendar, to: this.#calendarCode() });
    }
  }

  /** Points Transloco and `<html lang dir>` at a language. */
  #applyDocument(language: string, dir: 'ltr' | 'rtl'): void {
    if (this.#transloco.getActiveLang() !== language) {
      this.#transloco.setActiveLang(language);
    }
    if (this.#root.lang !== language) {
      this.#root.lang = language;
    }
    if (this.#root.dir !== dir) {
      this.#root.dir = dir;
    }
  }

  /** Loads a language's strings once; a failed load is tried again on the next call. */
  #loadLanguage(code: string): Promise<void> {
    if (this.#loadedLanguages().has(code)) {
      return Promise.resolve();
    }
    return this.#once(`language:${code}`, async () => {
      await firstValueFrom(this.#transloco.load(code));
      // Loading a language other than English loads English beside it, as its fallback.
      this.#loadedLanguages.update((loaded) => new Set(loaded).add(code).add('en'));
    });
  }

  /** Loads a calendar once; a failed load is tried again on the next call. */
  #loadCalendar(code: string): Promise<void> {
    if (this.#loadedCalendars().has(code)) {
      return Promise.resolve();
    }
    const load = this.#config.calendars?.find((calendar) => calendar.code === code)?.load ?? CALENDAR_LOADERS[code];
    if (load === undefined) {
      return Promise.reject(new Error(`No calendar has the code "${code}"; register it in the app configuration's calendars.`));
    }
    return this.#once(`calendar:${code}`, async () => {
      const calendar = await load();
      this.#loadedCalendars.update((loaded) => new Map(loaded).set(code, calendar));
    });
  }

  /** Runs a load once at a time per key, forgetting it when it fails so a later call tries again. */
  #once(key: string, load: () => Promise<void>): Promise<void> {
    let promise = this.#loads.get(key);
    if (promise === undefined) {
      promise = load().catch((error: unknown) => {
        this.#loads.delete(key);
        throw error;
      });
      this.#loads.set(key, promise);
    }
    return promise;
  }

  /** The definition of an offered language. */
  #definition(code: string): TmLanguageDefinition | undefined {
    return this.#config.languages.find((language) => language.code === code);
  }

  /** Refuses a switch: a development error, ignored in production. */
  #refuse(what: 'language' | 'calendar', code: string, offered: readonly string[]): void {
    // The build folds ngDevMode away, and the message with it.
    if (typeof ngDevMode !== 'undefined' && ngDevMode && this.#devMode()) {
      throw new Error(`TmLocalization: the ${what} "${code}" is not offered here; offer one of ${offered.join(', ')}.`);
    }
  }
}
