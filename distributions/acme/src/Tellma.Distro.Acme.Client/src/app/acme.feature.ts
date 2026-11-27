// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmEntity, tmFeature } from '@tellma/core';

/**
 * Acme's own feature, the client side of the server's `acme` feature: the
 * customers' details page, whose form also asks a corporate customer for a
 * credit limit, and GL's centers in the launcher so far; Acme's sections,
 * other entities, pages and reports join it as the server side composes
 * them.
 */
export const acmeFeature = tmFeature({
  key: 'acme',
  entities: [
    // The layout and the form's own rules load with the first customer's page, each in a chunk of its own.
    tmEntity('acme.Customer', {
      details: () => import('./customers/customer-details'),
      validate: () => import('./customers/customer-rules'),
    }),
    // GL's centers, under Administration as the GL module places them: a
    // stand-in for that module's client feature, which does not exist yet.
    tmEntity('gl.Center', { section: 'administration', order: 30 }),
  ],
});
