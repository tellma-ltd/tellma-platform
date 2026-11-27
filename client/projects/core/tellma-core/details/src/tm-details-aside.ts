// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  Directive,
  inject,
  input,
  type InputSignal,
  type Signal,
  TemplateRef,
} from '@angular/core';
import { TmLocalization } from '@tellma/core';

import { injectTmDetailsRecord } from './tm-details-record';

// The layout's aside (spec 0031 §6.7, §5.3): its content renders in the
// band's aside column, a sticky complementary region at the inline end
// from lg up, and a card after the main cards below it. The element itself
// draws nothing where the layout puts it; its content keeps the layout's
// injector, so the fields and extras in it read the record as anywhere
// else. One per layout.

/**
 * The aside's content, which registers with the record as the aside's
 * view is created. Internal: `tm-details-aside` renders it.
 */
@Directive({ selector: 'ng-template[tmDetailsAsideContent]' })
export class ɵTmDetailsAsideContent {
  /** Registers the content and the aside's heading with the record. */
  constructor() {
    // The aside is declared below; it is resolved when the content's view is created.
    const aside = inject(TmDetailsAside);
    injectTmDetailsRecord().registerAside({ template: inject<TemplateRef<unknown>>(TemplateRef), title: aside.title }, inject(DestroyRef));
  }
}

/**
 * Content for the details band's aside column: related documents, a
 * summary, a record's extras. The page renders it in a sticky column at
 * the inline end from lg up, and as a card after the main cards below.
 *
 * @example
 * ```html
 * <tm-details-aside titleKey="Acme_SalesInvoice_Related">
 *   <acme-related-documents *tmDetailsExtra="'related-documents'; let rows" [rows]="rows" />
 * </tm-details-aside>
 * ```
 */
@Component({
  selector: 'tm-details-aside',
  imports: [ɵTmDetailsAsideContent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-details-aside' },
  styles: `
    :host {
      display: none;
    }
  `,
  template: `
    <!-- The content, which the band's aside column renders. -->
    <ng-template tmDetailsAsideContent><ng-content /></ng-template>
  `,
})
export class TmDetailsAside {
  readonly #l10n = inject(TmLocalization);

  /** The aside's heading key; no heading when omitted. */
  readonly titleKey: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The heading, in the active language; empty for none. */
  readonly title: Signal<string> = computed(() => {
    const key = this.titleKey();
    return key === undefined ? '' : this.#l10n.ɵtext(key)();
  });
}
