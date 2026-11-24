// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, type Injector, untracked } from '@angular/core';
import { tmStripBidiControls } from '@tellma/core-ui/l10n';
import { TmToasts } from '@tellma/core-ui/toast';

import { TmApi } from '../api/tm-api';
import { TM_NOW } from '../app/tm-now';
import { TM_TOAST_FEEDBACK_MS } from '../attention/tm-toast-durations';
import type { TmPageContext } from '../context/tm-page-context';
import { TmPageContexts } from '../context/tm-page-contexts';
import { TmLocalization } from '../l10n/tm-localization';
import { TmClientMetadataStore } from '../metadata/tm-client-metadata';
import { TmShortcuts } from '../nav/tm-shortcuts';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmSession } from '../session/tm-session';
import { TmTabBus } from '../state/bus/tm-tab-bus';
import { TmLeader } from '../state/leader/tm-leader';
import { TmUrls } from '../urls/tm-urls';
import { TmAppVersion } from './tm-app-version';
import { type TmDiagnostics, type TmDiagnosticsTrigger, type TmRequestRecord, TmRequestLog } from './tm-request-log';

// Copy diagnostics (spec 0026 §8.3): a lazy chunk of the primary, which the
// app's startup loads at idle after the first render. It builds the
// plain-text block support reads (the versions, the session, the page and
// the last requests), registers the global shortcut that copies it
// (`Mod+Alt+Shift+D`, which runs in fields and over modals, and matches the
// physical key on any layout) and toasts the outcome once (spec 0028 §8.1).
// The text is English with Latin digits in every UI language, and holds no
// bidi control character (spec 0024 §7.9): every number is formatted here,
// never through the locale. Times are the device's local time, with the
// offset in the header.

/** The diagnostics' English strings, under `tm.diagnostics`. */
const STRINGS_EN = {
  /** The shortcut's row in the keyboard shortcuts dialog. */
  copy: 'Copy diagnostics',
  /** The toast after a copy. */
  copied: 'Diagnostics copied.',
  /** The toast after a copy that failed. */
  copyFailed: "Couldn't copy. Open About, then Diagnostics, to see the details.",
} as const;

/** The key the outcome toasts share, so a second copy replaces the first's toast. */
const TOAST_KEY = 'tm-diagnostics';

/** Everything the diagnostics text shows, read at one moment. */
export interface TmDiagnosticsInput {
  /** The time of the reading, in epoch milliseconds. */
  readonly now: number;
  /**
   * The device's offset from UTC at an instant, in minutes east of UTC.
   *
   * @param at - The instant, in epoch milliseconds.
   * @returns The offset, such as 180 for `+03:00`.
   */
  readonly offset: (at: number) => number;
  /** The distribution's name, such as `Acme`. */
  readonly distribution: string;
  /** The UI version. */
  readonly ui: string;
  /** The last server build seen, or `null`. */
  readonly server: string | null;
  /** The platform's version. */
  readonly platform: string;
  /** The features' keys and versions. */
  readonly features: readonly { readonly key: string; readonly version: string }[];
  /** The routed tenant, or `null` outside a tenant. */
  readonly tenantId: number | null;
  /** The tab's id. */
  readonly tabId: string;
  /** The formatting locale, such as `ar-SA`. */
  readonly locale: string;
  /** The calendar's code, such as `uq`. */
  readonly calendar: string;
  /** Whether the server is reachable. */
  readonly online: boolean;
  /** The session's state, such as `active`. */
  readonly session: string;
  /** Whether this tab is the tenant's leader. */
  readonly leader: boolean;
  /** The realtime connection's state, such as `connected`. */
  readonly hub: string;
  /** The routed page, or `null` before the first. */
  readonly page: TmDiagnosticsPage | null;
  /** The open context-bearing dialogs, bottom to top. */
  readonly dialogs: readonly TmDiagnosticsPage[];
  /** The most recent requests, newest first. */
  readonly requests: readonly TmRequestRecord[];
}

/** A page or a dialog, as the diagnostics text shows it. */
export interface TmDiagnosticsPage {
  /** Its route template, such as `/:t/centers/:id`. */
  readonly routeTemplate: string;
  /** Its kind, such as `details`. */
  readonly kind: string;
  /** Its data state. */
  readonly status: string;
  /** How many errors it shows. */
  readonly errors: number;
}

/**
 * Builds the diagnostics text.
 *
 * @param input - What the text shows.
 * @param count - How many requests to include, newest first.
 * @returns The text: plain English, Latin digits, no bidi control character.
 */
export function tmDiagnosticsText(input: TmDiagnosticsInput, count: number): string {
  const { now, offset } = input;
  // The local wall time of an instant, as UTC fields shifted by the offset.
  const local = (at: number): Date => new Date(at + offset(at) * 60_000);
  const clock = (at: number): string => {
    const date = local(at);
    return `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`;
  };
  const today = local(now);
  const minutes = offset(now);
  const zone = `${minutes < 0 ? '-' : '+'}${pad(Math.floor(Math.abs(minutes) / 60))}:${pad(Math.abs(minutes) % 60)}`;

  // --- The header: who, when, which builds, where ---
  const lines = [
    `${input.distribution} diagnostics · ${today.getUTCFullYear()}-${pad(today.getUTCMonth() + 1)}-${pad(today.getUTCDate())} ${clock(now)} ${zone}`,
    `UI ${input.ui || 'unknown'} · server ${input.server ?? 'unknown'} · platform ${input.platform || 'unknown'}`,
  ];
  if (input.features.length > 0) {
    lines.push(`Features ${input.features.map((feature) => `${feature.key} ${feature.version}`).join(', ')}`);
  }
  lines.push(
    [
      input.tenantId === null ? 'No tenant' : `Tenant ${input.tenantId}`,
      `tab ${input.tabId}`,
      input.locale,
      input.calendar,
      input.online ? 'online' : 'offline',
      `${input.leader ? 'leader' : 'follower'}, hub ${input.hub}`,
      ...(input.session === 'active' ? [] : [`session ${input.session}`]),
    ].join(' · '),
  );

  // --- The page and the dialogs above it ---
  const page = (label: string, shown: TmDiagnosticsPage): string =>
    `${label} ${shown.routeTemplate} (${shown.kind}) · status ${shown.status}` +
    (shown.errors === 0 ? '' : ` · ${shown.errors} ${shown.errors === 1 ? 'error' : 'errors'} on screen`);
  lines.push(input.page === null ? 'Page none' : page('Page', input.page));
  for (const dialog of input.dialogs) {
    lines.push(page('Dialog', dialog));
  }

  // --- The requests, newest first: start, duration, flag, call, trace id, status and code ---
  const requests = input.requests.slice(0, Math.max(0, count));
  if (requests.length === 0) {
    lines.push('No requests');
  }
  for (const request of requests) {
    const duration =
      request.durationMs === null
        ? `pending ${seconds(now - request.startedAt, 1)}`
        : seconds(request.durationMs, 2);
    const flag = request.outcome === 'error' ? 'error' : request.outcome === 'cancelled' ? 'cancel' : request.slow ? 'slow' : '';
    const answer = [request.status, request.code].filter((part) => part !== undefined).join(' ');
    lines.push(
      `${clock(request.startedAt)}  ${duration.padEnd(15)}${flag.padEnd(7)}${`${request.method} ${request.route}`.padEnd(24)} ${request.traceId}` +
        (answer === '' ? '' : `  ${answer}`),
    );
  }
  return tmStripBidiControls(lines.join('\n'));
}

/**
 * Two digits, zero-padded.
 *
 * @param value - A number from 0 to 99.
 * @returns The digits.
 */
function pad(value: number): string {
  return String(value).padStart(2, '0');
}

/**
 * A duration in seconds, with Latin digits and a fixed number of decimals.
 *
 * @param ms - The duration in milliseconds.
 * @param decimals - How many decimals.
 * @returns The duration, such as `4.81 s`.
 */
function seconds(ms: number, decimals: number): string {
  return `${(Math.max(0, ms) / 1000).toFixed(decimals)} s`;
}

/**
 * Reads what the diagnostics text shows from the app's services, untracked.
 *
 * @param injector - The root injector.
 * @returns The input.
 */
export function tmDiagnosticsInput(injector: Injector): TmDiagnosticsInput {
  return untracked(() => {
    const version = injector.get(TmAppVersion);
    const l10n = injector.get(TmLocalization);
    const leader = injector.get(TmLeader);
    const contexts = injector.get(TmPageContexts);
    const distribution = injector.get(TmClientMetadataStore).peek()?.distribution;
    const page = (context: TmPageContext): TmDiagnosticsPage => ({
      routeTemplate: context.routeTemplate,
      kind: context.kind,
      status: context.status(),
      errors: context.errors().length,
    });
    const current = contexts.current();
    return {
      now: injector.get(TM_NOW)(),
      offset: (at) => -new Date(at).getTimezoneOffset(),
      distribution: distribution?.displayName || distribution?.slug || 'Tellma',
      ui: version.ui,
      server: version.server(),
      platform: version.platform,
      features: version.features,
      tenantId: injector.get(TmUrls).tenantId(),
      tabId: injector.get(TmTabBus).tabId,
      locale: l10n.locale(),
      calendar: l10n.calendarCode(),
      online: injector.get(TmApi).online(),
      session: injector.get(TmSession).state(),
      leader: leader.isLeader(),
      hub: leader.hubStatus().state,
      page: current === null ? null : page(current),
      dialogs: contexts.overlays().map(page),
      requests: injector.get(TmRequestLog).recent(),
    };
  });
}

/**
 * Starts the diagnostics: registers their strings and the global shortcut
 * that copies them. Internal: the request log loads this chunk and calls it once.
 *
 * @param injector - The root injector.
 * @returns The diagnostics.
 */
export function tmStartDiagnostics(injector: Injector): TmDiagnostics {
  const l10n = injector.get(TmLocalization);
  l10n.ɵregisterStrings('diagnostics', STRINGS_EN);
  const text = (key: keyof typeof STRINGS_EN): string => untracked(l10n.ɵtext(`tm.diagnostics.${key}`));
  const diagnostics: TmDiagnostics = {
    text: (count) => tmDiagnosticsText(tmDiagnosticsInput(injector), count),
    copy: (trigger: TmDiagnosticsTrigger) => {
      // --- The write, synchronously inside the gesture ---
      let written: Promise<void>;
      try {
        written = injector.get(DOCUMENT).defaultView!.navigator.clipboard.writeText(diagnostics.text(10));
      } catch (error) {
        // No clipboard here, such as on an insecure origin.
        written = Promise.reject(error);
      }
      // --- The outcome, toasted once ---
      const toasts = injector.get(TmToasts);
      written.then(
        () => {
          toasts.show({ tone: 'success', title: text('copied'), duration: TM_TOAST_FEEDBACK_MS, key: TOAST_KEY });
          injector.get(TmTelemetry).event('tm.support.diagnostics-copied', { via: trigger });
        },
        () => toasts.show({ tone: 'error', title: text('copyFailed'), duration: 'sticky', key: TOAST_KEY }),
      );
    },
  };
  // --- The global shortcut, in fields and over modals (spec 0027 Appendix A) ---
  injector.get(TmShortcuts).register(
    {
      id: 'tm.support.copy-diagnostics',
      keys: 'Mod+Alt+Shift+D',
      labelKey: 'tm.diagnostics.copy',
      scope: 'global',
      inFields: true,
      overModals: true,
      run: () => diagnostics.copy('shortcut'),
    },
    injector.get(DestroyRef),
  );
  return diagnostics;
}
