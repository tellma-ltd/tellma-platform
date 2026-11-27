// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { required, type SchemaPathTree } from '@angular/forms/signals';
import type { TmEntityType } from '@tellma/core';

/**
 * Acme's own rules for a customer's details form, which the page applies
 * after the platform's: a corporate customer needs a credit limit. The
 * declaration loads this module with the layout, so Signal Forms stays out
 * of the app's first load.
 *
 * @param p - The customer form's schema path tree.
 */
export default function customerRules(p: SchemaPathTree<TmEntityType<'acme.Customer'>>): void {
  required(p.creditLimit, { when: ({ valueOf }) => valueOf(p.customerType) === 'Corporate' });
}
