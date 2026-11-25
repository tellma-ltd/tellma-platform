// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, Injector, untracked } from '@angular/core';
import {
  TmApi,
  type TmApiErrorKind,
  TmAppVersion,
  TmDeviceZone,
  type TmErrorRecord,
  type TmJson,
  TmLocalization,
  TmRequestLog,
  type TmRequestRecord,
  TmSession,
  TmTabBus,
  TmTelemetry,
  type TmTelemetryItem,
  TmUrls,
  ɵTM_UPDATES_LOAD,
  ɵTmClientMetadataStore,
  TmSupport,
  type ɵTmSupportSnapshot,
} from '@tellma/core';
import type { TmPageContextSnapshot } from '@tellma/core/page-snapshot';
import { TmTheme } from '@tellma/core-ui';

// The support dump (spec 0026 §10): one JSON document describing the tab at
// a moment, built from the snapshot the support facade took in the user's
// press. Everything but the device's storage estimate and the worker's
// version is read synchronously when the build starts, the contributed
// sections included; those two are asked together and given 200 ms, after
// which they read as unknown. The dump never holds cookies, the profile's
// name or e-mail, storage contents, launch documents, drafts, or request
// and response bodies: the contexts come from the page-snapshot entry
// point, the sections hold names and counts, and the request log holds
// none of them. Over 2 MiB, it sheds what it can in a fixed order.

/** The largest dump, in UTF-8 bytes of its compact JSON: 2 MiB. */
export const TM_SUPPORT_DUMP_MAX_BYTES = 2_097_152;

/** The largest contributed section, in UTF-8 bytes of its compact JSON: 64 KB. */
const SECTION_MAX_BYTES = 65_536;

/** How long the storage estimate and the worker's version may take, in milliseconds. */
const SLOW_READ_MS = 200;

/** How long the file's object URL outlives the click, in milliseconds: long enough for any browser to start the download. */
const REVOKE_MS = 40_000;

/** The page contexts of a dump. */
export interface TmSupportDumpContexts {
  /** The routed page's context, or `null` before the first page. */
  readonly current: TmPageContextSnapshot | null;
  /** The open context-bearing dialogs' contexts, bottom to top. */
  readonly overlays: readonly TmPageContextSnapshot[];
}

/** The tab described at one moment, for support: what a support request stores and the diagnostic file holds. */
export interface TmSupportDump {
  /** The document's format. */
  readonly format: 'tellma-support-dump';
  /** The format's version. */
  readonly formatVersion: 1;
  /** When the snapshot was taken, in ISO 8601 with the device's offset. */
  readonly createdAt: string;
  /** Whether the contexts carry page data and the page's URL its query. */
  readonly includesPageData: boolean;
  /** What the size cap dropped, and the contributed sections over their own cap, by name. */
  readonly truncated: readonly string[];
  /** The error the support dialog opened from. */
  readonly reported?: { readonly traceId: string; readonly kind: TmApiErrorKind; readonly code: string };
  /** The builds: the UI's, the platform's, the features', the server's last seen and the worker's. */
  readonly versions: {
    readonly ui: string;
    readonly platform: string;
    readonly features: readonly { readonly key: string; readonly version: string }[];
    readonly server: string | null;
    readonly worker: string | null;
  };
  /** The distribution. */
  readonly distribution: { readonly slug: string; readonly displayName: string };
  /** The session: the user's subject id, the routed tenant, the tab, the session's state and the connection. */
  readonly session: {
    readonly sub: string | null;
    readonly tenantId: number | null;
    readonly tabId: string;
    readonly state: string;
    readonly online: boolean;
  };
  /** The language, the regional formats, the calendar, the zones and the theme. */
  readonly locale: {
    readonly language: string;
    readonly region: string;
    readonly digits: 'Latin' | 'Native' | null;
    readonly locale: string;
    readonly dir: 'ltr' | 'rtl';
    readonly calendar: string;
    readonly timeZone: string;
    readonly deviceTimeZone: string;
    readonly zoneAuto: boolean;
    readonly theme: string;
    readonly effectiveTheme: 'light' | 'dark';
  };
  /** The browser and the device. */
  readonly environment: {
    readonly userAgent: string;
    readonly languages: readonly string[];
    readonly viewport: { readonly width: number; readonly height: number; readonly dpr: number };
    readonly standalone: boolean;
    readonly reducedMotion: boolean;
    readonly cookies: boolean;
    readonly storage: { readonly usage: number; readonly quota: number; readonly persisted: boolean } | null;
  };
  /** The page: its URL (its query only with page data), its route template and the history's length. */
  readonly page: { readonly url: string; readonly routeTemplate: string; readonly historyLength: number };
  /** The page contexts. */
  readonly contexts: TmSupportDumpContexts;
  /** The last requests, newest first: at most 100. */
  readonly requests: readonly TmRequestRecord[];
  /** The last captured errors, oldest first: at most 50. */
  readonly errors: readonly TmErrorRecord[];
  /** The last telemetry items, sent or not: at most 100. */
  readonly telemetry: readonly TmTelemetryItem[];
  /** The contributed sections, by name. */
  readonly sections: Readonly<Record<string, TmJson>>;
}

/** The options of {@link tmBuildSupportDump}. */
export interface TmSupportDumpOptions {
  /** Whether the dump carries page data: the contexts' data and the URL's query. */
  readonly includeData: boolean;
  /** The injector the dump reads the app's services from; by default, the injection context's. */
  readonly injector?: Injector;
}

/**
 * The UTF-8 length of a value's compact JSON.
 *
 * @param value - The value.
 * @returns The length in bytes.
 */
function bytes(value: unknown): number {
  return new TextEncoder().encode(JSON.stringify(value) ?? 'null').length;
}

/**
 * A promise's value, or a fallback once a deadline passes or the promise rejects.
 *
 * @param promise - The promise.
 * @param ms - The deadline, in milliseconds.
 * @param fallback - The value after the deadline or on a rejection.
 * @returns The value or the fallback.
 */
function within<T>(promise: Promise<T>, ms: number, fallback: T): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const late = new Promise<T>((resolve) => (timer = setTimeout(() => resolve(fallback), ms)));
  return Promise.race([promise.catch(() => fallback), late]).finally(() => clearTimeout(timer));
}

/**
 * An instant in ISO 8601 with the device's offset, such as `2026-10-20T14:03:12.512+03:00`.
 *
 * @param at - The instant, in epoch milliseconds.
 * @returns The text.
 */
function localIso(at: number): string {
  const offset = -new Date(at).getTimezoneOffset();
  const local = new Date(at + offset * 60_000).toISOString().slice(0, -1);
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${local}${offset < 0 ? '-' : '+'}${pad(Math.floor(Math.abs(offset) / 60))}:${pad(Math.abs(offset) % 60)}`;
}

/** An identifier, which an error's name must be to be shown. */
const IDENTIFIER = /^[A-Za-z_$][A-Za-z0-9_$]{0,63}$/;

/**
 * The class of what a section's reader threw: the error's name, else the
 * type of a value that is not an object. Never its message, which may hold
 * the user's data.
 *
 * @param error - What was thrown.
 * @returns The class, such as `TypeError`.
 */
function errorClass(error: unknown): string {
  const name: unknown = typeof error === 'object' && error !== null ? (error as { readonly name?: unknown }).name : undefined;
  if (typeof name === 'string' && IDENTIFIER.test(name)) {
    return name;
  }
  return error === null ? 'null' : typeof error;
}

/**
 * Reads a contributed section into plain JSON: `{ "error": "<errorClass>" }`
 * when its reader throws or its value is not JSON, `{ "truncated": true }`
 * over 64 KB.
 *
 * @param read - The section's reader.
 * @returns The section, and whether it was truncated.
 */
function readSection(read: () => TmJson): { readonly value: TmJson; readonly truncated: boolean } {
  let text: string;
  try {
    text = JSON.stringify(untracked(read)) ?? 'null';
  } catch (error) {
    return { value: { error: errorClass(error) }, truncated: false };
  }
  if (new TextEncoder().encode(text).length > SECTION_MAX_BYTES) {
    return { value: { truncated: true }, truncated: true };
  }
  return { value: JSON.parse(text) as TmJson, truncated: false };
}

/**
 * The page's URL without its query or fragment.
 *
 * @param url - The router's URL.
 * @returns The path alone.
 */
function pathOf(url: string): string {
  return url.split(/[?#]/, 1)[0];
}

/**
 * Builds the support dump from the snapshot taken in the user's press.
 *
 * Over {@link TM_SUPPORT_DUMP_MAX_BYTES}, the builder drops these in order,
 * stopping once the dump fits, and names each step that dropped something
 * in `truncated`: every context's visible rows (`visibleRows`), the
 * telemetry (`telemetry`), the rest of the page data (`pageData`), and the
 * contributed sections (`sections`).
 *
 * @param snapshot - The snapshot the support facade took.
 * @param options - Whether the dump carries page data, and the injector.
 * @returns The dump.
 *
 * @example
 * ```ts
 * const dump = await tmBuildSupportDump(snapshot, { includeData: false });
 * tmDownloadSupportDump(dump);
 * ```
 */
export async function tmBuildSupportDump(
  snapshot: ɵTmSupportSnapshot<TmPageContextSnapshot>,
  options: TmSupportDumpOptions,
): Promise<TmSupportDump> {
  const injector = options.injector ?? inject(Injector);
  const window = injector.get(DOCUMENT).defaultView;
  const navigator = window?.navigator;

  // --- The two slow reads, started first and given 200 ms together ---
  const storage = navigator?.storage;
  const slow = within<readonly [TmSupportDump['environment']['storage'], string | null]>(
    Promise.all([
      storage?.estimate
        ? Promise.all([storage.estimate(), storage.persisted?.() ?? Promise.resolve(false)]).then(
            ([estimate, persisted]) => ({ usage: estimate.usage ?? 0, quota: estimate.quota ?? 0, persisted }),
            () => null,
          )
        : Promise.resolve(null),
      injector
        .get(ɵTM_UPDATES_LOAD)()
        .then(
          (loaded) => (loaded === null ? null : untracked(injector.get(loaded.TmUpdates).version)),
          () => null,
        ),
    ]),
    SLOW_READ_MS,
    [null, null],
  );

  // --- Everything else, read now ---
  const read = untracked(() => {
    const version = injector.get(TmAppVersion);
    const l10n = injector.get(TmLocalization);
    const theme = injector.get(TmTheme);
    const telemetry = injector.get(TmTelemetry);
    const distribution = injector.get(ɵTmClientMetadataStore).peek()?.distribution;
    const media = (query: string): boolean => window?.matchMedia?.(query).matches ?? false;
    const truncated: string[] = [];
    const sections: Record<string, TmJson> = {};
    for (const section of injector.get(TmSupport).ɵsections()) {
      const { value, truncated: over } = readSection(section.read);
      sections[section.name] = value;
      if (over) {
        truncated.push(section.name);
      }
    }
    return {
      truncated,
      versions: { ui: version.ui, platform: version.platform, features: version.features, server: version.server() },
      distribution: { slug: distribution?.slug ?? '', displayName: distribution?.displayName ?? '' },
      session: {
        sub: injector.get(TmSession).profile()?.sub ?? null,
        tenantId: injector.get(TmUrls).tenantId(),
        tabId: injector.get(TmTabBus).tabId,
        state: injector.get(TmSession).state(),
        online: injector.get(TmApi).online(),
      },
      locale: {
        language: l10n.language(),
        region: l10n.region(),
        digits: l10n.digits(),
        locale: l10n.locale(),
        dir: l10n.dir(),
        calendar: l10n.calendarCode(),
        timeZone: l10n.timeZone(),
        deviceTimeZone: l10n.deviceTimeZone(),
        zoneAuto: injector.get(TmDeviceZone).auto(),
        theme: theme.setting(),
        effectiveTheme: theme.effective(),
      },
      environment: {
        userAgent: navigator?.userAgent ?? '',
        languages: [...(navigator?.languages ?? [])],
        viewport: { width: window?.innerWidth ?? 0, height: window?.innerHeight ?? 0, dpr: window?.devicePixelRatio ?? 1 },
        standalone: media('(display-mode: standalone)') || (navigator as { readonly standalone?: boolean } | undefined)?.standalone === true,
        reducedMotion: media('(prefers-reduced-motion: reduce)'),
        cookies: navigator?.cookieEnabled ?? false,
      },
      requests: injector.get(TmRequestLog).recent(),
      errors: telemetry.ɵerrorRecords(),
      telemetry: telemetry.recent(),
      sections,
    };
  });
  const [estimate, worker] = await slow;

  // --- The dump, as asked ---
  const shown = (includeData: boolean): Pick<TmSupportDump, 'includesPageData' | 'page' | 'contexts'> => ({
    includesPageData: includeData,
    page: {
      url: includeData ? snapshot.url : pathOf(snapshot.url),
      routeTemplate: snapshot.routeTemplate,
      historyLength: snapshot.historyLength,
    },
    contexts: includeData ? snapshot.withData : snapshot.withoutData,
  });
  let dump: TmSupportDump = {
    format: 'tellma-support-dump',
    formatVersion: 1,
    createdAt: localIso(snapshot.at),
    includesPageData: options.includeData,
    truncated: read.truncated,
    ...(snapshot.reported === undefined ? {} : { reported: snapshot.reported }),
    versions: { ...read.versions, worker },
    distribution: read.distribution,
    session: read.session,
    locale: read.locale,
    environment: { ...read.environment, storage: estimate },
    page: shown(options.includeData).page,
    contexts: shown(options.includeData).contexts,
    requests: read.requests,
    errors: read.errors,
    telemetry: read.telemetry,
    sections: read.sections,
  };

  // --- The size cap: each step only when the dump is still over, and only when it drops something ---
  const steps: readonly [name: string, drop: (over: TmSupportDump) => TmSupportDump | null][] = [
    ['visibleRows', dropVisibleRows],
    ['telemetry', (over) => (over.telemetry.length === 0 ? null : { ...over, telemetry: [] })],
    ['pageData', (over) => (over.includesPageData ? { ...over, ...shown(false) } : null)],
    ['sections', (over) => (Object.keys(over.sections).length === 0 ? null : { ...over, sections: {} })],
  ];
  for (const [name, drop] of steps) {
    if (bytes(dump) <= TM_SUPPORT_DUMP_MAX_BYTES) {
      break;
    }
    const smaller = drop(dump);
    if (smaller !== null) {
      dump = { ...smaller, truncated: [...smaller.truncated, name] };
    }
  }
  return dump;
}

/**
 * The dump with every context's visible rows left out.
 *
 * @param dump - The dump.
 * @returns The smaller dump, or `null` when no context carried rows.
 */
function dropVisibleRows(dump: TmSupportDump): TmSupportDump | null {
  let dropped = false;
  const strip = (context: TmPageContextSnapshot): TmPageContextSnapshot => {
    if (context.visible?.rows === undefined) {
      return context;
    }
    dropped = true;
    const { columns, rowCount, total } = context.visible;
    return { ...context, visible: { columns, rowCount, total } };
  };
  const contexts: TmSupportDumpContexts = {
    current: dump.contexts.current === null ? null : strip(dump.contexts.current),
    overlays: dump.contexts.overlays.map(strip),
  };
  return dropped ? { ...dump, contexts } : null;
}

/**
 * The diagnostic file's name: the distribution's slug, then the dump's
 * local time with Latin digits.
 *
 * @param dump - The dump.
 * @returns The name, such as `acme-diagnostics-20261020-140312.json`.
 */
export function tmSupportDumpFileName(dump: Pick<TmSupportDump, 'createdAt' | 'distribution'>): string {
  // createdAt is the device's local time already: its date and time digits, separators removed.
  const date = dump.createdAt.slice(0, 10).replaceAll('-', '');
  const time = dump.createdAt.slice(11, 19).replaceAll(':', '');
  return `${dump.distribution.slug || 'tellma'}-diagnostics-${date}-${time}.json`;
}

/**
 * Downloads a dump as a JSON file, through a temporary `<a download>` and
 * an object URL revoked once the download has started.
 *
 * @param dump - The dump.
 * @param injector - The injector the download reads the document and telemetry from; by default, the injection context's.
 *
 * @example
 * ```ts
 * tmDownloadSupportDump(await tmBuildSupportDump(snapshot, { includeData: false }));
 * ```
 */
export function tmDownloadSupportDump(dump: TmSupportDump, injector: Injector = inject(Injector)): void {
  const document = injector.get(DOCUMENT);
  const url = URL.createObjectURL(new Blob([JSON.stringify(dump, null, 2)], { type: 'application/json' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = tmSupportDumpFileName(dump);
  link.hidden = true;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), REVOKE_MS);
  injector.get(TmTelemetry).event('tm.support.dump-downloaded', { pageData: dump.includesPageData });
}
