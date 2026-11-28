// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { TM_LAUNCHER_PROVIDERS, tmEntity, tmFeature, tmPage } from '@tellma/core';
import Landmark from 'lucide/dist/esm/icons/landmark.mjs';
import Users from 'lucide/dist/esm/icons/users.mjs';

import { AcmeInvoiceSearch } from './invoices/acme-invoice-search';

/**
 * Acme's own feature, the client side of the server's `acme` feature: the
 * Sales section of the launcher with the customers, whose details page also
 * asks a corporate customer for a credit limit, and the sales invoices; the
 * credit-review page under Financials; the launcher's bucket of matching
 * sales invoices; and GL's centers under Administration. Acme's coded
 * reports join it as the server side declares them.
 */
export const acmeFeature = tmFeature({
  key: 'acme',
  // --- The launcher's sections: Sales, and Financials in place of the GL module's ---
  sections: [
    { id: 'sales', labelKey: 'Acme_Nav_Sales', icon: 'users', order: 20 },
    // A stand-in for the section the GL module's client feature declares, which does not exist yet.
    { id: 'financials', labelKey: 'Acme_Nav_Financials', icon: 'landmark', order: 10 },
  ],
  entities: [
    // The layout and the form's own rules load with the first customer's page, each in a chunk of its own.
    tmEntity('acme.Customer', {
      section: 'sales',
      order: 10,
      details: () => import('./customers/customer-details'),
      validate: () => import('./customers/customer-rules'),
    }),
    tmEntity('acme.SalesInvoice', { section: 'sales', order: 20 }),
    // GL's centers, under Administration as the GL module places them: a
    // stand-in for that module's client feature, which does not exist yet.
    tmEntity('gl.Center', { section: 'administration', order: 30 }),
  ],
  pages: [
    tmPage('credit-review', {
      component: () => import('./credit/credit-review-page'),
      titleKey: 'Acme_CreditReview',
      section: 'financials',
    }),
  ],
  // --- The section icons, which no platform surface registers ---
  icons: { landmark: Landmark, users: Users },
  // --- The launcher's sales-invoice bucket: its search loads on the first query that reaches it ---
  providers: [{ provide: TM_LAUNCHER_PROVIDERS, multi: true, useClass: AcmeInvoiceSearch }],
});
