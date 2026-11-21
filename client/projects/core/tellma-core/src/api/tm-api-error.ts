// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJsonObject } from '../json/tm-json';
import type { TmStepUpChallenge } from '../session/tm-session';

// The one error every TmApi call rejects with (spec 0024 §10.1, §10.6). The
// HTTP layer turns each status and problem code into a kind, so no other code
// in the app reads an HTTP status: pages render an error view from the kind,
// and the error presenter shows the shared dialogs.

/**
 * What went wrong with a call, as the platform presents it:
 *
 * - `cancelled` — the caller aborted the call, or the session left this tab; nothing is shown;
 * - `offline` — the call never reached the server (`network`, `not-sent`) or its outcome is
 *   unknown (`outcome-unknown`);
 * - `unauthenticated` — the session ended; `step-up` — the call needs a stronger sign-in;
 * - `forbidden`, `not-found`, `tenant-not-found`, `tenant-unavailable`, `read-only` — access and
 *   tenant state;
 * - `validation`, `query-invalid`, `bad-request`, `conflict`, `confirmation`, `limit` — the
 *   request was refused as sent;
 * - `client-outdated` — this tab or the server runs an older build (`client-outdated`,
 *   `server-outdated`);
 * - `unavailable` — the server is busy or timed out; `partial-failure` — part of the work
 *   failed; `server` — anything else.
 */
export type TmApiErrorKind =
  | 'cancelled'
  | 'offline'
  | 'unauthenticated'
  | 'step-up'
  | 'forbidden'
  | 'not-found'
  | 'tenant-not-found'
  | 'tenant-unavailable'
  | 'read-only'
  | 'validation'
  | 'query-invalid'
  | 'bad-request'
  | 'conflict'
  | 'confirmation'
  | 'limit'
  | 'client-outdated'
  | 'unavailable'
  | 'partial-failure'
  | 'server';

/**
 * A problem body as the server sends it: the standard members, the kebab-case
 * `code`, and whatever `arguments`, `errors` and `errorDetails` the problem
 * carries, as plain JSON.
 *
 * @example
 * ```ts
 * const reason = error.problem?.['arguments'];
 * ```
 */
export type TmProblem = TmJsonObject & {
  /** The kebab-case problem code, such as `forbidden`. */
  readonly code: string;
  /** The request's trace id, as the server recorded it. */
  readonly instance: string;
  /** The HTTP status. */
  readonly status: number;
};

/**
 * One item of a problem's `errors`: a dotted validation code, its message in
 * the request's language, and the item's `path` and `arguments` when it has
 * them.
 */
export type TmProblemError = TmJsonObject & {
  /** The dotted validation code, such as `Tree.Cycle`. */
  readonly code: string;
  /** The message, rendered in the request's language. */
  readonly message: string;
};

/** What a {@link TmApiError} is made from. */
export interface TmApiErrorInit {
  /** What went wrong. */
  readonly kind: TmApiErrorKind;
  /** The HTTP status; 0, the default, for a kind decided on the client. */
  readonly status?: number;
  /** The problem code, or the client's own code for a client-side kind; the kind when omitted. */
  readonly code?: string;
  /** The call's trace id; empty when omitted. */
  readonly traceId?: string;
  /** The problem body, when the server sent one. */
  readonly problem?: TmProblem | null;
  /** The wait the server asked for in `Retry-After`, in milliseconds. */
  readonly retryAfterMs?: number | null;
  /** For `step-up`, the stronger sign-in the call asked for. */
  readonly challenge?: TmStepUpChallenge | null;
  /** The message; a summary of the kind and the code when omitted. */
  readonly message?: string;
}

/**
 * The error every `TmApi` call rejects with. Its `kind` says how the platform
 * presents it, its `code` names the exact cause, and its `traceId` is the
 * reference support asks for.
 *
 * @example
 * ```ts
 * try {
 *   await api.write('centers/save', { entities });
 * } catch (error) {
 *   if (error instanceof TmApiError && error.kind === 'validation') {
 *     showErrors(error.problem?.['errors']);
 *   }
 * }
 * ```
 */
export class TmApiError extends Error {
  /** The error's name, for stack traces and logs. */
  override readonly name = 'TmApiError';

  /** What went wrong. */
  readonly kind: TmApiErrorKind;

  /** The HTTP status, or 0 for a kind decided on the client. */
  readonly status: number;

  /** The problem code, such as `forbidden`, or the client's own, such as `network` or `not-sent`. */
  readonly code: string;

  /** The call's trace id: the server's, else the one the client sent. */
  readonly traceId: string;

  /** The problem body, or `null` when the server sent none. */
  readonly problem: TmProblem | null;

  /** The wait the server asked for in `Retry-After`, in milliseconds, or `null`. */
  readonly retryAfterMs: number | null;

  /** For `step-up`, the stronger sign-in the call asked for; otherwise `null`. */
  readonly challenge: TmStepUpChallenge | null;

  /**
   * Creates the error.
   *
   * @param init - Its kind, status, code, trace id and problem.
   */
  constructor(init: TmApiErrorInit) {
    const code = init.code ?? init.kind;
    super(init.message ?? `The request failed: ${init.kind} (${code}).`);
    this.kind = init.kind;
    this.status = init.status ?? 0;
    this.code = code;
    this.traceId = init.traceId ?? '';
    this.problem = init.problem ?? null;
    this.retryAfterMs = init.retryAfterMs ?? null;
    this.challenge = init.challenge ?? null;
  }
}
