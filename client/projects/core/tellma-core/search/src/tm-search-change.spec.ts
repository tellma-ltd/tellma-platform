// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmColumnInput, TmSearchQuery } from '@tellma/core';
import type { TmFakeTelemetry } from '@tellma/core-testing';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmSearchApply, tmSearchResize, tmSearchSetColumns, tmSearchUpdate, type TmSearchTransition } from './tm-search-change';
import { TM_SEARCH_EXPRESSION_LABEL, type TmSearchDeclaration, type TmSearchSetting, type TmSearchState } from './tm-search-model';
import { tmSearchPlan, tmSearchRequest } from './tm-search-request';
import { tmSearchResolve } from './tm-search-resolve';
import {
  TM_TEST_CUSTOMERS,
  TM_TEST_CUSTOMERS_WITH_EXPRESSION,
  tmSearchTestHistory,
  tmSearchTestOpening,
  tmSearchTestPayload,
  tmSearchTestSetting,
} from './tm-search.testing.util';

// How each query change reaches the URL, the history and the preferences:
// only a page change pushes; the text, a filter, the tab, Active only, a
// custom filter, Clear all and the order go back to the first page; the
// columns and the view keep the page; tree and list go back to the first;
// a page-size change keeps the first visible row.

/**
 * A page's state, resolved from its URL.
 *
 * @param setting - The page's setting.
 * @param url - The URL.
 * @returns The state.
 */
function opened(setting: TmSearchSetting, url: string): TmSearchState {
  return tmSearchResolve(setting, tmSearchTestOpening(url)).state;
}

describe('search changes', () => {
  /** The rig's telemetry. */
  let telemetry: TmFakeTelemetry;

  beforeEach(async () => {
    ({ telemetry } = await tmEntityRig({ access: { isSystem: true } }));
  });

  /** The `tm.entity.column-dropped` events reported so far, each event's properties. */
  const drops = (): unknown[] =>
    telemetry
      .recent()
      .filter((item) => item.name === 'tm.entity.column-dropped')
      .map((item) => item.props);

  describe('history and paging', () => {
    it('goes back to the first page, with a replace, on a change of the text, a filter, the tab, Active only, a custom filter or the order', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const state = opened(setting, '/17/customers?q=al&page=3');
      const patches = [
        { q: 'alx' },
        { tab: 'open' },
        { columnFilters: { Code: 'eq:A1' } },
        { filter: 'CreditLimit > 0' },
        { inactive: true },
        { custom: { mine: '1' } },
        { order: 'Name:desc' },
      ];
      for (const patch of patches) {
        const change = tmSearchUpdate(setting, state, patch);
        expect(change.state.query.page, JSON.stringify(patch)).toBeUndefined();
        expect(change.history, JSON.stringify(patch)).toBe('replace');
        expect(change.preferences, JSON.stringify(patch)).toEqual({});
      }
      expect(tmSearchUpdate(setting, state, { q: 'alx' }).canonical).toBe('/17/customers?q=alx');
    });

    it('clears every filter at once, back to the first page', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const state = opened(setting, '/17/customers?q=al&f.Code=eq:A1&inactive=1&x.mine=1&page=2');
      const change = tmSearchUpdate(setting, state, { columnFilters: undefined, filter: undefined, inactive: undefined, custom: undefined });
      expect(change.state.query).toEqual({ q: 'al' });
      expect(change.history).toBe('replace');
    });

    it('pushes a page change alone, and does nothing when nothing changes', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const state = opened(setting, '/17/customers?q=al&page=3');
      const next = tmSearchUpdate(setting, state, { page: 4 });
      expect(next.history).toBe('push');
      expect(next.canonical).toBe('/17/customers?q=al&page=4');
      expect(tmSearchUpdate(setting, state, { page: 3 }).history).toBeNull();
      expect(tmSearchUpdate(setting, state, { q: ' al ' }).history).toBeNull();
      // A patch that names the page with another field sets it, with a replace.
      const both = tmSearchUpdate(setting, state, { q: 'alx', page: 2 });
      expect(both.state.query).toEqual({ q: 'alx', page: 2 });
      expect(both.history).toBe('replace');
    });

    it('keeps the first visible row on a page-size change, and writes the preference', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const change = tmSearchUpdate(setting, opened(setting, '/17/customers?page=3'), { size: 20 });
      expect(change.state.query).toEqual({ page: 6, size: 20 });
      expect(change.history).toBe('replace');
      expect(change.preferences).toEqual({ pageSize: 20 });
      // Back to the default size: page 6 at 20 (rows 101-120) is page 3 at 50.
      const back = tmSearchUpdate(setting, change.state, { size: undefined });
      expect(back.state.query).toEqual({ page: 3 });
      expect(back.preferences).toEqual({ pageSize: 50 });
    });

    it('keeps the page on a view change, and writes the width class’s half', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { preferences: { columns: null, view: { narrow: 'grid' }, pageSize: 50 } });
      const state = opened(setting, '/17/customers?page=3');
      const tiles = tmSearchUpdate(setting, state, { view: 'tiles' });
      expect(tiles.state.query).toEqual({ page: 3, view: 'tiles' });
      expect(tiles.history).toBe('replace');
      expect(tiles.preferences).toEqual({ view: { narrow: 'grid', wide: 'tiles' } });
      // A toggle back to the class default deletes its half and keeps the other.
      const grid = tmSearchUpdate({ ...setting, preferences: { ...setting.preferences, view: { narrow: 'grid', wide: 'tiles' } } }, tiles.state, { view: 'grid' });
      expect(grid.state.query).toEqual({ page: 3 });
      expect(grid.preferences).toEqual({ view: { narrow: 'grid' } });
    });

    it('leaves the narrow half alone through a desktop peek and a toggle there', () => {
      const preferences = { columns: null, view: { narrow: 'grid' as const }, pageSize: 50 };
      const narrow = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { width: 'narrow', preferences });
      const wide = { ...narrow, width: 'wide' as const };
      // The peek shows the wide default and writes nothing.
      const peek = tmSearchResize(wide, opened(narrow, '/17/customers?view=grid'), 'narrow');
      expect(peek.state.query).toEqual({});
      expect(peek.preferences).toEqual({});
      // A toggle at the wide width writes the wide half only.
      const tiles = tmSearchUpdate(wide, peek.state, { view: 'tiles' });
      expect(tiles.preferences).toEqual({ view: { narrow: 'grid', wide: 'tiles' } });
      // Back to the narrow width, the narrow half still names its view.
      const back = tmSearchResize({ ...narrow, preferences: { ...preferences, view: { narrow: 'grid', wide: 'tiles' } } }, tiles.state, 'wide');
      expect(back.state.query).toEqual({ view: 'grid' });
    });

    it('goes back to the first page between tree and list, and writes the list choice', () => {
      const setting = tmSearchTestSetting('gl.Center', {}, { preferences: { columns: null, view: { wide: 'tiles' }, pageSize: 50 } });
      const state = opened(setting, '/17/centers?q=a&page=2');
      const list = tmSearchUpdate(setting, state, { list: true });
      expect(list.state.query).toEqual({ q: 'a', view: 'tiles', list: true });
      expect(list.preferences).toEqual({ view: { wide: 'tiles', list: true } });
      const tree = tmSearchUpdate({ ...setting, preferences: { ...setting.preferences, view: { wide: 'tiles', list: true } } }, { ...list.state, query: { ...list.state.query, page: 2 } }, { list: undefined });
      expect(tree.state.query).toEqual({ q: 'a', view: 'tiles' });
      expect(tree.preferences).toEqual({ view: { wide: 'tiles' } });
    });

    it('applies an anchor’s whole query with a replace and no paging rule', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const typed = opened(setting, '/17/customers?q=alx');
      const restored = tmSearchApply(setting, typed, { q: 'al', page: 3 });
      expect(restored.state.query).toEqual({ q: 'al', page: 3 });
      expect(restored.history).toBe('replace');
      expect(restored.preferences).toEqual({});
    });
  });

  describe('columns', () => {
    it('keep the page, replace the URL and write the preference, deleted at the code default', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const state = opened(setting, '/17/customers?page=3');
      const changed = tmSearchSetColumns(setting, state, ['code', 'Name']);
      expect(changed.state.query).toEqual({ cols: ['Code', 'Name'], page: 3 });
      expect(changed.state.columnsFrom).toBe('preference');
      expect(changed.history).toBe('replace');
      expect(changed.preferences).toEqual({ columns: ['Code', 'Name'] });

      const reset = tmSearchSetColumns(setting, changed.state, TM_TEST_CUSTOMERS.columns!);
      expect(reset.state.query).toEqual({ page: 3 });
      expect(reset.state.columnsFrom).toBe('declaration');
      expect(reset.preferences).toEqual({ columns: null });
      // The same change through the query's `cols`.
      expect(tmSearchUpdate(setting, state, { cols: ['Code', 'Name'] }).state).toEqual(changed.state);
      expect(tmSearchUpdate(setting, changed.state, { cols: undefined }).state).toEqual(reset.state);
    });

    it('never put an expression column in the URL', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS_WITH_EXPRESSION);
      const state = opened(setting, '/17/customers?q=a');
      const changed = tmSearchSetColumns(setting, state, ['Code', { expression: 'CreditLimit * 3', labelKey: 'tm.search.expressionColumn' }]);
      expect(changed.state.query).toEqual({ q: 'a' });
      expect(changed.history).toBeNull();
      expect(changed.preferences).toEqual({ columns: ['Code', { expression: 'CreditLimit * 3' }] });
      // Removing the expression puts the path columns in the URL.
      const paths = tmSearchSetColumns(setting, changed.state, ['Code']);
      expect(paths.canonical).toBe('/17/customers?q=a&cols=Code');
      expect(paths.history).toBe('replace');
    });

    it('keep a drill-through list’s columns in its payload, never in the preference', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { mode: 'list', segment: 'customers' });
      const state = opened(setting, `/17/list/customers${tmSearchTestPayload({ filter: 'CreditLimit > 0' })}`);
      const changed = tmSearchSetColumns(setting, state, ['Code', 'Name']);
      expect(changed.canonical).toBe(`/17/list/customers${tmSearchTestPayload({ filter: 'CreditLimit > 0', cols: ['Code', 'Name'] })}`);
      expect(changed.state.columnsFrom).toBe('url');
      expect(changed.history).toBe('replace');
      expect(changed.preferences).toEqual({});
    });
  });

  describe('drops', () => {
    it('report each value a change drops once, and the request reports nothing more', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS);
      const state = opened(setting, '/17/customers?page=2');
      const change = tmSearchUpdate(setting, state, { columnFilters: { CreditLimit: 'gt:abc', Code: 'eq:A' }, order: 'Nope' });
      expect(change.state.query).toEqual({ columnFilters: { Code: 'eq:A' } });
      expect(drops()).toEqual([
        { reason: 'invalid-value', source: 'url' },
        { reason: 'unknown', source: 'url' },
      ]);
      tmSearchRequest(setting, change.state, tmSearchPlan(setting, change.state));
      expect(drops()).toHaveLength(2);
    });

    it('drop an order on an expression column with a reason of its own', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS_WITH_EXPRESSION);
      const change = tmSearchUpdate(setting, opened(setting, '/17/customers?q=a'), { order: 'CreditLimit * 2:desc' });
      expect(change.state.query).toEqual({ q: 'a' });
      expect(change.history).toBeNull();
      expect(drops()).toEqual([{ reason: 'expression', source: 'url' }]);
    });

    it('report a tile path that does not resolve under the declaration, whatever the columns’ source', () => {
      const declaration: TmSearchDeclaration = { ...TM_TEST_CUSTOMERS, tile: { title: 'Nope' } };
      const setting = tmSearchTestSetting('acme.Customer', declaration);
      const state = opened(setting, '/17/customers?cols=Code,Name');
      expect(state.columnsFrom).toBe('url');
      expect(drops()).toEqual([]);
      tmSearchPlan(setting, state);
      expect(drops()).toEqual([{ reason: 'unknown', source: 'declaration' }]);
    });
  });

  describe('picker and resize', () => {
    it('never touch history in a picker', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { mode: 'picker' });
      const state = tmSearchResolve(setting, { text: 'al' }).state;
      expect(tmSearchUpdate(setting, state, { page: 2 }).history).toBeNull();
      const size = tmSearchUpdate(setting, state, { size: 100 });
      expect(size.history).toBeNull();
      expect(size.canonical).toBe('');
      expect(size.preferences).toEqual({ pageSize: 100 });
    });

    it('recomputes the view when a resize crosses `md`', () => {
      const wide = tmSearchTestSetting('gl.Center');
      const narrow = tmSearchTestSetting('gl.Center', {}, { width: 'narrow' });
      // The width default on both sides: nothing to write.
      const plain = tmSearchResize(narrow, opened(wide, '/17/centers?q=a'), 'wide');
      expect(plain.state.query).toEqual({ q: 'a' });
      expect(plain.history).toBeNull();
      // The URL's tiles on a wide page is the narrow default.
      const tiles = tmSearchResize(narrow, opened(wide, '/17/centers?q=a&view=tiles'), 'wide');
      expect(tiles.state.query).toEqual({ q: 'a' });
      expect(tiles.history).toBe('replace');
      expect(tiles.canonical).toBe('/17/centers?q=a');
      // The new class's preference half names the view.
      const preferred = { ...narrow, preferences: { columns: null, view: { narrow: 'grid' as const }, pageSize: 50 } };
      const grid = tmSearchResize(preferred, opened(wide, '/17/centers?q=a'), 'wide');
      expect(grid.canonical).toBe('/17/centers?q=a&view=grid');
      expect(grid.history).toBe('replace');
      expect(grid.preferences).toEqual({});
      // No class crossed: nothing changes.
      expect(tmSearchResize(wide, opened(wide, '/17/centers?view=tiles'), 'wide').history).toBeNull();
    });
  });

  describe('round trip', () => {
    it('reaches history by its verdict and leaves a URL and preferences that reopen the page as it shows, after any run of changes', () => {
      // A seeded generator, so a failure reproduces.
      let seed = 1124;
      const random = (): number => {
        seed = (seed * 1103515245 + 12345) % 2147483648;
        return seed / 2147483648;
      };
      const pick = <T>(values: readonly T[]): T => values[Math.floor(random() * values.length)]!;
      const patches: readonly (() => Partial<TmSearchQuery>)[] = [
        () => ({ q: pick(['al', ' jed ', '', 'x y']) }),
        () => ({ tab: pick(['all', 'open', 'starred', undefined]) }),
        () => ({ columnFilters: pick<Readonly<Record<string, string>> | undefined>([undefined, { Code: 'eq:A1' }, { CreditLimit: 'between:1,5', CustomerType: 'in:Retail' }]) }),
        () => ({ filter: pick([undefined, 'CreditLimit > 0']) }),
        () => ({ inactive: pick([true, undefined]) }),
        () => ({ custom: pick<Readonly<Record<string, string>> | undefined>([undefined, { mine: '1' }, { credit: 'high', shared: '1' }]) }),
        () => ({ order: pick([undefined, 'Code', 'Name:desc', 'Currency']) }),
        () => ({ page: pick([1, 2, 7]) }),
        () => ({ size: pick([20, 50, 100, 200]) }),
        () => ({ view: pick(['grid', 'tiles'] as const) }),
        () => ({ cols: pick([undefined, ['Code', 'Rank'], ['Name']]) }),
        () => ({ list: pick([true, false, undefined]) }),
      ];
      const columns: readonly (readonly TmColumnInput<string>[])[] = [
        ['Code', 'Name'],
        TM_TEST_CUSTOMERS.columns!,
        ['Code', { expression: 'CreditLimit * 3', labelKey: TM_SEARCH_EXPRESSION_LABEL }],
      ];
      for (let run = 0; run < 40; run++) {
        // Customers as declared, or centers, a tree without a declaration.
        const [entity, declaration, path] = pick([
          ['acme.Customer', TM_TEST_CUSTOMERS, '/17/customers'],
          ['gl.Center', {}, '/17/centers'],
        ] as const);
        let setting = tmSearchTestSetting(entity, declaration, { width: pick(['narrow', 'wide'] as const) });
        const first = tmSearchResolve(setting, tmSearchTestOpening(path));
        let state = first.state;
        let canonical = first.canonical;
        for (let step = 0; step < 12; step++) {
          // A resize across `md`, a change of the columns, or a patch of the query.
          const roll = random();
          let pageOnly = false;
          let change: TmSearchTransition;
          if (roll < 0.1) {
            const from = setting.width;
            setting = { ...setting, width: from === 'wide' ? 'narrow' : 'wide' };
            change = tmSearchResize(setting, state, from);
          } else if (roll < 0.25) {
            change = tmSearchSetColumns(setting, state, pick(columns));
          } else {
            const patch = pick(patches)();
            pageOnly = Object.keys(patch).join() === 'page';
            change = tmSearchUpdate(setting, state, patch);
          }
          // None when the URL is unchanged, a push for a page change alone, else a replace.
          expect(change.history, `run ${run} step ${step}`).toBe(tmSearchTestHistory(canonical, change.canonical, pageOnly));
          canonical = change.canonical;
          // The host writes the preferences the change names.
          const { columns: shown, view, pageSize } = change.preferences;
          setting = {
            ...setting,
            preferences: {
              columns: shown === undefined ? setting.preferences.columns : shown,
              view: view ?? setting.preferences.view,
              pageSize: pageSize ?? setting.preferences.pageSize,
            },
          };
          state = change.state;
          const reopened = tmSearchResolve(setting, tmSearchTestOpening(change.canonical));
          expect(reopened.canonical, `run ${run} step ${step}`).toBe(change.canonical);
          expect(reopened.state.query, `run ${run} step ${step}`).toEqual(state.query);
          expect(reopened.state.columns, `run ${run} step ${step}`).toEqual(state.columns);
        }
      }
    });
  });
});
