// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, Injector, inject } from '@angular/core';
import type { TmLauncherItem, TmLauncherProvider, TmLauncherSearchContext } from '@tellma/core';

// The launcher's sales-invoice bucket (spec 0027 §9.6): a query of digits
// finds the invoice of that serial number, and any other query the invoices
// of the customers whose name holds it. The class is all the first frame
// carries; the read and the rows' shaping load with the first query that
// reaches the provider.

/** The launcher's provider of matching sales invoices, under the Sales invoices heading. */
@Injectable()
export class AcmeInvoiceSearch implements TmLauncherProvider {
  /** The bucket's id. */
  readonly id = 'acme-invoices';

  /** The bucket's heading: the invoices' plural label. */
  readonly labelKey = 'Acme_SalesInvoice_Plural';

  /** The bucket's place, the first below Pages. */
  readonly order = 10;

  /** A serial number of one digit is worth asking about. */
  readonly minLength = 1;

  /** The injector the search's services come from. */
  readonly #injector = inject(Injector);

  /**
   * Finds the invoices that match a query.
   *
   * @param query - The query as typed.
   * @param context - The tenant, the language, the row limit and the abort signal.
   * @returns The rows, at most the limit.
   */
  async search(query: string, context: TmLauncherSearchContext): Promise<readonly TmLauncherItem[]> {
    const { acmeSearchInvoices } = await import('./invoice-search');
    return acmeSearchInvoices(this.#injector, query, context);
  }
}
