// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmApiError, TmContextError, TmJson, TmJsonObject } from '@tellma/core';

import type { TmSearchMode } from './tm-search-model';
import type { TmSearchClauseOrigin, TmSearchRequest } from './tm-search-request';
import type { TmSearchRegionState } from './tm-search-state';

// A refused query's diagnostics mapped back to their controls (spec 0030
// §5.3). The server names each diagnostic's clause, `filters[i]` or
// `namedFilters[i]` among others, and the request kept each leaf's and each
// name's origin in that order, so a diagnostic on the user's advanced filter
// offers Edit filter, a diagnostic on a leaf or a name from code is a
// distribution's bug, and one on a drill-through list's fixed filter means
// the link itself is bad.

/** One diagnostic of a refused query, with the control it came from. */
export interface TmSearchDiagnostic {
  /** The clause, such as `filters[1]`. */
  readonly clause: string;
  /** The code, such as `Queryex.UnknownProperty`. */
  readonly code: string;
  /** Where the problem starts in the clause's text. */
  readonly start: number;
  /** The problem's length in the clause's text. */
  readonly length: number;
  /** The problem's location, when the server names one. */
  readonly location: string | null;
  /** The message's arguments. */
  readonly arguments: TmJsonObject;
  /** The message in the request's language; `null` when the problem carries none. */
  readonly message: string | null;
  /** The leaf or named filter the clause holds; `null` for any other clause, such as the select. */
  readonly origin: TmSearchClauseOrigin | null;
}

/**
 * What a refused query shows:
 *
 * - `invalid-filter` — the advanced filter is invalid, and Edit filter is offered;
 * - `invalid-code` — a leaf or a named filter from code is invalid: the same heading without Edit filter;
 * - `invalid-link` — a drill-through list's fixed filter is invalid;
 * - `other` — no leaf is at fault, such as an invalid expression column, and the error view shows.
 */
export type TmSearchDiagnosisState = 'invalid-filter' | 'invalid-code' | 'invalid-link' | 'other';

/** A refused query's diagnostics, and what the page shows for them. */
export interface TmSearchDiagnosis {
  /** What the page shows. */
  readonly state: TmSearchDiagnosisState;
  /** The diagnostic the state shows: the first at fault for it; `null` when the problem lists none. */
  readonly diagnostic: TmSearchDiagnostic | null;
  /** Every diagnostic, in the server's order. */
  readonly diagnostics: readonly TmSearchDiagnostic[];
}

/** A clause that holds one of the request's leaves or named filters. */
const CLAUSE = /^(filters|namedFilters)\[(\d+)\]$/;

/**
 * Maps a refused query's diagnostics back to the controls their clauses
 * came from, and picks the state the page shows. The advanced filter comes
 * first, since the user can fix it; then a list's bad link; then a leaf or
 * a name from code, which the host also logs.
 *
 * @param request - The request that was refused.
 * @param mode - Where the page runs.
 * @param error - The error the read rejected with.
 * @returns The diagnosis, or `null` when the error is not an invalid query.
 */
export function tmSearchDiagnose(request: TmSearchRequest, mode: TmSearchMode, error: TmApiError): TmSearchDiagnosis | null {
  if (error.kind !== 'query-invalid') {
    return null;
  }
  const problem = error.problem;
  const details = asObject(problem?.['errorDetails']);
  const items = Array.isArray(details?.['diagnostics']) ? (details['diagnostics'] as readonly TmJson[]) : [];
  const errors = Array.isArray(problem?.['errors']) ? (problem['errors'] as readonly TmJson[]) : [];

  // --- Each diagnostic, its message by its clause's turn, and its origin ---
  const used = new Map<string, number>();
  const diagnostics: TmSearchDiagnostic[] = [];
  for (const item of items) {
    const value = asObject(item);
    if (value === null || typeof value['clause'] !== 'string' || typeof value['code'] !== 'string') {
      continue;
    }
    const clause = value['clause'];
    // The n-th diagnostic of a clause takes the n-th localized error at that path.
    const turn = used.get(clause) ?? 0;
    used.set(clause, turn + 1);
    const message = errors.map(asObject).filter((entry) => entry?.['path'] === clause)[turn]?.['message'];
    diagnostics.push({
      clause,
      code: value['code'],
      start: typeof value['start'] === 'number' ? value['start'] : 0,
      length: typeof value['length'] === 'number' ? value['length'] : 0,
      location: typeof value['location'] === 'string' ? value['location'] : null,
      arguments: asObject(value['arguments']) ?? {},
      message: typeof message === 'string' ? message : null,
      origin: originOf(request, clause),
    });
  }

  // --- The state, by what is at fault ---
  const first = (test: (origin: TmSearchClauseOrigin) => boolean): TmSearchDiagnostic | undefined =>
    diagnostics.find((diagnostic) => diagnostic.origin !== null && test(diagnostic.origin));
  const advanced = first((origin) => origin.kind === 'advanced');
  if (advanced) {
    return { state: 'invalid-filter', diagnostic: advanced, diagnostics };
  }
  const link = mode === 'list' ? first((origin) => origin.kind === 'fixed') : undefined;
  if (link) {
    return { state: 'invalid-link', diagnostic: link, diagnostics };
  }
  const code = first(() => true);
  if (code) {
    return { state: 'invalid-code', diagnostic: code, diagnostics };
  }
  return { state: 'other', diagnostic: diagnostics[0] ?? null, diagnostics };
}

/**
 * The state the results region shows for a refused query: the invalid
 * filter's or the code's, with the first diagnostic's message and the text
 * it points at, or the invalid link's.
 *
 * @param request - The request that was refused.
 * @param mode - Where the page runs.
 * @param error - The error the read rejected with.
 * @returns The state, or `null` when no leaf is at fault and the error view shows.
 */
export function tmSearchRefusedState(request: TmSearchRequest, mode: TmSearchMode, error: TmApiError): TmSearchRegionState | null {
  const diagnosis = tmSearchDiagnose(request, mode, error);
  if (diagnosis === null || diagnosis.state === 'other') {
    return null;
  }
  if (diagnosis.state === 'invalid-link') {
    return { kind: 'invalid-link' };
  }
  // The token is the slice of the leaf's own text the diagnostic points at;
  // a list's fixed leaf is the link's, and shows none.
  const diagnostic = diagnosis.diagnostic;
  const filters = request.body['filters'];
  const clause = diagnostic?.origin?.kind === 'fixed' || diagnostic === null ? null : Array.isArray(filters) ? filters[indexOf(diagnostic.clause)] : null;
  const token = typeof clause === 'string' && diagnostic !== null && diagnostic.length > 0 ? clause.slice(diagnostic.start, diagnostic.start + diagnostic.length) : null;
  return { kind: diagnosis.state, message: diagnostic?.message ?? null, token };
}

/**
 * A failed read's errors as the page context carries them: each diagnostic
 * of a refused query, else the error itself, with its trace id.
 *
 * @param request - The request that failed.
 * @param mode - Where the page runs.
 * @param error - The error the read rejected with.
 * @returns The errors.
 */
export function tmSearchContextErrors(request: TmSearchRequest, mode: TmSearchMode, error: TmApiError): readonly TmContextError[] {
  const trace = error.traceId !== '' && { traceId: error.traceId };
  const diagnosis = tmSearchDiagnose(request, mode, error);
  if (diagnosis !== null && diagnosis.diagnostics.length > 0) {
    return diagnosis.diagnostics.map((diagnostic) => ({
      source: 'request',
      code: diagnostic.code,
      message: diagnostic.message ?? diagnostic.code,
      path: diagnostic.clause,
      ...trace,
    }));
  }
  return [{ source: 'request', code: error.code, message: error.message, ...trace }];
}

/**
 * The index a clause such as `filters[1]` names.
 *
 * @param clause - The clause.
 * @returns The index, or -1.
 */
function indexOf(clause: string): number {
  const match = /^filters\[(\d+)\]$/.exec(clause);
  return match === null ? -1 : Number(match[1]);
}

/**
 * The origin of the leaf or named filter a clause names.
 *
 * @param request - The request.
 * @param clause - The clause, such as `filters[1]`.
 * @returns The origin, or `null` for a clause that holds neither.
 */
function originOf(request: TmSearchRequest, clause: string): TmSearchClauseOrigin | null {
  const match = CLAUSE.exec(clause);
  if (match === null) {
    return null;
  }
  const origins = match[1] === 'filters' ? request.filters : request.namedFilters;
  return origins[Number(match[2])] ?? null;
}

/**
 * A JSON value as an object.
 *
 * @param value - The value.
 * @returns The object, or `null` when the value is not one.
 */
function asObject(value: TmJson | undefined): TmJsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as TmJsonObject) : null;
}
