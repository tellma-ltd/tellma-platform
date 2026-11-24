// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmSearchQuery, TmSearchSnapshot } from '@tellma/core';
import type { TmFakeTelemetry } from '@tellma/core-testing';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSearchCanonical, tmSearchUrl } from './tm-search-canonical';
import { tmSearchUpdate } from './tm-search-change';
import { TM_SEARCH_EXPRESSION_LABEL, type TmSearchSetting } from './tm-search-model';
import { tmSearchPlan, tmSearchRequest } from './tm-search-request';
import { tmSearchResolve } from './tm-search-resolve';
import {
  TM_TEST_CUSTOMERS,
  TM_TEST_CUSTOMERS_WITH_EXPRESSION,
  tmSearchTestHash,
  tmSearchTestHistory,
  tmSearchTestOpening,
  tmSearchTestPayload,
  tmSearchTestSetting,
} from './tm-search.testing.util';

// Where a search page's query state comes from when it opens: the vectors of
// the precedence (a superseding snapshot, the URL, a `#d=`-only URL, the
// page state, the preferences, the code defaults), the values the entity
// cannot have and how they are reported, the canonical URL's rules, the
// drill-through list's payload and the picker, and a seeded round trip of
// random URLs through their canonical form.

/** A user who reads everything. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true } };

/** A user who reads customers but not currencies. */
const READER: TmEntityRigOptions = { access: { securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] } };

/**
 * A snapshot of a page's query state.
 *
 * @param entity - The entity.
 * @param query - The query state.
 * @param canonical - Its canonical URL.
 * @param supersedes - The canonical URL it replaces.
 * @returns The snapshot.
 */
function snapshot(entity: string, query: TmSearchQuery, canonical: string, supersedes?: string): TmSearchSnapshot {
  return { entity, query, canonical, ...(supersedes && { supersedes }), request: null, result: null, ids: [5, 6], selection: [6], highlight: 5 };
}

/**
 * The `tm.entity.column-dropped` events reported so far.
 *
 * @param telemetry - The fake telemetry.
 * @returns Each event's properties.
 */
function drops(telemetry: TmFakeTelemetry): unknown[] {
  return telemetry
    .recent()
    .filter((item) => item.name === 'tm.entity.column-dropped')
    .map((item) => item.props);
}

describe('search state resolution', () => {
  describe('precedence', () => {
    beforeEach(async () => {
      await tmEntityRig(SYSTEM);
    });

    it('takes the whole query from a URL that carries any of it, its absent fields from the preferences, and the selection from the page state', () => {
      const setting = tmSearchTestSetting('gl.Center', {}, { preferences: { columns: ['Code', 'Name', 'Parent'], view: {}, pageSize: 100 } });
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers?f.CenterType=in:Service', snapshot('gl.Center', { q: 'jed' }, '/17/centers?q=jed')));
      expect(resolved.source).toBe('url');
      // The selection restores from the page's snapshot whatever the query's source.
      expect(resolved.snapshot?.selection).toEqual([6]);
      expect(resolved.state.query).toEqual({ columnFilters: { CenterType: 'in:Service' }, cols: ['Code', 'Name', 'Parent'], size: 100 });
      expect(resolved.state.columns).toEqual(['Code', 'Name', 'Parent']);
      expect(resolved.state.columnsFrom).toBe('preference');
      expect(resolved.canonical).toBe('/17/centers?f.CenterType=in:Service&cols=Code,Name,Parent&size=100');
    });

    it('takes a colleague’s link exactly, whatever the reader’s preference', () => {
      const setting = tmSearchTestSetting('gl.Center', {}, { preferences: { columns: ['Name'], view: {}, pageSize: 50 } });
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers?q=jed&cols=Code,Name,Parent&page=2'));
      expect(resolved.state.query).toEqual({ q: 'jed', cols: ['Code', 'Name', 'Parent'], page: 2 });
      expect(resolved.state.columnsFrom).toBe('url');
      expect(resolved.canonical).toBe('/17/centers?q=jed&cols=Code,Name,Parent&page=2');
    });

    it('reads a URL value at its code default as left out, so the preference applies', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, {
        preferences: { columns: ['Code', 'Name'], view: { wide: 'tiles' }, pageSize: 100 },
      });
      const url = '/17/customers?q=a&size=50&view=grid&cols=Code,Name,CustomerType,Currency,CreditLimit,IsActive';
      const resolved = tmSearchResolve(setting, tmSearchTestOpening(url));
      expect(resolved.state.query).toEqual({ q: 'a', cols: ['Code', 'Name'], size: 100, view: 'tiles' });
      expect(resolved.state.columnsFrom).toBe('preference');
    });

    it('applies the page state’s snapshot whole when the URL carries no query state', () => {
      const setting = tmSearchTestSetting('gl.Center', {}, { preferences: { columns: null, view: {}, pageSize: 100 } });
      const saved = snapshot('gl.Center', { q: 'jed', page: 2 }, '/17/centers?q=jed&page=2');
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers?clone=5', saved));
      expect(resolved.source).toBe('page-state');
      expect(resolved.snapshot).toBe(saved);
      // The snapshot's page size is its code default, never the preference.
      expect(resolved.state.query).toEqual({ q: 'jed', page: 2 });
      expect(resolved.canonical).toBe('/17/centers?q=jed&page=2');
    });

    it('applies the view preference to a snapshot taken at another width, as a resize does', () => {
      const setting = tmSearchTestSetting('gl.Center', {}, { width: 'narrow', preferences: { columns: null, view: { narrow: 'grid' }, pageSize: 50 } });
      // Taken on a wide window, where grid was the default and left the query.
      const saved = snapshot('gl.Center', { q: 'jed' }, '/17/centers?q=jed');
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers', saved));
      expect(resolved.source).toBe('page-state');
      expect(resolved.state.query).toEqual({ q: 'jed', view: 'grid' });
      expect(resolved.canonical).toBe('/17/centers?q=jed&view=grid');
    });

    it('skips the page state on Back and Forward, so Back to a bare URL reaches the first page', () => {
      const setting = tmSearchTestSetting('gl.Center');
      // Page 2 pushed `?page=2`, and a row opened from it left this snapshot.
      const saved = snapshot('gl.Center', { page: 2 }, '/17/centers?page=2');
      const back = tmSearchResolve(setting, { ...tmSearchTestOpening('/17/centers', saved), navigation: 'history' });
      expect(back.source).toBe('defaults');
      expect(back.state.query).toEqual({});
      expect(back.canonical).toBe('/17/centers');
      expect(back.snapshot).toBe(saved);
      // A fresh opening of the same URL still takes the page state.
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/centers', saved)).state.query).toEqual({ page: 2 });
      // A snapshot that supersedes the URL still applies.
      const moved = snapshot('gl.Center', { page: 3 }, '/17/centers?page=3', '/17/centers?page=2');
      const superseded = tmSearchResolve(setting, { ...tmSearchTestOpening('/17/centers?page=2', moved), navigation: 'history' });
      expect(superseded.source).toBe('supersedes');
      expect(superseded.state.query).toEqual({ page: 3 });
    });

    it('applies a snapshot that supersedes the URL’s canonical query, and replaces the URL', () => {
      const setting = tmSearchTestSetting('gl.Center');
      const saved = snapshot('gl.Center', { q: 'jed', page: 3 }, '/17/centers?q=jed&page=3', '/17/centers?q=jed&page=2');
      // The URL is the superseded state, its parameters in another order.
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers?page=2&q=jed', saved));
      expect(resolved.source).toBe('supersedes');
      expect(resolved.snapshot).toBe(saved);
      expect(resolved.state.query).toEqual({ q: 'jed', page: 3 });
      expect(resolved.canonical).toBe('/17/centers?q=jed&page=3');
      // Another page's URL is not superseded.
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/centers?q=jed&page=4', saved)).source).toBe('url');
    });

    it('takes a URL that carries only a `#d=` filter as the whole query', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const hash = tmSearchTestHash('CreditLimit > 100000');
      const resolved = tmSearchResolve(setting, tmSearchTestOpening(`/17/customers${hash}`, snapshot('acme.Customer', { q: 'al' }, '/17/customers?q=al')));
      expect(resolved.source).toBe('url');
      expect(resolved.state.query).toEqual({ filter: 'CreditLimit > 100000' });
      expect(resolved.canonical).toBe(`/17/customers${hash}`);
    });

    it('takes the preferences, then the code defaults, when nothing else applies', () => {
      const preferred = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, {
        preferences: { columns: ['Code', 'Name'], view: { wide: 'tiles' }, pageSize: 100 },
      });
      const resolved = tmSearchResolve(preferred, tmSearchTestOpening('/17/customers'));
      expect(resolved.source).toBe('defaults');
      expect(resolved.state.query).toEqual({ cols: ['Code', 'Name'], size: 100, view: 'tiles' });
      expect(resolved.canonical).toBe('/17/customers?cols=Code,Name&size=100&view=tiles');

      const plain = tmSearchResolve(tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS), tmSearchTestOpening('/17/customers'));
      expect(plain.state.query).toEqual({});
      expect(plain.state.columns).toEqual(TM_TEST_CUSTOMERS.columns);
      expect(plain.state.columnsFrom).toBe('declaration');
      expect(plain.canonical).toBe('/17/customers');
    });

    it('ignores another entity’s snapshot', () => {
      const resolved = tmSearchResolve(tmSearchTestSetting('gl.Center'), tmSearchTestOpening('/17/centers', snapshot('acme.Customer', { q: 'al' }, '/17/customers?q=al')));
      expect(resolved.source).toBe('defaults');
      expect(resolved.state.query).toEqual({});
    });

    it('opens a picker with its text and the code default columns, without a URL', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, {
        mode: 'picker',
        preferences: { columns: ['Code'], view: {}, pageSize: 20 },
      });
      const resolved = tmSearchResolve(setting, { text: '  al ' });
      expect(resolved.state.query).toEqual({ q: 'al', size: 20 });
      expect(resolved.state.columns).toEqual(TM_TEST_CUSTOMERS.columns);
      expect(resolved.canonical).toBe('');
    });
  });

  describe('drops', () => {
    it('drops the values the entity cannot have, and rewrites the URL', async () => {
      await tmEntityRig(SYSTEM);
      // Not activatable, not a tree.
      const extras = tmSearchResolve(tmSearchTestSetting('acme.CustomerExtra'), tmSearchTestOpening('/17/customer-extras?inactive=1&list=1&q=x'));
      expect(extras.state.query).toEqual({ q: 'x' });
      expect(extras.canonical).toBe('/17/customer-extras?q=x');

      // An unknown tab, custom key, option value and switch value.
      const customers = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const unknown = tmSearchResolve(customers, tmSearchTestOpening('/17/customers?tab=nope&x.nope=1&x.credit=low&x.mine=yes&x.shared=1'));
      expect(unknown.state.query).toEqual({ custom: { shared: '1' } });
      expect(unknown.canonical).toBe('/17/customers?x.shared=1');

      // The first tab is the default and leaves the URL.
      expect(tmSearchResolve(customers, tmSearchTestOpening('/17/customers?tab=all')).canonical).toBe('/17/customers');
      expect(tmSearchResolve(customers, tmSearchTestOpening('/17/customers?tab=open')).canonical).toBe('/17/customers?tab=open');
    });

    it('drops a column key that does not resolve from `cols`, `order` and `f.`, and reports it under the URL', async () => {
      const { telemetry } = await tmEntityRig(SYSTEM);
      const setting = tmSearchTestSetting('gl.Center');
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers?f.Nope=eq:1&order=Nope:desc&cols=Code,Nope,Name'));
      expect(resolved.state.query).toEqual({ cols: ['Code', 'Name'] });
      expect(resolved.canonical).toBe('/17/centers?cols=Code,Name');
      expect(drops(telemetry)).toEqual([
        { reason: 'unknown', source: 'url' },
        { reason: 'unknown', source: 'url' },
        { reason: 'unknown', source: 'url' },
      ]);
    });

    it('reports a bad preference column once when the page state applies', async () => {
      const { telemetry } = await tmEntityRig(SYSTEM);
      const setting = tmSearchTestSetting('gl.Center', {}, { preferences: { columns: ['Code', 'Nope'], view: {}, pageSize: 50 } });
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers', snapshot('gl.Center', { q: 'jed' }, '/17/centers?q=jed')));
      expect(resolved.state.columns).toEqual(['Code']);
      expect(drops(telemetry)).toEqual([{ reason: 'unknown', source: 'preference' }]);
    });

    it('drops a column key the user may not read', async () => {
      const { telemetry } = await tmEntityRig(READER);
      const setting = tmSearchTestSetting('acme.Customer');
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/customers?f.Currency.Name=contains:usd&order=Currency.Name&cols=Code,Currency.Name'));
      expect(resolved.state.query).toEqual({ cols: ['Code'] });
      expect(drops(telemetry)).toEqual([
        { reason: 'unreachable', source: 'url' },
        { reason: 'unreachable', source: 'url' },
        { reason: 'unreachable', source: 'url' },
      ]);
    });

    it('drops an `f.` value its control rejects and an order on a column that cannot order, as invalid values', async () => {
      const { telemetry } = await tmEntityRig(SYSTEM);
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/customers?f.CustomerType=in:Retired&f.CreditLimit=gt:abc&f.Image=in:1&f.Code=eq:A1&order=Image'));
      expect(resolved.state.query).toEqual({ columnFilters: { Code: 'eq:A1' } });
      expect(drops(telemetry)).toEqual([
        { reason: 'invalid-value', source: 'url' },
        { reason: 'invalid-value', source: 'url' },
        { reason: 'invalid-value', source: 'url' },
        { reason: 'invalid-value', source: 'url' },
      ]);
    });

    it('writes column keys in their canonical case, and one filter per column', async () => {
      await tmEntityRig(SYSTEM);
      const setting = tmSearchTestSetting('gl.Center');
      const resolved = tmSearchResolve(setting, tmSearchTestOpening('/17/centers?f.code=eq:A&f.CODE=eq:B&order=parent:desc&cols=code,parent.code'));
      expect(resolved.state.query).toEqual({ columnFilters: { Code: 'eq:B' }, order: 'Parent:desc', cols: ['Code', 'Parent.Code'] });
      expect(resolved.canonical).toBe('/17/centers?f.Code=eq:B&order=Parent:desc&cols=Code,Parent.Code');
    });

    it('drops a page size the page does not offer and a page beyond the skip window, and cuts the text', async () => {
      await tmEntityRig(SYSTEM);
      const setting = tmSearchTestSetting('gl.Center');
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/centers?size=30&page=2')).state.query).toEqual({ page: 2 });
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/centers?size=200&page=500')).state.query).toEqual({ page: 500, size: 200 });
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/centers?size=200&page=501')).state.query).toEqual({ size: 200 });
      const long = `${'a'.repeat(199)}😀b`;
      expect(tmSearchResolve(setting, tmSearchTestOpening(`/17/centers?q=${encodeURIComponent(`  ${long}`)}`)).state.query.q).toBe('a'.repeat(199));
    });
  });

  describe('canonical URL', () => {
    beforeEach(async () => {
      await tmEntityRig(SYSTEM);
    });

    it('leaves out the columns at their code default or with an expression column', () => {
      const declared = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const url = (setting: TmSearchSetting, cols: string): string => tmSearchResolve(setting, tmSearchTestOpening(`/17/customers?q=a&cols=${cols}`)).canonical;
      expect(url(declared, 'code,name,customertype,currency,creditlimit,isactive')).toBe('/17/customers?q=a');
      expect(url(declared, 'Code,Name')).toBe('/17/customers?q=a&cols=Code,Name');

      // A preference with an expression column never travels.
      const expression = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS_WITH_EXPRESSION, {
        preferences: { columns: ['Code', { expression: 'CreditLimit * 3' }], view: {}, pageSize: 50 },
      });
      const resolved = tmSearchResolve(expression, tmSearchTestOpening('/17/customers?q=a'));
      expect(resolved.state.columns).toEqual(['Code', { expression: 'CreditLimit * 3', labelKey: TM_SEARCH_EXPRESSION_LABEL }]);
      expect(resolved.canonical).toBe('/17/customers?q=a');
    });

    it('writes the view only when it differs from the width default', () => {
      const wide = tmSearchTestSetting('gl.Center');
      const narrow = tmSearchTestSetting('gl.Center', {}, { width: 'narrow' });
      expect(tmSearchResolve(wide, tmSearchTestOpening('/17/centers?view=grid')).canonical).toBe('/17/centers');
      expect(tmSearchResolve(wide, tmSearchTestOpening('/17/centers?view=tiles')).canonical).toBe('/17/centers?view=tiles');
      expect(tmSearchResolve(narrow, tmSearchTestOpening('/17/centers?view=tiles')).canonical).toBe('/17/centers');
      expect(tmSearchResolve(narrow, tmSearchTestOpening('/17/centers?view=grid')).canonical).toBe('/17/centers?view=grid');
      // The current class's preference half names the view.
      const preferred = tmSearchTestSetting('gl.Center', {}, { width: 'narrow', preferences: { columns: null, view: { narrow: 'grid', wide: 'tiles' }, pageSize: 50 } });
      expect(tmSearchResolve(preferred, tmSearchTestOpening('/17/centers')).canonical).toBe('/17/centers?view=grid');
    });

    it('keeps an explicit order, even on the default order’s column', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/customers?order=Code')).canonical).toBe('/17/customers?order=Code');
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/customers')).canonical).toBe('/17/customers');
    });

    it('takes the list choice from the preference on a tree', () => {
      const setting = tmSearchTestSetting('gl.Center', {}, { preferences: { columns: null, view: { list: true }, pageSize: 50 } });
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/centers')).canonical).toBe('/17/centers?list=1');
      // A URL that leaves the list out takes the preference's, as `list=0` does.
      expect(tmSearchResolve(setting, tmSearchTestOpening('/17/centers?q=a&list=0')).canonical).toBe('/17/centers?q=a&list=1');
    });
  });

  describe('drill-through list', () => {
    beforeEach(async () => {
      await tmEntityRig(SYSTEM);
    });

    /** Customers' drill-through list. */
    const list = (): TmSearchSetting => tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { mode: 'list', segment: 'customers' });

    it('keys two lists apart by their payloads', () => {
      const a = tmSearchResolve(list(), tmSearchTestOpening(`/17/list/customers?q=al${tmSearchTestPayload({ filter: 'CreditLimit > 0' })}`));
      const b = tmSearchResolve(list(), tmSearchTestOpening(`/17/list/customers?q=al${tmSearchTestPayload({ filter: 'CreditLimit > 1' })}`));
      expect(a.state.listFilter).toBe('CreditLimit > 0');
      expect(b.state.listFilter).toBe('CreditLimit > 1');
      expect(a.canonical).not.toBe(b.canonical);
      expect(a.canonical).toBe(`/17/list/customers?q=al${tmSearchTestPayload({ filter: 'CreditLimit > 0' })}`);
    });

    it('reads its columns from the payload, and drops the filter expression and Active only', () => {
      const payload = tmSearchTestPayload({ filter: 'CreditLimit > 0', cols: ['Code', 'CreditLimit'] });
      const resolved = tmSearchResolve(list(), tmSearchTestOpening(`/17/list/customers?inactive=1&cols=Name${payload}`));
      expect(resolved.source).toBe('url');
      expect(resolved.state.columns).toEqual(['Code', { path: 'CreditLimit', width: 140 }]);
      expect(resolved.state.columnsFrom).toBe('url');
      expect(resolved.state.query).toEqual({ cols: ['Code', 'CreditLimit'] });
      expect(resolved.canonical).toBe(`/17/list/customers${payload}`);
    });

    it('never takes the column preference', () => {
      const setting = { ...list(), preferences: { columns: ['Code'], view: {}, pageSize: 50 } };
      const resolved = tmSearchResolve(setting, tmSearchTestOpening(`/17/list/customers${tmSearchTestPayload({ filter: null })}`));
      expect(resolved.state.columnsFrom).toBe('declaration');
      expect(resolved.state.listFilter).toBeNull();
    });

    it('applies a snapshot under the same fixed filter only', () => {
      const payload = tmSearchTestPayload({ filter: 'CreditLimit > 0' });
      const saved = snapshot('acme.Customer', { q: 'al' }, `/17/list/customers?q=al${payload}`);
      expect(tmSearchResolve(list(), tmSearchTestOpening(`/17/list/customers${payload}`, saved)).source).toBe('page-state');
      // The selection restores from it whatever the query's source.
      expect(tmSearchResolve(list(), tmSearchTestOpening(`/17/list/customers?q=x${payload}`, saved)).snapshot).toBe(saved);
      const other = tmSearchTestPayload({ filter: 'CreditLimit > 1' });
      const elsewhere = tmSearchResolve(list(), tmSearchTestOpening(`/17/list/customers${other}`, saved));
      expect(elsewhere.source).toBe('defaults');
      expect(elsewhere.snapshot).toBeNull();
    });

    it('writes no URL and reads nothing for a missing or malformed payload', () => {
      const saved = snapshot('acme.Customer', { q: 'al' }, `/17/list/customers?q=al${tmSearchTestPayload({ filter: null })}`);
      for (const url of ['/17/list/customers#d=!!', '/17/list/customers?q=al']) {
        const resolved = tmSearchResolve(list(), tmSearchTestOpening(url, saved));
        expect(resolved.payloadInvalid, url).toBe(true);
        expect(resolved.state.payloadInvalid, url).toBe(true);
        // The address bar keeps the link as it came, never an unfiltered list's URL.
        expect(resolved.canonical, url).toBe('');
        expect(resolved.snapshot, url).toBeNull();
        expect(() => tmSearchRequest(list(), resolved.state, tmSearchPlan(list(), resolved.state)), url).toThrow();
        const change = tmSearchUpdate(list(), resolved.state, { q: 'x' });
        expect(change.canonical, url).toBe('');
        expect(change.history, url).toBeNull();
      }
    });
  });

  describe('round trip', () => {
    it('resolves any URL to a canonical URL that resolves to itself', async () => {
      await tmEntityRig(SYSTEM);
      // A seeded generator, so a failure reproduces.
      let seed = 20261124;
      const random = (): number => {
        seed = (seed * 1103515245 + 12345) % 2147483648;
        return seed / 2147483648;
      };
      const pick = <T>(values: readonly T[]): T => values[Math.floor(random() * values.length)]!;
      const parameters: readonly (() => string)[] = [
        () => `tab=${pick(['all', 'open', 'starred', 'nope', ''])}`,
        () => `q=${encodeURIComponent(pick(['al', ' jed ', 'a b', '‏x', '%', '']))}`,
        () => `f.${pick(['Code', 'code', 'CustomerType', 'CreditLimit', 'Nope', 'Image'])}=${pick(['eq:A1', 'in:Retail', 'between:1,5', 'gt:x', 'contains:a,b'])}`,
        () => `inactive=${pick(['1', '0', 'yes'])}`,
        () => `x.${pick(['mine', 'credit', 'shared', 'nope'])}=${pick(['1', 'high', 'none', 'x'])}`,
        () => `order=${pick(['Code', 'name:desc', 'Currency', 'Image', 'Nope'])}`,
        () => `cols=${pick(['Code,Name', 'code,name,customertype,currency,creditlimit,isactive', 'Name,Nope', 'Currency.Code'])}`,
        () => `page=${pick(['1', '2', '0', '2001', 'x'])}`,
        () => `size=${pick(['20', '50', '100', '30'])}`,
        () => `view=${pick(['grid', 'tiles', 'list'])}`,
        () => `list=${pick(['1', '0'])}`,
      ];
      for (let run = 0; run < 300; run++) {
        const width = pick(['narrow', 'wide'] as const);
        // Customers as declared, or centers, a tree without a declaration.
        const [entity, declaration] = pick([['acme.Customer', TM_TEST_CUSTOMERS], ['gl.Center', {}]] as const);
        const setting = tmSearchTestSetting(entity, declaration, {
          width,
          preferences: {
            columns: pick([null, ['Code', 'Name', 'Rank'], ['Code', { expression: 'CreditLimit * 3' }]]),
            view: pick([{}, { narrow: 'grid' as const }, { wide: 'tiles' as const }, { list: true }]),
            pageSize: pick([50, 100]),
          },
        });
        const query = Array.from({ length: 1 + Math.floor(random() * 6) }, () => pick(parameters)()).join('&');
        const hash = random() < 0.3 ? tmSearchTestHash(pick(['CreditLimit > 0', 'Code = "A"'])) : '';
        const first = tmSearchResolve(setting, tmSearchTestOpening(`/17/customers?${query}${hash}`));
        const second = tmSearchResolve(setting, tmSearchTestOpening(first.canonical));
        expect(second.canonical, `/17/customers?${query}${hash}`).toBe(first.canonical);
        // Columns the preference showed travel as `cols`, so only their source may differ.
        expect({ ...second.state, columnsFrom: null }, `/17/customers?${query}${hash}`).toEqual({ ...first.state, columnsFrom: null });
        // The canonical query is a fixed point of its own canonical form.
        expect(tmSearchCanonical(setting, first.state.query, first.state.columns)).toEqual(first.state.query);
        expect(tmSearchUrl(setting, first.state)).toBe(first.canonical);
        // A change from it reaches history by its verdict: none when the URL is unchanged, a push for a page alone.
        const patch = pick<Partial<TmSearchQuery>>([{ page: 2 }, { page: 1 }, { q: 'zz' }, { size: 20 }, { list: true }, { list: undefined }]);
        const change = tmSearchUpdate(setting, first.state, patch);
        const pageOnly = Object.keys(patch).join() === 'page';
        expect(change.history, `/17/customers?${query}${hash} ${JSON.stringify(patch)}`).toBe(tmSearchTestHistory(first.canonical, change.canonical, pageOnly));
      }
    });
  });
});
