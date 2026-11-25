// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component } from '@angular/core';
import type { TmJsonObject, TmPropertyDescriptor } from '@tellma/core';
import { injectTmDetails } from '@tellma/core/details';

/** The customer members the page shows, in order. */
const MEMBERS = ['Code', 'Name', 'CustomerType', 'CreditLimit'] as const;

/**
 * Acme's customer details layout: the customer's code, name, type and
 * credit limit, each labelled from the entity's descriptors and shown as the
 * record holds it. The host owns the record's lifecycle, modes, header and
 * errors; this is the body alone. It shows the values in every mode until
 * the layout kit's cards and fields, which format and edit them, replace the
 * list.
 */
@Component({
  selector: 'acme-customer-details',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- One labelled value per member; empty while the record loads. -->
    <dl class="acme-customer-details">
      @for (property of properties; track property.name) {
        <div class="acme-customer-details__field">
          <dt class="acme-customer-details__label">{{ property.label() }}</dt>
          <dd class="acme-customer-details__value">
            {{ details.entity()?.[property.json] ?? '' }}
          </dd>
        </div>
      }
    </dl>
  `,
  styles: `
    /* A card of label and value rows, as the layout kit's card will draw it. */
    .acme-customer-details {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 8px 16px;
      margin: 0;
      padding: 16px;
      border: var(--border-width) solid var(--border-subtle);
      border-radius: var(--radius-md);
      background: var(--surface-card);
    }

    .acme-customer-details__field {
      display: contents;
    }

    .acme-customer-details__label {
      color: var(--text-muted);
    }

    .acme-customer-details__value {
      min-block-size: 1lh;
      margin: 0;
    }
  `,
})
export default class CustomerDetails {
  /** The customer on the page. */
  protected readonly details = injectTmDetails<TmJsonObject>();

  /** The members' descriptors, as the tenant's shape gates them. */
  protected readonly properties: readonly TmPropertyDescriptor[] = MEMBERS.map((name) => this.details.descriptor.property(name)).filter(
    (property) => property !== undefined,
  );
}
