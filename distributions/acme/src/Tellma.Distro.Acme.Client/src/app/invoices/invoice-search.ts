// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Injector, untracked } from '@angular/core';
import {
  type TmJson,
  type TmLauncherItem,
  type TmLauncherSearchContext,
  type TmQueryRowSet,
  TmApi,
  TmLocalization,
  TmUrls,
} from '@tellma/core';
import { tmSearchNormalize } from '@tellma/core/search-normalize';

// The sales-invoice bucket's read, which the provider loads with its first
// query: one query of the invoices, at most the launcher's limit, newest
// first. Each row opens the invoices' search page filtered to that invoice,
// since the invoices have no details page of their own yet.

/** The invoices' entity name. */
const INVOICE = 'acme.SalesInvoice';

/** A query of digits alone, read as a serial number. */
const SERIAL = /^\d{1,9}$/;

/**
 * Finds the sales invoices that match a launcher query: by serial number
 * when the query is digits, else by the customer's name, in the active
 * content language.
 *
 * @param injector - The provider's injector.
 * @param query - The query as typed.
 * @param context - The tenant, the row limit and the abort signal.
 * @returns The rows: the serial number, described by the customer's name.
 */
export async function acmeSearchInvoices(
  injector: Injector,
  query: string,
  context: TmLauncherSearchContext,
): Promise<readonly TmLauncherItem[]> {
  // Normalized, the query's digits are ASCII whatever the keyboard typed them in.
  const normalized = tmSearchNormalize(query);
  const slot = untracked(injector.get(TmLocalization).contentSlot);
  const names = slot === 1 ? ['Customer.Name'] : ['Customer.Name', `Customer.Name${slot}`];

  // --- The filter: the serial number, or either name holding the text as typed, a quoted literal ---
  const text = `'${query.trim().replaceAll("'", "''")}'`;
  const filter = SERIAL.test(normalized)
    ? `SerialNumber = ${normalized}`
    : names.map((path) => `contains(${path}, ${text})`).join(' or ');
  const page = await injector.get(TmApi).read<{ readonly rows: TmQueryRowSet }>(
    'sales-invoices/query',
    {
      select: ['Id', 'SerialNumber', ...names].join(','),
      filters: [filter],
      orderBy: 'SerialNumber desc',
      take: context.limit,
    },
    { signal: context.signal, background: true, tenantId: context.tenantId },
  );

  // --- The rows, read by their select positions ---
  const urls = injector.get(TmUrls).forTenant(context.tenantId);
  return page.rows.rows.map((row: readonly TmJson[]) => {
    const serial = String(row[1]);
    const customer = (row[3] ?? row[2]) as string | null;
    return {
      id: String(row[0]),
      icon: 'file-text',
      href: urls.search(INVOICE, { filter: `SerialNumber = ${serial}` }),
      label: serial,
      ...(customer !== null && { description: customer }),
    };
  });
}
