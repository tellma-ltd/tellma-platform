// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmEntity, type TmEntityDeclaration, type TmLazyComponent } from '@tellma/core';
import { tmTestEntities } from '@tellma/core-testing';

import { tmCheckEntityDeclaration, TmPathResolver } from './tm-entity-checks';

// One entity declaration against the server's recorded Entities chunk, by
// code: members that need an operation the entity lacks, a launcher section
// on an entity the launcher cannot open, unknown actions and paths that do
// not resolve.

/** A lazy layout that is never loaded. */
const lazy: TmLazyComponent = () => Promise.reject(new Error('never loaded'));

/** The findings of one declaration, as code, member and facts. */
function check(declaration: TmEntityDeclaration): unknown[] {
  return tmCheckEntityDeclaration(declaration, tmTestEntities().chunk).map((finding) => [finding.code, finding.member, finding.facts]);
}

describe('tmCheckEntityDeclaration', () => {
  it('passes a declaration whose members the entity serves', () => {
    expect(
      check(
        tmEntity('acme.Customer', {
          section: 'sales',
          columns: ['Code', 'Name', 'Currency.Code', { path: 'CreditLimit', select: ['Currency.DecimalPlaces'] }, { expression: 'Code', labelKey: 'Code' }],
          orderBy: 'Code:desc',
          tile: { image: 'Image', title: 'Name', subtitle: 'Code', topEnd: 'CreditLimit', badge: 'Currency' },
          picker: { description: ['Code', 'CustomerType'], orderBy: 'Name' },
          actions: [{ name: 'set-credit-limit' }],
          details: lazy,
        }),
      ),
    ).toEqual([]);
  });

  it('reports unknown-path for each member that names one, ignoring case where the model matches', () => {
    expect(
      check(
        tmEntity('acme.Customer', {
          columns: ['code', 'Cod', { path: 'Currency.Nope' }, { path: 'Code', select: ['Code.Length'] }],
          orderBy: 'Nope:desc',
          tile: { image: 'Picture' },
          picker: { description: ['Currency.Rate'], orderBy: 'Rank.Nope' },
        } as never),
      ),
    ).toEqual([
      ['unknown-path', 'columns', { member: 'columns', path: 'Cod' }],
      ['unknown-path', 'columns', { member: 'columns', path: 'Currency.Nope' }],
      ['unknown-path', 'columns', { member: 'columns', path: 'Code.Length' }],
      ['unknown-path', 'orderBy', { member: 'orderBy', path: 'Nope' }],
      ['unknown-path', 'tile', { member: 'tile', path: 'Picture' }],
      ['unknown-path', 'picker', { member: 'picker', path: 'Currency.Rate' }],
      ['unknown-path', 'picker', { member: 'picker', path: 'Rank.Nope' }],
    ]);
  });

  it('reports unknown-action', () => {
    expect(check(tmEntity('acme.Customer', { actions: [{ name: 'set-credit-limit' }, { name: 'archive' }] }))).toEqual([
      ['unknown-action', 'actions', { action: 'archive' }],
    ]);
  });

  it('reports operation-missing for each member whose operation the entity lacks', () => {
    // acme.Currency offers query and export only; core.Blob offers nothing.
    expect(check(tmEntity('acme.Currency', { details: lazy, columns: ['Code'] }))).toEqual([
      ['operation-missing', 'details', { member: 'details', operation: 'details' }],
    ]);
    expect(check(tmEntity('core.Blob', { search: lazy, tabs: [{ key: 'all', labelKey: 'All' }], filters: [] } as never))).toEqual([
      ['operation-missing', 'search', { member: 'search', operation: 'query' }],
      ['operation-missing', 'filters', { member: 'filters', operation: 'query' }],
      ['operation-missing', 'tabs', { member: 'tabs', operation: 'query' }],
    ]);
  });

  it('reports entity-not-routable for a section on an entity with no route segment', () => {
    expect(check(tmEntity('core.Settings', { section: 'administration' }))).toEqual([
      ['entity-not-routable', 'section', { reason: 'no-segment' }],
    ]);
  });

  it('finds nothing for an entity the chunk lacks, which the app check reports itself', () => {
    expect(check(tmEntity('acme.Ghost', { columns: ['Nope'] } as never))).toEqual([]);
  });

  it('takes a resolver indexed once for many declarations', () => {
    const resolver = new TmPathResolver(tmTestEntities().chunk);

    expect(resolver.resolves('gl.Center', 'Parent.Manager.Name')).toBe(true);
    expect(resolver.resolves('gl.Center', 'Parent.')).toBe(false);
    expect(tmCheckEntityDeclaration(tmEntity('gl.Center', { columns: ['Nope'] } as never), resolver)).toHaveLength(1);
  });
});
