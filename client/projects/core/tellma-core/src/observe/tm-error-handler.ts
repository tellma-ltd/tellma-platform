// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type ErrorHandler, inject, Injectable } from '@angular/core';

import type { TmApiErrorKind } from '../api/tm-api-error';
import { TmTelemetry } from './tm-telemetry';

// The app's error handler (spec 0026 §4). Angular hands it every error a
// template, an effect or a lifecycle hook throws, and the browser's global
// listeners that bootstrapTellma installs hand it every uncaught error and
// unhandled rejection. It passes each to TmTelemetry.error, which logs it to
// the console, tells the two kinds apart by the event being dispatched (an
// `ErrorEvent` or a `PromiseRejectionEvent` while the global listeners run),
// keeps its record and queues its item. The classification, the filters,
// the redaction and the throttles live in the telemetry pipeline's lazy
// chunk, so this class costs the first frame almost nothing.

/** How an error reached the platform. */
export type TmErrorSource = 'api' | 'render' | 'global';

/**
 * One captured error as the tab keeps it, for the support dump: the last 50,
 * redacted as they would be sent.
 */
export interface TmErrorRecord {
  /** How the error arrived. */
  readonly source: TmErrorSource;
  /** The error's class, such as `TypeError` or `ChunkLoadError`. */
  readonly errorClass: string;
  /** The class and the first same-origin frame, such as `TypeError@chunk-K3J7Q2M9.js:1:2345`; for a request error, its kind and code, such as `TmApiError@server.internal`. */
  readonly fingerprint: string;
  /** The request error's kind, for a failed request. */
  readonly kind?: TmApiErrorKind;
  /** The request error's code, for a failed request. */
  readonly code?: string;
  /** The HTTP status, for a failed request that had one. */
  readonly status?: number;
  /** The request's trace id, for a failed request. */
  readonly traceId?: string;
  /** The routed page's template, such as `/:t/centers/:id`, or empty before the first page. */
  readonly routeTemplate: string;
  /** The stack, its frames reduced to file names; empty when the error had none. */
  readonly stack: string;
  /** The redacted message; absent for a request error, whose message can quote what the user typed. */
  readonly message?: string;
  /** When the error was captured, in epoch milliseconds. */
  readonly at: number;
  /** The trace ids of the three most recent requests, newest first. */
  readonly recentTraceIds: readonly string[];
}

/**
 * The error handler `provideTellma` installs: every uncaught error,
 * unhandled rejection and template error reaches {@link TmTelemetry.error},
 * which marks it `global` or `render`.
 *
 * @example
 * ```ts
 * // provideTellma provides it; a test app may provide it alone:
 * providers: [{ provide: ErrorHandler, useClass: TmErrorHandler }]
 * ```
 */
@Injectable()
export class TmErrorHandler implements ErrorHandler {
  readonly #telemetry = inject(TmTelemetry);

  /**
   * Reports an error to telemetry, which marks it `global` while the browser
   * dispatches an uncaught error or rejection, else `render`.
   *
   * @param error - What was thrown or rejected.
   */
  handleError(error: unknown): void {
    this.#telemetry.error(error);
  }
}
