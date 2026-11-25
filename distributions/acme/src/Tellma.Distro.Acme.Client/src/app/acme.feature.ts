// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmEntity, tmFeature } from '@tellma/core';

/**
 * Acme's own feature, the client side of the server's `acme` feature: the
 * customers' details page so far; Acme's sections, other entities, pages
 * and reports join it as the server side composes them.
 */
export const acmeFeature = tmFeature({
  key: 'acme',
  entities: [
    // The layout loads with the first customer's page, in a chunk of its own.
    tmEntity('acme.Customer', { details: () => import('./customers/customer-details') }),
  ],
});
