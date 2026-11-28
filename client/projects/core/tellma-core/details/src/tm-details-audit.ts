// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, type Signal } from '@angular/core';

import { TmDetailsCard } from './tm-details-card';
import { injectTmDetailsRecord } from './tm-details-record';
import { TmField } from './tm-field';

// The record's audit card (spec 0031 §6.8): a collapsed "Audit info" card
// of the audit columns the entity has, each a read-only field, so the
// creator and the last modifier render as links and the instants in the
// display zone with its label, as tm-value renders them. An entity without
// audit columns gets nothing.

/** The audit columns, in the card's order: each navigation, then its instant. */
const AUDIT: readonly string[] = ['CreatedBy', 'CreatedAt', 'ModifiedBy', 'ModifiedAt'];

/**
 * The record's "Audit info" card, collapsed: who created and last modified
 * the record, and when. One line in a layout; nothing on an entity without
 * audit columns.
 *
 * @example
 * ```html
 * <tm-details-audit />
 * ```
 */
@Component({
  selector: 'tm-details-audit',
  imports: [TmDetailsCard, TmField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[style.display]': '"contents"' },
  template: `
    @if (paths().length > 0) {
      <tm-details-card id="audit" titleKey="tm.details.audit" collapsible collapsed>
        @for (path of paths(); track path) {
          <tm-field [path]="path" readonly />
        }
      </tm-details-card>
    }
  `,
})
export class TmDetailsAudit {
  readonly #record = injectTmDetailsRecord();

  /** The audit columns the entity has, as navigations or properties. */
  protected readonly paths: Signal<readonly string[]> = computed(() => {
    const descriptor = this.#record.descriptor;
    return AUDIT.filter((name) => descriptor.navigation(name) !== undefined || descriptor.property(name) !== undefined);
  });
}
