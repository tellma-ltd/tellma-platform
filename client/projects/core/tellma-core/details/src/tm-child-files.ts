// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, type InputSignal } from '@angular/core';
import { TmLocalization } from '@tellma/core';

import { ɵTmChildFilesBody } from './tm-child-files-body';
import { ɵtmDeferTriggers } from './tm-details-defer';

// A child collection of files (spec 0031 §6.5), such as a sales invoice's
// attachments. This part loads with the details route and holds only the
// deferred body: the chips, the drop zone and the rows' uploads render in
// a chunk of their own once the list enters the viewport, prefetched once
// the browser is idle, as the child grid's does (§6.4), so a record whose
// files sit below the fold or on another tab never loads them up front.

/**
 * A child collection of files as a list of chips: each a download link
 * that previews on click, with a thumbnail or a type icon, the name and the
 * size. In edit and create mode a drop zone adds files, each uploading at
 * once with a progress ring; each row has a remove button and its caption
 * edits inline.
 *
 * @example
 * ```html
 * <tm-details-card titleKey="Acme_SalesInvoice_Attachments">
 *   <tm-child-files collection="Attachments" file="File" caption="Caption" />
 * </tm-details-card>
 * ```
 */
@Component({
  selector: 'tm-child-files',
  imports: [ɵTmChildFilesBody],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-child-files', '[attr.data-tm-collection]': 'collection()' },
  styles: `
    :host {
      display: block;
      min-inline-size: 0;
    }

    /* Before the body renders: one row of chips' height, so the card hardly moves. */
    .tm-child-files__placeholder {
      min-block-size: var(--chip-height);
    }

    .tm-child-files__failed {
      margin: 0;
      color: var(--error);
      font-size: var(--text-sm);
    }
  `,
  template: `
    @defer (when defer.visible()) {
      <tm-child-files-body [collection]="collection()" [file]="file()" [caption]="caption()" />
    } @placeholder {
      <div class="tm-child-files__placeholder" aria-hidden="true"></div>
    } @error {
      <p class="tm-child-files__failed" role="alert">{{ failedText() }}</p>
    }
  `,
})
export class TmChildFiles {
  readonly #l10n = inject(TmLocalization);

  /** The child collection's name, such as `Attachments`. */
  readonly collection: InputSignal<string> = input.required<string>();

  /** The child's file navigation, such as `File`. */
  readonly file: InputSignal<string> = input.required<string>();

  /** The child's caption: a text property or a multilingual group, such as `Caption`; none when omitted. */
  readonly caption: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** When the body's chunk loads: once the list enters the viewport, prefetched once the browser is idle. */
  protected readonly defer = ɵtmDeferTriggers(() => import('./tm-child-files-body'));

  /** The failed load's message. */
  protected readonly failedText = computed(() => this.#l10n.ɵtext('tm.details.filesFailed')());
}
