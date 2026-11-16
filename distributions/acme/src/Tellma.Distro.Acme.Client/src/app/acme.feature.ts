// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmFeature } from '@tellma/core';

/**
 * Acme's own feature, the client side of the server's `acme` feature. It
 * declares nothing yet; Acme's sections, entities, pages and reports join it
 * as the server side composes them.
 */
export const acmeFeature = tmFeature({ key: 'acme' });
