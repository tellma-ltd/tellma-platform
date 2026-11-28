// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector, signal } from '@angular/core';
import { type TmJson, type TmLauncherSearchContext, TmApi, TmLocalization, TmUrls } from '@tellma/core';

import { AcmeInvoiceSearch } from './acme-invoice-search';
import { acmeSearchInvoices } from './invoice-search';

// The sales-invoice bucket's read over stand-ins of the API, the
// localization and the URLs: digits, in any digit set, find an invoice by
// its serial number; other text finds the invoices of the customers whose
// name, in the content language and the first, holds it as typed, quoted;
// and each row opens the invoices' search filtered to it, described by the
// customer's name in the content language when it has one.

/** One read the stand-in API was asked for. */
interface AcmeRead {
  /** The route. */
  readonly route: string;
  /** The body. */
  readonly body: { readonly select: string; readonly filters: readonly string[]; readonly orderBy: string; readonly take: number };
  /** The options. */
  readonly options: { readonly signal: AbortSignal; readonly background: boolean; readonly tenantId: number };
}

/**
 * Runs one search over stand-ins that answer the given rows.
 *
 * @param query - The query as typed.
 * @param slot - The content language's slot.
 * @param rows - The rows the read answers, by the select's positions.
 * @returns The read the search sent and the items it returned.
 */
async function search(query: string, slot: number, rows: readonly (readonly TmJson[])[] = []) {
  const reads: AcmeRead[] = [];
  const injector = Injector.create({
    providers: [
      {
        provide: TmApi,
        useValue: {
          read: (route: string, body: AcmeRead['body'], options: AcmeRead['options']) => {
            reads.push({ route, body, options });
            return Promise.resolve({ rows: { columns: [], rows } });
          },
        },
      },
      { provide: TmLocalization, useValue: { contentSlot: signal(slot) } },
      {
        provide: TmUrls,
        useValue: {
          forTenant: (tenantId: number) => ({
            search: (entity: string, state: { filter: string }) => `/${tenantId}/${entity}?${state.filter}`,
          }),
        },
      },
    ],
  });
  const context: TmLauncherSearchContext = { tenantId: 7, language: 'en', limit: 5, signal: new AbortController().signal };
  const items = await acmeSearchInvoices(injector, query, context);
  return { read: reads[0]!, items, context };
}

describe("Acme's sales-invoice launcher bucket", () => {
  it('declares its bucket under the invoices heading, asked from one character', () => {
    const provider = Injector.create({ providers: [{ provide: AcmeInvoiceSearch }] }).get(AcmeInvoiceSearch);
    expect(provider.id).toBe('acme-invoices');
    expect(provider.labelKey).toBe('Acme_SalesInvoice_Plural');
    expect(provider.minLength).toBe(1);
  });

  it('finds an invoice by its serial number, newest first, in the background of the tenant', async () => {
    const { read, context } = await search('12', 1);
    expect(read.route).toBe('sales-invoices/query');
    expect(read.body).toEqual({ select: 'Id,SerialNumber,Customer.Name', filters: ['SerialNumber = 12'], orderBy: 'SerialNumber desc', take: 5 });
    expect(read.options).toEqual({ signal: context.signal, background: true, tenantId: 7 });
  });

  it('reads Arabic-Indic digits as the serial number they write', async () => {
    const { read } = await search('١٢', 1);
    expect(read.body.filters).toEqual(['SerialNumber = 12']);
  });

  it("finds other text in either of the customer's names, quoted as typed", async () => {
    const { read } = await search(" O'Brien ", 2);
    expect(read.body.select).toBe('Id,SerialNumber,Customer.Name,Customer.Name2');
    expect(read.body.filters).toEqual(["contains(Customer.Name, 'O''Brien') or contains(Customer.Name2, 'O''Brien')"]);
  });

  it("opens each row's invoice in the invoices' search, described by the content language's name", async () => {
    const { items } = await search('نور', 2, [
      [41, 1007, 'Al Noor Trading', 'النور للتجارة'],
      [40, 1006, 'Falcon Foods', null],
      [39, 1005, null, null],
    ]);
    expect(items).toEqual([
      { id: '41', icon: 'file-text', href: '/7/acme.SalesInvoice?SerialNumber = 1007', label: '1007', description: 'النور للتجارة' },
      { id: '40', icon: 'file-text', href: '/7/acme.SalesInvoice?SerialNumber = 1006', label: '1006', description: 'Falcon Foods' },
      { id: '39', icon: 'file-text', href: '/7/acme.SalesInvoice?SerialNumber = 1005', label: '1005' },
    ]);
  });
});
