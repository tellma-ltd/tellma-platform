// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmApiError, type TmJsonObject } from '@tellma/core';

import { tmSearchDiagnose } from './tm-search-diagnostics';
import type { TmSearchRequest } from './tm-search-request';

// A refused query's diagnostics mapped back to the controls their clauses
// came from, by the request's leaf order: the advanced filter offers Edit
// filter, a leaf or a name from code is a distribution's bug, a list's fixed
// filter is a bad link, and any other clause shows the error view.

/** A request with a leaf from every source and two named filters. */
const REQUEST: TmSearchRequest = {
  tree: null,
  route: 'customers/query',
  body: {},
  filters: [{ kind: 'tab', key: 'open' }, { kind: 'advanced' }, { kind: 'custom', key: 'mine', value: '1' }, { kind: 'column', key: 'Code' }, { kind: 'fixed', index: 0 }],
  namedFilters: [
    { kind: 'tab', key: 'starred' },
    { kind: 'custom', key: 'shared', value: '1' },
  ],
};

/**
 * A `400 query-invalid` with diagnostics, each with its localized message.
 *
 * @param diagnostics - Each diagnostic's clause and code.
 * @returns The error.
 */
function invalid(...diagnostics: readonly (readonly [clause: string, code: string])[]): TmApiError {
  const items: TmJsonObject[] = diagnostics.map(([clause, code], index) => ({ clause, code, start: index, length: 3, location: null, arguments: { name: `n${index}` } }));
  return new TmApiError({
    kind: 'query-invalid',
    status: 400,
    code: 'query-invalid',
    problem: {
      code: 'query-invalid',
      instance: 'trace-1',
      status: 400,
      errors: diagnostics.map(([clause, code], index) => ({ code, message: `Message ${index}`, path: clause })),
      errorDetails: { diagnostics: items },
    },
  });
}

describe('search diagnostics', () => {
  it('map a diagnostic on the advanced filter to the invalid-filter state, with its message', () => {
    const diagnosis = tmSearchDiagnose(REQUEST, 'page', invalid(['filters[1]', 'Queryex.UnknownProperty']))!;
    expect(diagnosis.state).toBe('invalid-filter');
    expect(diagnosis.diagnostic).toEqual({
      clause: 'filters[1]',
      code: 'Queryex.UnknownProperty',
      start: 0,
      length: 3,
      location: null,
      arguments: { name: 'n0' },
      message: 'Message 0',
      origin: { kind: 'advanced' },
    });
  });

  it('map a diagnostic on a leaf or a name from code to the code state', () => {
    for (const clause of ['filters[0]', 'filters[2]', 'filters[3]']) {
      const diagnosis = tmSearchDiagnose(REQUEST, 'page', invalid([clause, 'Queryex.UnknownProperty']))!;
      expect(diagnosis.state, clause).toBe('invalid-code');
      expect(diagnosis.diagnostic?.origin, clause).toEqual(REQUEST.filters[Number(clause.slice(8, 9))]);
    }
    const named = tmSearchDiagnose(REQUEST, 'page', invalid(['namedFilters[1]', 'Query.UnknownNamedFilter']))!;
    expect(named.state).toBe('invalid-code');
    expect(named.diagnostic?.origin).toEqual({ kind: 'custom', key: 'shared', value: '1' });
  });

  it('map a diagnostic on a fixed leaf to a bad link in a list, and to the code state in a picker', () => {
    expect(tmSearchDiagnose(REQUEST, 'list', invalid(['filters[4]', 'Queryex.Syntax']))!.state).toBe('invalid-link');
    expect(tmSearchDiagnose(REQUEST, 'picker', invalid(['filters[4]', 'Queryex.Syntax']))!.state).toBe('invalid-code');
  });

  it('prefer the advanced filter, then a bad link, over a leaf from code', () => {
    const both = tmSearchDiagnose(REQUEST, 'list', invalid(['filters[0]', 'A'], ['filters[4]', 'B'], ['filters[1]', 'C']))!;
    expect(both.state).toBe('invalid-filter');
    expect(both.diagnostic?.code).toBe('C');
    expect(both.diagnostics.map((diagnostic) => diagnostic.code)).toEqual(['A', 'B', 'C']);
    expect(tmSearchDiagnose(REQUEST, 'list', invalid(['filters[0]', 'A'], ['filters[4]', 'B']))!.diagnostic?.code).toBe('B');
  });

  it('show the error view for any other clause', () => {
    const select = tmSearchDiagnose(REQUEST, 'page', invalid(['select', 'Queryex.UnknownFunction'], ['filters[9]', 'X']))!;
    expect(select.state).toBe('other');
    expect(select.diagnostic?.origin).toBeNull();
    expect(select.diagnostics[1]?.origin).toBeNull();
    expect(tmSearchDiagnose(REQUEST, 'page', new TmApiError({ kind: 'query-invalid', status: 400 }))).toEqual({ state: 'other', diagnostic: null, diagnostics: [] });
  });

  it('give each of a clause’s diagnostics its own message, in turn', () => {
    const diagnosis = tmSearchDiagnose(REQUEST, 'page', invalid(['filters[1]', 'A'], ['filters[1]', 'B']))!;
    expect(diagnosis.diagnostics.map((diagnostic) => diagnostic.message)).toEqual(['Message 0', 'Message 1']);
  });

  it('read nothing from an error that is not an invalid query', () => {
    expect(tmSearchDiagnose(REQUEST, 'page', new TmApiError({ kind: 'forbidden', status: 403 }))).toBeNull();
  });
});
