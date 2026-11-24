// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmEntities, TmUrls, type TmListPayload, type TmSearchSnapshot } from '@tellma/core';

import type { TmSearchDeclaration, TmSearchSetting } from './tm-search-model';
import type { TmSearchOpening } from './tm-search-resolve';

// The search model's test fixtures over the entity rig's recorded model:
// customers declared as a distribution would declare them (columns, an
// order, a tile, custom filters and tabs), centers as a tree with none of
// them, and a setting and an opening built from a URL.

/** Customers' search members: the declared columns, order and tile, a switch, an options filter, a named switch, and tabs with a leaf and a named filter. */
export const TM_TEST_CUSTOMERS: TmSearchDeclaration = {
  columns: ['Code', 'Name', 'CustomerType', 'Currency', { path: 'CreditLimit', width: 140 }, 'IsActive'],
  orderBy: 'Code',
  tile: { image: 'Image', title: 'Name', subtitle: 'Code', badge: 'IsActive' },
  filters: [
    { key: 'mine', labelKey: 'Acme_Customer_CreatedByMe', queryex: 'CreatedById = me()' },
    {
      key: 'credit',
      labelKey: 'Acme_Customer_Credit',
      options: [
        { value: 'none', labelKey: 'Acme_Customer_Credit_None', queryex: 'CreditLimit = 0' },
        { value: 'high', labelKey: 'Acme_Customer_Credit_High', queryex: 'CreditLimit >= 500000' },
      ],
    },
    { key: 'shared', labelKey: 'Acme_Customer_Shared', named: 'shared-with-me' },
  ],
  tabs: [
    { key: 'all', labelKey: 'Acme_Customer_TabAll' },
    { key: 'open', labelKey: 'Acme_Customer_TabOpen', queryex: 'CreditLimit > 0' },
    { key: 'starred', labelKey: 'Acme_Customer_TabStarred', named: 'starred' },
  ],
};

/** Customers with an expression column declared among the paths. */
export const TM_TEST_CUSTOMERS_WITH_EXPRESSION: TmSearchDeclaration = {
  ...TM_TEST_CUSTOMERS,
  columns: ['Code', 'Name', { expression: 'CreditLimit * 2', labelKey: 'Acme_Customer_DoubleCredit', type: 'decimal' }],
};

/**
 * A search setting in the rig's app: page mode, wide, no preference.
 *
 * @param entity - The entity's name.
 * @param declaration - Its declaration.
 * @param options - The members to set instead.
 * @returns The setting.
 */
export function tmSearchTestSetting(
  entity: string,
  declaration: TmSearchDeclaration = {},
  options: Partial<Omit<TmSearchSetting, 'descriptor' | 'declaration' | 'injector'>> = {},
): TmSearchSetting {
  return {
    mode: 'page',
    descriptor: TestBed.inject(TmEntities).get(entity),
    declaration,
    width: 'wide',
    preferences: { columns: null, view: {}, pageSize: 50 },
    injector: TestBed.inject(Injector),
    ...options,
  };
}

/**
 * What a page opens over: a URL's query and raw fragment, and a snapshot.
 *
 * @param url - The URL, such as `/17/centers?q=jed#d=…`.
 * @param snapshot - The page state's snapshot.
 * @returns The opening.
 */
export function tmSearchTestOpening(url: string, snapshot?: TmSearchSnapshot): TmSearchOpening {
  const parsed = new URL(url, 'https://tellma.test');
  return { params: parsed.searchParams, hash: parsed.hash, ...(snapshot && { snapshot }) };
}

/**
 * The `#d=` fragment of a search page's advanced filter, as the URLs print it.
 *
 * @param filter - The filter.
 * @returns The fragment, with its `#`.
 */
export function tmSearchTestHash(filter: string): string {
  const url = TestBed.inject(TmUrls).search('acme.Customer', { filter });
  return url.slice(url.indexOf('#'));
}

/**
 * The `#d=` fragment of a drill-through list's payload, as the URLs print it.
 *
 * @param payload - The payload.
 * @returns The fragment, with its `#`.
 */
export function tmSearchTestPayload(payload: TmListPayload): string {
  const url = TestBed.inject(TmUrls).list('customers', payload);
  return url.slice(url.indexOf('#'));
}
