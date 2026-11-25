// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmApiError, type TmJson, type TmJsonObject, type TmQueryRowSet } from '@tellma/core';

import type { TmSearchRequest } from './tm-search-request';

// The read of a search page's rows (spec 0030 §5.2-§5.4, §14), as plain
// data and pure functions: what a read is keyed by, when a rebuilt request
// needs a read of its own, what the wire answers, how the total and the
// capped count read, and what a failed read shows. The host owns the
// resource; these decide.

/** Where a read came from, as the `tm.search.query` timing reports it. */
export type TmSearchReadSource = 'load' | 'typing' | 'filter' | 'order' | 'page' | 'refresh' | 'restore' | 'anchor';

/**
 * What one read is keyed by: the canonical query of the state it reads, its
 * request, and the refresh generation. A new key is a new read, which
 * aborts the one in flight.
 */
export interface TmSearchReadKey {
  /** The identity of the state the read answers: its canonical URL, or in picker mode its canonical query. */
  readonly canonical: string;
  /** The request. */
  readonly request: TmSearchRequest;
  /** The refresh generation the read was started under. */
  readonly generation: number;
  /** What started the read. */
  readonly source: TmSearchReadSource;
}

/** What a query or a parent read answers. */
export interface TmSearchWire {
  /** The rows. */
  readonly rows: TmQueryRowSet;
  /** The count of matching rows, at most the entity's cap; absent from a parent read. */
  readonly count?: number;
  /** Whether the count reached the entity's cap. */
  readonly countCapped?: boolean;
  /** In a search tree, the ancestors of the matched rows. */
  readonly ancestors?: TmQueryRowSet;
}

/** A read that answered. */
export interface TmSearchResult {
  /** Whether the read answered: always. */
  readonly ok: true;
  /** The read's key. */
  readonly key: TmSearchReadKey;
  /** The rows. */
  readonly set: TmQueryRowSet;
  /** In a search tree, the ancestors of the matched rows; `null` otherwise. */
  readonly ancestors: TmQueryRowSet | null;
  /** The number of matching rows; `null` when unknown. */
  readonly total: number | null;
  /** Whether `total` is a lower bound. */
  readonly capped: boolean;
}

/** A read that failed. */
export interface TmSearchFailure {
  /** Whether the read answered: never. */
  readonly ok: false;
  /** The read's key. */
  readonly key: TmSearchReadKey;
  /** What went wrong, as an API error; a fault of the client's reads as a `server` error. */
  readonly error: TmApiError;
}

/** How a read ended. */
export type TmSearchOutcome = TmSearchResult | TmSearchFailure;

/** The flags and the clauses a re-read compares; every body member but the select. */
const COMPARED = ['search', 'filters', 'namedFilters', 'orderBy', 'skip', 'take', 'includeCount', 'includeInactive', 'includeAncestors', 'parentIds', 'includeRoots'] as const;

/**
 * Whether a request rebuilt over the same canonical query, after a language
 * switch or a permissions or settings move, needs a read of its own: its
 * route, order, filters, search, paging or flags differ from the held
 * result's request, or its select gained a term. A select that only lost
 * terms reads nothing, since the held rows carry every term it still names.
 *
 * @param held - The held result's request.
 * @param next - The rebuilt request.
 * @returns Whether it needs a read.
 *
 * @example
 * ```ts
 * tmSearchRereads(held, next); // true after a switch to Arabic under a text order, `coalesce(Name2, Name)`
 * ```
 */
export function tmSearchRereads(held: TmSearchRequest, next: TmSearchRequest): boolean {
  if (held.route !== next.route) {
    return true;
  }
  if (COMPARED.some((member) => !same(held.body[member], next.body[member]))) {
    return true;
  }
  const had = new Set(terms(held.body['select']));
  return terms(next.body['select']).some((term) => !had.has(term));
}

/**
 * The total and whether it is a lower bound, from a read's answer: a browse
 * tree's rows are every row; a capped count is a lower bound until a short
 * page shows where the rows end.
 *
 * @param request - The request.
 * @param wire - The answer.
 * @returns The total, `null` when unknown, and whether it is capped.
 *
 * @example
 * ```ts
 * tmSearchTotal(request, { rows, count: 10000, countCapped: true }); // { total: 10000, capped: true }
 * ```
 */
export function tmSearchTotal(request: TmSearchRequest, wire: TmSearchWire): { readonly total: number | null; readonly capped: boolean } {
  const shown = wire.rows.rows.length;
  if (request.tree === 'browse') {
    return { total: shown, capped: false };
  }
  const skip = typeof request.body['skip'] === 'number' ? request.body['skip'] : 0;
  const take = typeof request.body['take'] === 'number' ? request.body['take'] : Number.POSITIVE_INFINITY;
  // A short page ends the rows: the total is exact whatever the count said.
  const short = shown < take && (shown > 0 || skip === 0);
  if (wire.countCapped === true) {
    return short ? { total: skip + shown, capped: false } : { total: wire.count ?? skip + shown, capped: true };
  }
  if (typeof wire.count === 'number') {
    return { total: wire.count, capped: false };
  }
  return short ? { total: skip + shown, capped: false } : { total: null, capped: false };
}

/**
 * A read's rejection as an API error: the API's own, else a fault of the
 * client's, which reads as a `server` error.
 *
 * @param error - What the read rejected with.
 * @returns The error.
 */
export function tmSearchApiError(error: unknown): TmApiError {
  return error instanceof TmApiError ? error : new TmApiError({ kind: 'server', code: 'client-fault', message: error instanceof Error ? error.message : String(error) });
}

/**
 * The reason of a `422 Query.ArithmeticFailed`: a column or a filter that
 * cannot be calculated for some rows.
 *
 * @param error - The error.
 * @returns `divide-by-zero` or `overflow`, `other` for a reason the client does not know; `null` for any other error.
 */
export function tmSearchArithmetic(error: TmApiError): 'divide-by-zero' | 'overflow' | 'other' | null {
  if (error.kind !== 'validation' || !Array.isArray(error.problem?.['errors'])) {
    return null;
  }
  const failed = (error.problem['errors'] as readonly TmJson[]).map(asObject).find((entry) => entry?.['code'] === 'Query.ArithmeticFailed');
  if (failed === undefined || failed === null) {
    return null;
  }
  const reason = asObject(failed['arguments'] ?? null)?.['reason'];
  return reason === 'divide-by-zero' || reason === 'overflow' ? reason : 'other';
}

// -----------------------------------------------------------------------------
// Helpers
// -----------------------------------------------------------------------------

/**
 * A select's terms, folded, so a term's case never counts as a change. The
 * select splits on its top-level commas only: an expression column's term
 * may hold commas of its own, inside a call's parentheses or a string.
 *
 * @param select - The select.
 * @returns The terms.
 */
function terms(select: TmJson | undefined): readonly string[] {
  if (typeof select !== 'string') {
    return [];
  }
  const found: string[] = [];
  let depth = 0;
  let quoted = false;
  let start = 0;
  for (let at = 0; at < select.length; at++) {
    const char = select[at];
    if (char === "'") {
      // A doubled quote inside a string toggles twice, which leaves it quoted.
      quoted = !quoted;
    } else if (!quoted && char === '(') {
      depth++;
    } else if (!quoted && char === ')') {
      depth--;
    } else if (!quoted && depth === 0 && char === ',') {
      found.push(select.slice(start, at));
      start = at + 1;
    }
  }
  found.push(select.slice(start));
  return found.map((term) => term.trim().toLowerCase());
}

/**
 * Whether two body members are equal, by value.
 *
 * @param a - One member.
 * @param b - The other.
 * @returns Whether they are equal.
 */
function same(a: TmJson | undefined, b: TmJson | undefined): boolean {
  return a === b || JSON.stringify(a) === JSON.stringify(b);
}

/**
 * A JSON value as an object.
 *
 * @param value - The value.
 * @returns The object, or `null` when the value is not one.
 */
function asObject(value: TmJson): TmJsonObject | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? (value as TmJsonObject) : null;
}
