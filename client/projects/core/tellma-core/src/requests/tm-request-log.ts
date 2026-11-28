// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, inject, Injectable, Injector, signal, type Signal, untracked } from '@angular/core';
import { TmToasts } from '@tellma/core-ui/toast';

import { TM_NOW } from '../app/tm-now';
import { TM_TOAST_FEEDBACK_MS } from '../attention/tm-toast-durations';
import { TmLocalization } from '../l10n/tm-localization';
import { TmShortcuts } from '../nav/tm-shortcuts';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmAppVersion } from './tm-app-version';
import { tmDiagnosticsInput, tmDiagnosticsText } from './tm-diagnostics';

// The request log (spec 0026 §8.1). TmApi calls `begin` when it sends a
// request and the handle's `end` when the request settles, so a slow, failed
// or hanging call can always be quoted by its trace id. The log keeps the
// last 100 calls, newest first; a pending call turns slow at 3 s on a timer
// of its own, which `end` clears. Each end hands the response's
// `Tellma-Build` to TmAppVersion. Copy diagnostics (§8.3) builds its text
// and writes it to the clipboard synchronously, inside the key or click
// handler that asked for it, as Safari and Firefox require; the outcome
// toasts once (spec 0028 §8.1). The app's startup registers its global
// shortcut before the first navigation.

/** Where a logged request stands. */
export type TmRequestOutcome = 'pending' | 'ok' | 'cancelled' | 'error';

/** One logged request. */
export interface TmRequestRecord {
  /** The trace id the client generated for the call. */
  readonly traceId: string;
  /** The route as the HTTP layer received it, such as `centers/query`. */
  readonly route: string;
  /** The tenant the call went to, or `null` for a tenantless route. */
  readonly tenantId: number | null;
  /** The HTTP method. */
  readonly method: 'POST' | 'GET';
  /** Whether the call was a background one, which shows no dialog. */
  readonly background: boolean;
  /** When the call was sent, in epoch milliseconds. */
  readonly startedAt: number;
  /** How long the call took, or `null` while it is pending. */
  readonly durationMs: number | null;
  /** Where the call stands. */
  readonly outcome: TmRequestOutcome;
  /** The HTTP status, once there is one. */
  readonly status?: number;
  /** The error's code, when the call failed. */
  readonly code?: string;
  /** Whether the call reached 3 s, pending or not. */
  readonly slow: boolean;
}

/** What the HTTP layer knows when it sends a request. */
export type TmRequestStart = Pick<TmRequestRecord, 'traceId' | 'route' | 'tenantId' | 'method' | 'background'>;

/** How a request settled, as its handle's `end` receives it. */
export interface TmRequestEnd {
  /** How the call ended. */
  readonly outcome: Exclude<TmRequestOutcome, 'pending'>;
  /** The HTTP status, when a response arrived. */
  readonly status?: number;
  /** The error's code, when the call failed. */
  readonly code?: string;
  /** The server's build from the response's `Tellma-Build` header. */
  readonly serverBuild?: string;
}

/** The open entry of one request in the log. */
export interface TmRequestHandle {
  /**
   * Records how the request settled. A second call does nothing.
   *
   * @param result - The outcome, status, code and server build.
   */
  end(result: TmRequestEnd): void;
}

/**
 * What triggered a copy of the diagnostics: the keyboard shortcut, a Copy
 * diagnostics button such as the Diagnostics panel's, or an error dialog's
 * action.
 */
export type TmDiagnosticsTrigger = 'shortcut' | 'button' | 'dialog';

/**
 * The log of the tab's most recent requests, each with its trace id. Only the
 * HTTP layer begins entries; everything else reads them.
 *
 * Copy diagnostics puts a plain-text block for support on the clipboard: the
 * versions, the session, the page and the last requests, in English with
 * Latin digits whatever the UI language. `Mod+Alt+Shift+D` copies it from
 * anywhere in the app.
 *
 * @example
 * ```ts
 * // In a click handler, so the clipboard write runs inside the user's gesture.
 * inject(TmRequestLog).copy('button');
 * ```
 */
@Injectable({ providedIn: 'root', useFactory: () => new TmAppRequestLog() })
export abstract class TmRequestLog {
  /** The most recent requests, newest first: at most 100. */
  abstract readonly recent: Signal<readonly TmRequestRecord[]>;

  /**
   * The diagnostics as plain text for support: the versions, the session, the
   * page and the most recent requests, built synchronously.
   *
   * @param count - How many requests to include; 10 by default.
   * @returns The text.
   */
  abstract diagnosticsText(count?: number): string;

  /**
   * Copies {@link TmRequestLog.diagnosticsText} to the clipboard and toasts
   * whether it was copied. Call it inside the user's gesture: the clipboard
   * write runs synchronously, as some browsers require.
   *
   * @param trigger - What triggered the copy; `button` by default.
   */
  abstract copy(trigger?: TmDiagnosticsTrigger): void;

  /**
   * Records a request as pending, and returns the handle that ends it.
   *
   * @param start - What the HTTP layer knows when it sends the request.
   * @returns The handle.
   */
  abstract begin(start: TmRequestStart): TmRequestHandle;
}

/** The most requests the log keeps. */
const RING = 100;

/** How long a request runs before it is slow, in milliseconds. */
const SLOW_MS = 3000;

/** The key the copy's toasts share, so a second copy replaces the first's toast. */
const TOAST_KEY = 'tm-diagnostics';

/** The root {@link TmRequestLog}: a ring of the last 100 requests. Internal. */
export class TmAppRequestLog extends TmRequestLog {
  readonly #now = inject(TM_NOW);
  readonly #version = inject(TmAppVersion);
  readonly #injector = inject(Injector);

  /** The ring, newest first. */
  readonly #ring = signal<readonly TmRequestRecord[]>([]);

  /** {@inheritDoc TmRequestLog.recent} */
  override readonly recent: Signal<readonly TmRequestRecord[]> = this.#ring.asReadonly();

  /** The slow timers of the pending requests, which the app's end clears. */
  readonly #timers = new Set<ReturnType<typeof setTimeout>>();

  /** Creates the log, whose timers end with the app. */
  constructor() {
    super();
    inject(DestroyRef).onDestroy(() => this.#timers.forEach(clearTimeout));
  }

  /** {@inheritDoc TmRequestLog.diagnosticsText} */
  override diagnosticsText(count = 10): string {
    return tmDiagnosticsText(tmDiagnosticsInput(this.#injector, untracked(this.#ring)), count);
  }

  /** {@inheritDoc TmRequestLog.copy} */
  override copy(trigger: TmDiagnosticsTrigger = 'button'): void {
    // --- The write, synchronously inside the gesture ---
    let written: Promise<void>;
    try {
      written = this.#injector.get(DOCUMENT).defaultView!.navigator.clipboard.writeText(this.diagnosticsText());
    } catch (error) {
      // No clipboard here, such as on an insecure origin, or the text failed.
      written = Promise.reject(error);
    }
    // --- The outcome, toasted once ---
    const toasts = this.#injector.get(TmToasts);
    const title = (key: 'copied' | 'copyFailed'): string => untracked(this.#injector.get(TmLocalization).ɵtext(`tm.diagnostics.${key}`));
    written.then(
      () => {
        toasts.show({ tone: 'success', title: title('copied'), duration: TM_TOAST_FEEDBACK_MS, key: TOAST_KEY });
        this.#injector.get(TmTelemetry).event('tm.support.diagnostics-copied', { via: trigger });
      },
      () => toasts.show({ tone: 'error', title: title('copyFailed'), duration: 'sticky', key: TOAST_KEY }),
    );
  }

  /** {@inheritDoc TmRequestLog.begin} */
  override begin({ traceId, route, tenantId, method, background }: TmRequestStart): TmRequestHandle {
    let record: TmRequestRecord = { traceId, route, tenantId, method, background, startedAt: this.#now(), durationMs: null, outcome: 'pending', slow: false };
    // Each change replaces the request's record in the ring, while it is still there.
    const put = (next: TmRequestRecord): void => {
      const previous = record;
      record = next;
      this.#ring.update((records) => records.map((held) => (held === previous ? next : held)));
    };
    this.#ring.update((records) => [record, ...records.slice(0, RING - 1)]);
    // --- Slow at 3 s, while it is pending ---
    const timer = setTimeout(() => {
      this.#timers.delete(timer);
      put({ ...record, slow: true });
    }, SLOW_MS);
    this.#timers.add(timer);
    let ended = false;
    return {
      end: ({ outcome, status, code, serverBuild }) => {
        if (ended) {
          return;
        }
        ended = true;
        clearTimeout(timer);
        this.#timers.delete(timer);
        const durationMs = Math.max(0, this.#now() - record.startedAt);
        put({
          ...record,
          durationMs,
          outcome,
          ...(status === undefined ? {} : { status }),
          ...(code === undefined ? {} : { code }),
          slow: record.slow || durationMs >= SLOW_MS,
        });
        if (serverBuild !== undefined) {
          this.#version.ɵserver(serverBuild);
        }
      },
    };
  }

  /**
   * Registers the global shortcut that copies the diagnostics, in fields and
   * over modals (spec 0027 Appendix A). Internal: the app's startup calls it
   * before the first navigation.
   */
  ɵstart(): void {
    this.#injector.get(TmShortcuts).register(
      {
        id: 'tm.support.copy-diagnostics',
        keys: 'Mod+Alt+Shift+D',
        labelKey: 'tm.diagnostics.copy',
        scope: 'global',
        inFields: true,
        overModals: true,
        run: () => this.copy('shortcut'),
      },
      this.#injector.get(DestroyRef),
    );
  }
}
