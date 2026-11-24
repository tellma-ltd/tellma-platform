// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmEntities } from '@tellma/core';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import {
  TM_SEARCH_EXPRESSION_LABEL,
  tmSearchDefaultColumns,
  tmSearchEffective,
  tmSearchMaxPage,
  tmSearchPageForSize,
  tmSearchPageSizes,
  tmSearchPathKeys,
  tmSearchSameColumns,
  tmSearchTilePaths,
  tmSearchTreeKeys,
  tmSearchWidthView,
} from './tm-search-model';
import {
  tmSearchColumnsFromPreference,
  tmSearchColumnsKey,
  tmSearchColumnsToPreference,
  tmSearchPageSizeKey,
  tmSearchViewHalf,
  tmSearchViewKey,
  tmSearchViewList,
} from './tm-search-preferences';
import { TM_TEST_CUSTOMERS, TM_TEST_CUSTOMERS_WITH_EXPRESSION, tmSearchTestSetting } from './tm-search.testing.util';

// The search model's derived facts over the recorded entity model: the code
// default columns, the tile's and the tree's extra paths, the page sizes and
// the page a size change keeps, and the three preferences' shapes.

describe('search model', () => {
  describe('code default columns', () => {
    beforeEach(async () => {
      await tmEntityRig({ access: { isSystem: true } });
    });

    it('are the declared columns when the declaration has them', () => {
      const customers = TestBed.inject(TmEntities).get('acme.Customer');
      expect(tmSearchDefaultColumns(customers, TM_TEST_CUSTOMERS)).toBe(TM_TEST_CUSTOMERS.columns);
    });

    it('are the display name, the other natural keys and IsActive without a declaration', () => {
      const entities = TestBed.inject(TmEntities);
      // Customers: the prefix Code, then the source Name, then IsActive.
      expect(tmSearchDefaultColumns(entities.get('acme.Customer'), {})).toEqual(['Code', 'Name', 'IsActive']);
      // Centers: the source Code, which is also the natural key, once.
      expect(tmSearchDefaultColumns(entities.get('gl.Center'), {})).toEqual(['Code', 'IsActive']);
      // Users: the source Name, the natural keys Email and Subject.
      expect(tmSearchDefaultColumns(entities.get('core.User'), {})).toEqual(['Name', 'Email', 'Subject', 'IsActive']);
      // An entity that is not activatable shows no IsActive.
      expect(tmSearchDefaultColumns(entities.get('acme.CustomerExtra'), {})).toEqual(['Id']);
    });

    it('keep the path keys of a list without an expression only', () => {
      expect(tmSearchPathKeys(['Code', { path: 'Name' }])).toEqual(['Code', 'Name']);
      expect(tmSearchPathKeys(TM_TEST_CUSTOMERS_WITH_EXPRESSION.columns!)).toBeNull();
    });

    it('compare path keys ignoring case and expressions by their exact text', () => {
      expect(tmSearchSameColumns(['code', { path: 'NAME' }], ['Code', 'Name'])).toBe(true);
      expect(tmSearchSameColumns(['Code', 'Name'], ['Name', 'Code'])).toBe(false);
      expect(tmSearchSameColumns(['Code'], ['Code', 'Name'])).toBe(false);
      const expression = { expression: 'CreditLimit * 2', labelKey: 'X' };
      expect(tmSearchSameColumns([expression], [{ expression: 'CreditLimit * 2', labelKey: 'Y' }])).toBe(true);
      expect(tmSearchSameColumns([expression], [{ expression: 'creditlimit * 2', labelKey: 'X' }])).toBe(false);
      expect(tmSearchSameColumns([expression], ['CreditLimit * 2'])).toBe(false);
    });
  });

  describe('extra paths', () => {
    beforeEach(async () => {
      await tmEntityRig({ access: { isSystem: true } });
    });

    it('read a declared tile’s parts, its title defaulting to the display name', () => {
      expect(tmSearchTilePaths(tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS))).toEqual(['Image', 'Name', 'Code', 'IsActive']);
      expect(tmSearchTilePaths(tmSearchTestSetting('acme.Customer', { tile: { topEnd: 'CreditLimit' } }))).toEqual(['Code', 'Name', 'CreditLimit']);
    });

    it('read an image given as its key as its navigation', () => {
      expect(tmSearchTilePaths(tmSearchTestSetting('acme.Customer', { tile: { image: 'ImageId', title: 'Code' } }))).toEqual(['Image', 'Code']);
      // A key that is not an image's stays as it is.
      expect(tmSearchTilePaths(tmSearchTestSetting('acme.Customer', { tile: { image: 'CurrencyId', title: 'Code' } }))).toEqual(['CurrencyId', 'Code']);
    });

    it('read the default tile without a declaration', () => {
      // The first image navigation, the display name and IsActive; Code is the display name's prefix.
      expect(tmSearchTilePaths(tmSearchTestSetting('acme.Customer'))).toEqual(['Image', 'Code', 'Name', 'IsActive']);
      // The first natural key the display name does not show.
      expect(tmSearchTilePaths(tmSearchTestSetting('core.User'))).toEqual(['Image', 'Name', 'Email', 'IsActive']);
      // No image navigation, and the only natural key is the display name.
      expect(tmSearchTilePaths(tmSearchTestSetting('gl.Center'))).toEqual(['Code', 'IsActive']);
    });

    it('read nothing for a tile drawn by a component', () => {
      expect(tmSearchTilePaths(tmSearchTestSetting('acme.Customer', { tile: { component: () => Promise.resolve(class {}) } }))).toEqual([]);
    });

    it('read a tree’s parent and subtree sizes', () => {
      const entities = TestBed.inject(TmEntities);
      expect(tmSearchTreeKeys(entities.get('gl.Center'))).toEqual(['ParentId', 'SubtreeCount', 'ActiveSubtreeCount']);
      expect(tmSearchTreeKeys(entities.get('acme.Customer'))).toEqual([]);
    });
  });

  describe('paging', () => {
    beforeEach(async () => {
      await tmEntityRig({ access: { isSystem: true } });
    });

    it('offers the page sizes within the largest page and pages within the skip window', () => {
      const centers = TestBed.inject(TmEntities).get('gl.Center');
      expect(tmSearchPageSizes(centers)).toEqual([20, 50, 100, 200]);
      expect(tmSearchPageSizes({ ...centers, limits: { ...centers.limits, maxTake: 100 } })).toEqual([20, 50, 100]);
      expect(tmSearchMaxPage(centers, 50)).toBe(2000);
      expect(tmSearchMaxPage({ ...centers, limits: { ...centers.limits, maxSkipWindow: 130 } }, 50)).toBe(3);
    });

    it('keeps the first visible row when the page size changes', () => {
      expect(tmSearchPageForSize(3, 50, 20)).toBe(6);
      expect(tmSearchPageForSize(6, 20, 50)).toBe(3);
      expect(tmSearchPageForSize(1, 50, 200)).toBe(1);
      expect(tmSearchPageForSize(5, 50, 200)).toBe(2);
      // Every page and size pair: the old page's first row is on the new page.
      for (const oldSize of [20, 50, 100, 200]) {
        for (const newSize of [20, 50, 100, 200]) {
          for (let page = 1; page <= 40; page++) {
            const first = (page - 1) * oldSize + 1;
            const next = tmSearchPageForSize(page, oldSize, newSize);
            expect(first).toBeGreaterThan((next - 1) * newSize);
            expect(first).toBeLessThanOrEqual(next * newSize);
          }
        }
      }
    });

    it('fills a canonical query’s left-out fields with their code defaults', () => {
      const setting = tmSearchTestSetting('acme.Customer', TM_TEST_CUSTOMERS, { width: 'narrow' });
      expect(tmSearchEffective(setting, {})).toEqual({ tab: TM_TEST_CUSTOMERS.tabs![0], q: '', inactive: false, page: 1, size: 50, view: 'tiles', tree: false });
      expect(tmSearchEffective(setting, { tab: 'open', q: 'al', inactive: true, page: 3, size: 20, view: 'grid' })).toEqual({
        tab: TM_TEST_CUSTOMERS.tabs![1],
        q: 'al',
        inactive: true,
        page: 3,
        size: 20,
        view: 'grid',
        tree: false,
      });
      expect(tmSearchEffective(tmSearchTestSetting('gl.Center'), {}).tree).toBe(true);
      expect(tmSearchEffective(tmSearchTestSetting('gl.Center'), { list: true }).tree).toBe(false);
      expect(tmSearchWidthView('wide')).toBe('grid');
    });
  });

  describe('preferences', () => {
    it('are keyed per entity, written behind, and check their stored shapes', () => {
      const columns = tmSearchColumnsKey('gl.Center');
      expect([columns.key, columns.kind, columns.fallback]).toEqual(['search.gl.Center.columns', 'write-behind', null]);
      expect(columns.validate!(['Code', { expression: 'CreditLimit * 2' }])).toBe(true);
      expect(columns.validate!([])).toBe(false);
      expect(columns.validate!(['Code', 7])).toBe(false);
      expect(columns.validate!([{ expression: '' }])).toBe(false);

      const view = tmSearchViewKey('gl.Center');
      expect([view.key, view.fallback]).toEqual(['search.gl.Center.view', {}]);
      expect(view.validate!({ narrow: 'grid', wide: 'tiles', list: true })).toBe(true);
      expect(view.validate!({ wide: 'list' })).toBe(false);
      expect(view.validate!([])).toBe(false);

      const size = tmSearchPageSizeKey('gl.Center');
      expect([size.key, size.fallback]).toEqual(['search.gl.Center.pageSize', 50]);
      expect(size.validate!(200)).toBe(true);
      expect(size.validate!(30)).toBe(false);
    });

    it('read stored columns as the declared columns where they match', () => {
      const declared = TM_TEST_CUSTOMERS_WITH_EXPRESSION;
      const read = tmSearchColumnsFromPreference({ ...declared, columns: [...declared.columns!, { path: 'CreditLimit', width: 140 }] }, [
        'creditlimit',
        { expression: 'CreditLimit * 2' },
        { expression: 'CreditLimit * 3' },
        'Currency',
      ]);
      expect(read).toEqual([
        { path: 'creditlimit', width: 140 },
        { expression: 'CreditLimit * 2', labelKey: 'Acme_Customer_DoubleCredit', type: 'decimal' },
        { expression: 'CreditLimit * 3', labelKey: TM_SEARCH_EXPRESSION_LABEL },
        'Currency',
      ]);
      expect(tmSearchColumnsToPreference(read)).toEqual(['creditlimit', { expression: 'CreditLimit * 2' }, { expression: 'CreditLimit * 3' }, 'Currency']);
    });

    it('write one view half, deleting it at its class default and keeping the rest', () => {
      expect(tmSearchViewHalf({ narrow: 'grid', list: true }, 'wide', 'tiles', 'grid')).toEqual({ narrow: 'grid', list: true, wide: 'tiles' });
      expect(tmSearchViewHalf({ narrow: 'grid', wide: 'tiles' }, 'wide', 'grid', 'grid')).toEqual({ narrow: 'grid' });
      expect(tmSearchViewList({ wide: 'tiles' }, true)).toEqual({ wide: 'tiles', list: true });
      expect(tmSearchViewList({ wide: 'tiles', list: true }, false)).toEqual({ wide: 'tiles' });
    });
  });
});
