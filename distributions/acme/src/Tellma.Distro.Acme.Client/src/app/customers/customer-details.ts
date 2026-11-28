// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TmDetailsKit } from '@tellma/core/details';

/**
 * Acme's customer details layout, in the platform's layout kit: one card
 * of the customer's name (a field per tenant language), code, type,
 * currency and credit limit, a card of the customer's contacts in a grid
 * that edits them in place, then the collapsed audit card. The host owns
 * the record's lifecycle, modes, header and errors; each field labels,
 * formats and edits its value from the entity's descriptors.
 */
@Component({
  selector: 'acme-customer-details',
  imports: [TmDetailsKit],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The customer: the multilingual name, then the code, the type, the currency (a link in view mode, a picker in edit mode) and the credit limit. -->
    <tm-details-card>
      <tm-field path="Name" />
      <tm-field path="Code" />
      <tm-field path="CustomerType" />
      <tm-field path="Currency" />
      <tm-field path="CreditLimit" />
    </tm-details-card>
    <!-- The contacts: a grid of their name, email and mobile, edited in place in edit mode. -->
    <tm-details-card titleKey="Acme_Customer_Contacts">
      <tm-child-grid collection="Contacts" [columns]="['Name', 'Email', 'Mobile']" />
    </tm-details-card>
    <!-- Who created and last modified the customer, and when. -->
    <tm-details-audit />
  `,
})
export default class CustomerDetails {}
