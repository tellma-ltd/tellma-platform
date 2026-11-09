// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, type Signal } from '@angular/core';

// The request-log seam (spec 0026 §8.1). TmApi calls `begin` when it sends a
// request and the handle's `end` when the request settles, so a slow, failed
// or hanging call can always be quoted by its trace id. Until the log is
// provided, the root default keeps nothing.

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
   * Records how the request settled.
   *
   * @param result - The outcome, status, code and server build.
   */
  end(result: TmRequestEnd): void;
}

/**
 * The log of the tab's most recent requests, each with its trace id. Only the
 * HTTP layer begins entries; everything else reads them.
 */
@Injectable({ providedIn: 'root', useFactory: () => new TmQuietRequestLog() })
export abstract class TmRequestLog {
  /** The most recent requests, newest first. */
  abstract readonly recent: Signal<readonly TmRequestRecord[]>;

  /**
   * The most recent requests as plain text for a support message.
   *
   * @param count - How many requests to include.
   */
  abstract diagnosticsText(count?: number): string;

  /** Copies {@link TmRequestLog.diagnosticsText} to the clipboard, inside the user's gesture. */
  abstract copy(): void;

  /**
   * Records a request as pending, and returns the handle that ends it.
   *
   * @param start - What the HTTP layer knows when it sends the request.
   */
  abstract begin(start: TmRequestStart): TmRequestHandle;
}

/** The handle the quiet log hands out: ending it records nothing. */
const QUIET_HANDLE: TmRequestHandle = Object.freeze({
  end(): void {
    // Quiet by design: no log is provided.
  },
});

/** The root default until the log is provided: it keeps nothing. */
class TmQuietRequestLog extends TmRequestLog {
  /** Always empty: nothing is logged. */
  override readonly recent: Signal<readonly TmRequestRecord[]> = signal<readonly TmRequestRecord[]>([]).asReadonly();

  /** Empty: nothing is logged. */
  override diagnosticsText(): string {
    return '';
  }

  /** Has nothing to copy. */
  override copy(): void {
    // Quiet by design: no log is provided.
  }

  /** Hands out a handle that records nothing. */
  override begin(): TmRequestHandle {
    return QUIET_HANDLE;
  }
}
