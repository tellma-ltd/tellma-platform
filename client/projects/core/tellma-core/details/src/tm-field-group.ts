// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive } from '@angular/core';

// A component of several fields (spec 0031 §6.2): as a host directive it
// takes the component's box out of the layout, so its fields are cells of
// the card's grid like any other. Its fields reach the record, and the
// card and tab around the component, through the component's injector, so
// their registrations and errors are the enclosing card's and tab's.

/**
 * Joins a component of `tm-field`s to the card's field grid: put it in the
 * component's `hostDirectives`. The component's box takes no space, so each
 * field is a cell of the card's grid, and the fields' registrations and
 * errors reach the card and tab around the component.
 *
 * @example
 * ```ts
 * @Component({
 *   selector: 'acme-address-fields',
 *   hostDirectives: [TmFieldGroup],
 *   imports: [TmDetailsKit],
 *   template: '<tm-field path="Street" /><tm-field path="City" />',
 * })
 * export class AcmeAddressFields {}
 * ```
 */
@Directive({
  selector: '[tmFieldGroup]',
  host: { class: 'tm-field-group', '[style.display]': '"contents"' },
})
export class TmFieldGroup {}
