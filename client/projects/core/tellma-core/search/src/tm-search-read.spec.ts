// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmApiError, type TmJsonObject, type TmQueryRowSet } from '@tellma/core';

import { tmSearchApiError, tmSearchArithmetic, tmSearchRereads, tmSearchTotal } from './tm-search-read';
import type { TmSearchRequest } from './tm-search-request';

// The read's pure rules: when a request rebuilt over the same query needs
// a read of its own, how a total and a capped count read from an answer,
// and how a refused calculation and a client fault read as errors.

/**
 * A request for customers.
 *
 * @param body - The body's members beside a select of `Id,Code,Name`.
 * @param tree - The tree's form.
 * @returns The request.
 */
function request(body: TmJsonObject = {}, tree: TmSearchRequest['tree'] = null): TmSearchRequest {
  return {
    tree,
    route: tree === 'browse' ? 'customers/get-by-parent-ids' : 'customers/query',
    body: { select: 'Id,Code,Name', orderBy: 'Code', skip: 0, take: 50, includeCount: true, ...body },
    filters: [],
    namedFilters: [],
  };
}

/**
 * A row set of `count` rows of one column.
 *
 * @param count - How many rows.
 * @returns The rows.
 */
function rows(count: number): TmQueryRowSet {
  return {
    columns: [{ name: 'Id', type: 'Int32', nullable: false, groupingKey: false, path: ['Id'] }],
    rows: Array.from({ length: count }, (_, index) => [index + 1]),
  };
}

describe('tmSearchRereads', () => {
  it('reads nothing when the rebuilt request is the same, or its select only lost terms', () => {
    expect(tmSearchRereads(request(), request())).toBe(false);
    expect(tmSearchRereads(request(), request({ select: 'Id,Code' }))).toBe(false);
  });

  it('reads nothing for a term whose case alone changed', () => {
    expect(tmSearchRereads(request(), request({ select: 'id, code, NAME' }))).toBe(false);
  });

  it('reads again when the select gains a term, as a re-granted column does', () => {
    expect(tmSearchRereads(request(), request({ select: 'Id,Code,Name,CreditLimit' }))).toBe(true);
  });

  it('reads again when the order moves, as a text order does on a language switch', () => {
    expect(tmSearchRereads(request(), request({ orderBy: 'coalesce(Name2, Name)' }))).toBe(true);
  });

  it('reads again when a filter drops, a flag moves or the route changes', () => {
    expect(tmSearchRereads(request({ filters: ['CreditLimit > 0'] }), request())).toBe(true);
    expect(tmSearchRereads(request(), request({ includeInactive: true }))).toBe(true);
    expect(tmSearchRereads(request(), request({}, 'browse'))).toBe(true);
  });

  it("splits an expression's term on its top-level commas only", () => {
    const held = request({ select: "Id,coalesce(Name2, 'a,b')" });
    expect(tmSearchRereads(held, request({ select: "Id,coalesce(Name2, 'a,b')" }))).toBe(false);
    expect(tmSearchRereads(held, request({ select: "Id,coalesce(Name3, 'a,b')" }))).toBe(true);
  });
});

describe('tmSearchTotal', () => {
  it('takes the count as exact', () => {
    expect(tmSearchTotal(request(), { rows: rows(50), count: 120 })).toEqual({ total: 120, capped: false });
  });

  it('takes a capped count as a lower bound', () => {
    expect(tmSearchTotal(request(), { rows: rows(50), count: 10_000, countCapped: true })).toEqual({ total: 10_000, capped: true });
  });

  it('takes a short page as the end of the rows, whatever the capped count said', () => {
    expect(tmSearchTotal(request({ skip: 10_000 }), { rows: rows(7), count: 10_000, countCapped: true })).toEqual({ total: 10_007, capped: false });
  });

  it('knows no total for a full page without a count, and every row of a browse tree', () => {
    expect(tmSearchTotal(request({ includeCount: false }), { rows: rows(50) })).toEqual({ total: null, capped: false });
    expect(tmSearchTotal(request({}, 'browse'), { rows: rows(3) })).toEqual({ total: 3, capped: false });
  });
});

describe('tmSearchArithmetic', () => {
  /**
   * A refused calculation.
   *
   * @param reason - The server's reason.
   * @returns The error.
   */
  function failed(reason: string): TmApiError {
    return new TmApiError({
      kind: 'validation',
      status: 422,
      code: 'Query.ArithmeticFailed',
      problem: { code: 'Query.ArithmeticFailed', instance: 'trace-1', status: 422, errors: [{ code: 'Query.ArithmeticFailed', arguments: { reason } }] },
    });
  }

  it('reads the reason of a refused calculation', () => {
    expect(tmSearchArithmetic(failed('divide-by-zero'))).toBe('divide-by-zero');
    expect(tmSearchArithmetic(failed('overflow'))).toBe('overflow');
    expect(tmSearchArithmetic(failed('precision'))).toBe('other');
  });

  it('reads no reason from any other error', () => {
    expect(tmSearchArithmetic(new TmApiError({ kind: 'validation', status: 422, code: 'validation' }))).toBeNull();
    expect(tmSearchArithmetic(new TmApiError({ kind: 'server', status: 500, code: 'server' }))).toBeNull();
  });
});

describe('tmSearchApiError', () => {
  it("keeps the API's error, and reads a client fault as a server error", () => {
    const offline = new TmApiError({ kind: 'offline' });
    expect(tmSearchApiError(offline)).toBe(offline);
    expect(tmSearchApiError(new TypeError('boom'))).toMatchObject({ kind: 'server', code: 'client-fault', message: 'boom' });
  });
});
