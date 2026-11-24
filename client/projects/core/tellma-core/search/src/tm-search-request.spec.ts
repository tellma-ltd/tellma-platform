// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import type { TmSearchDeclaration, TmSearchSetting } from './tm-search-model';
import { tmSearchPlan, tmSearchRequest, type TmSearchRequest } from './tm-search-request';
import { tmSearchResolve } from './tm-search-resolve';
import { TM_TEST_CUSTOMERS, tmSearchTestHash, tmSearchTestOpening, tmSearchTestPayload, tmSearchTestSetting } from './tm-search.testing.util';

// The rows request of a query state: the select over the shown columns with
// the tile's and the tree's paths, the leaves and named filters in their
// order with their origins, the order's fallbacks, the paging and the flags,
// and the browse tree's one call.

/**
 * The request of a page opened at a URL.
 *
 * @param setting - The page's setting.
 * @param url - The URL.
 * @param expanded - A browse tree's remembered expansion.
 * @returns The request.
 */
function requestAt(setting: TmSearchSetting, url: string, expanded?: readonly number[]): TmSearchRequest {
  const { state } = tmSearchResolve(setting, tmSearchTestOpening(url));
  return tmSearchRequest(setting, state, tmSearchPlan(setting, state), expanded && { expanded });
}

/** Centers' search members: two columns and an order. */
const CENTERS: TmSearchDeclaration = { columns: ['Code', 'Name'], orderBy: 'Code' };

describe('search request', () => {
  it('reads customers in Arabic as the example of the request reads them', async () => {
    await tmEntityRig({ access: { isSystem: true }, languages: 2, language: 'ar' });
    const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
    const hash = tmSearchTestHash('CreditLimit > 100000');
    const request = requestAt(setting, `/17/customers?q=al&f.CustomerType=in:Retail&x.mine=1&order=Name${hash}`);
    expect(request.route).toBe('customers/query');
    expect(request.tree).toBeNull();
    expect(request.body).toEqual({
      select: 'Id,Code,Name,Name2,CustomerType,Currency.Id,Currency.Code,CreditLimit,IsActive,ImageId,Image.Variants',
      search: 'al',
      filters: ['CreditLimit > 100000', 'CreatedById = me()', "CustomerType in ('Retail')"],
      orderBy: 'coalesce(Name2, Name)',
      skip: 0,
      take: 50,
      includeCount: true,
    });
    expect(request.filters).toEqual([{ kind: 'advanced' }, { kind: 'custom', key: 'mine', value: '1' }, { kind: 'column', key: 'CustomerType' }]);
  });

  describe('leaves and named filters', () => {
    beforeEach(async () => {
      await tmEntityRig({ access: { isSystem: true } });
    });

    it('come in the order the diagnostics map back by, each with its origin', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { mode: 'picker', fixedLeaves: ['IsActive = true', 'CreditLimit < 9'] });
      const state = tmSearchResolve(setting, {}).state;
      const query = {
        tab: 'open',
        filter: 'Code <> "X"',
        custom: { shared: '1', credit: 'high', mine: '1' },
        columnFilters: { Rank: 'eq:B', CreditLimit: 'gt:5' },
      };
      const next = { ...state, query };
      const request = tmSearchRequest(setting, next, tmSearchPlan(setting, next));
      expect(request.body['filters']).toEqual([
        'CreditLimit > 0',
        'Code <> "X"',
        'CreatedById = me()',
        'CreditLimit >= 500000',
        'CreditLimit > 5',
        "Rank = 'B'",
        'IsActive = true',
        'CreditLimit < 9',
      ]);
      expect(request.filters).toEqual([
        { kind: 'tab', key: 'open' },
        { kind: 'advanced' },
        { kind: 'custom', key: 'mine', value: '1' },
        { kind: 'custom', key: 'credit', value: 'high' },
        { kind: 'column', key: 'CreditLimit' },
        { kind: 'column', key: 'Rank' },
        { kind: 'fixed', index: 0 },
        { kind: 'fixed', index: 1 },
      ]);
      expect(request.body['namedFilters']).toEqual(['shared-with-me']);
      expect(request.namedFilters).toEqual([{ kind: 'custom', key: 'shared', value: '1' }]);
    });

    it('send a named tab’s name before a named switch’s, and no leaf for a tab with neither', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const starred = requestAt(setting, '/17/customers?tab=starred&x.shared=1');
      expect(starred.body['namedFilters']).toEqual(['starred', 'shared-with-me']);
      expect(starred.namedFilters).toEqual([
        { kind: 'tab', key: 'starred' },
        { kind: 'custom', key: 'shared', value: '1' },
      ]);
      expect(starred.body['filters']).toBeUndefined();
      const all = requestAt(setting, '/17/customers');
      expect(all.body['filters']).toBeUndefined();
      expect(all.body['namedFilters']).toBeUndefined();
    });

    it('send a drill-through list’s fixed filter last, with inactive rows, and no filter expression', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { mode: 'list', segment: 'customers' });
      const request = requestAt(setting, `/17/list/customers?q=al&x.mine=1${tmSearchTestPayload({ filter: 'CreditLimit > 0' })}`);
      expect(request.body['filters']).toEqual(['CreatedById = me()', 'CreditLimit > 0']);
      expect(request.filters).toEqual([
        { kind: 'custom', key: 'mine', value: '1' },
        { kind: 'fixed', index: 0 },
      ]);
      expect(request.body['includeInactive']).toBe(true);
    });
  });

  it('leaves out a column filter whose column the user can no longer read', async () => {
    const { telemetry } = await tmEntityRig({ access: { securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] } });
    const setting = tmSearchTestSetting('acme.Customer', {});
    const state = { ...tmSearchResolve(setting, {}).state, query: { columnFilters: { 'Currency.Name': 'contains:usd', Code: 'eq:A' } } };
    const request = tmSearchRequest(setting, state, tmSearchPlan(setting, state));
    expect(request.body['filters']).toEqual(["Code = 'A'"]);
    expect(telemetry.recent().filter((item) => item.name === 'tm.entity.column-dropped').map((item) => item.props)).toEqual([
      { reason: 'unreachable', source: 'url' },
    ]);
  });

  describe('order, paging and flags', () => {
    beforeEach(async () => {
      await tmEntityRig({ access: { isSystem: true } });
    });

    it('orders by the order column, else the declaration, else the entity’s default', () => {
      const declared = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      expect(requestAt(declared, '/17/customers?order=Currency:desc').body['orderBy']).toBe('Currency.Code desc');
      expect(requestAt(declared, '/17/customers').body['orderBy']).toBe('Code');
      expect(requestAt(tmSearchTestSetting('acme.Customer', { orderBy: 'Name:desc' }), '/17/customers').body['orderBy']).toBe('Name desc');
      expect(requestAt(tmSearchTestSetting('acme.Customer'), '/17/customers').body['orderBy']).toBe('Id desc');
    });

    it('pages within the skip window', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const page = requestAt(setting, '/17/customers?page=3&size=20');
      expect([page.body['skip'], page.body['take'], page.body['includeCount']]).toEqual([40, 20, true]);
      // The last page of a window the size does not divide takes what is left of it.
      const narrow = { ...setting, descriptor: { ...setting.descriptor, limits: { ...setting.descriptor.limits, maxSkipWindow: 130 } } };
      const last = requestAt(narrow, '/17/customers?page=3');
      expect([last.body['skip'], last.body['take']]).toEqual([100, 30]);
    });

    it('includes inactive rows on an activatable entity only when asked', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      expect(requestAt(setting, '/17/customers?inactive=1').body['includeInactive']).toBe(true);
      expect(requestAt(setting, '/17/customers').body['includeInactive']).toBeUndefined();
      expect(requestAt(tmSearchTestSetting('acme.CustomerExtra'), '/17/customer-extras?inactive=1').body['includeInactive']).toBeUndefined();
    });
  });

  describe('trees', () => {
    beforeEach(async () => {
      await tmEntityRig({ access: { isSystem: true } });
    });

    it('load a browse tree in one unpaged call: the roots and the remembered expansion’s children', () => {
      const setting = tmSearchTestSetting('gl.Center', CENTERS);
      const request = requestAt(setting, '/17/centers', [12, 40, 41]);
      expect(request.tree).toBe('browse');
      expect(request.route).toBe('centers/get-by-parent-ids');
      expect(request.body).toEqual({
        parentIds: [12, 40, 41],
        includeRoots: true,
        orderBy: 'Code',
        includeInactive: false,
        select: 'Id,Code,Name,IsActive,ParentId,SubtreeCount,ActiveSubtreeCount',
      });
      // Active only is a flag, not a leaf.
      expect(requestAt(setting, '/17/centers?inactive=1').body['includeInactive']).toBe(true);
      expect(requestAt(setting, '/17/centers').body['parentIds']).toEqual([]);
    });

    it('send the newest remembered nodes within the entity’s limit', () => {
      const setting = tmSearchTestSetting('gl.Center', CENTERS);
      const limited = { ...setting, descriptor: { ...setting.descriptor, limits: { ...setting.descriptor.limits, maxIds: 2 } } };
      expect(requestAt(limited, '/17/centers', [12, 40, 41]).body['parentIds']).toEqual([40, 41]);
    });

    it('read a search tree’s matches with their ancestors, and a list flat', () => {
      const setting = tmSearchTestSetting('gl.Center', CENTERS);
      for (const url of ['/17/centers?q=jed', '/17/centers?f.Code=eq:A', '/17/centers?order=Name']) {
        const request = requestAt(setting, url);
        expect(request.tree, url).toBe(url.endsWith('order=Name') ? 'browse' : 'search');
      }
      const search = requestAt(setting, '/17/centers?q=jed');
      expect(search.route).toBe('centers/query');
      expect(search.body['includeAncestors']).toBe(true);
      const list = requestAt(setting, '/17/centers?q=jed&list=1');
      expect(list.tree).toBeNull();
      expect(list.body['includeAncestors']).toBeUndefined();
      // A picker's fixed leaf makes a search tree.
      const picker = tmSearchTestSetting('gl.Center', CENTERS, { mode: 'picker', fixedLeaves: ['IsActive = true'] });
      const state = tmSearchResolve(picker, {}).state;
      expect(tmSearchRequest(picker, state, tmSearchPlan(picker, state)).tree).toBe('search');
    });

    it('select the tree keys and the tile’s paths beside the shown columns, never as columns', () => {
      const setting = tmSearchTestSetting('gl.Center', CENTERS);
      const state = tmSearchResolve(setting, {}).state;
      const plan = tmSearchPlan(setting, state);
      expect(plan.columns.map((column) => column.key)).toEqual(['Code', 'Name']);
      expect(plan.select).toBe('Id,Code,Name,IsActive,ParentId,SubtreeCount,ActiveSubtreeCount');
    });
  });
});
