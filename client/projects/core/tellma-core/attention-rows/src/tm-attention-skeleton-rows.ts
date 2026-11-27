// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

// The loading rows every attention list shows before its first copy (spec
// 0028 §6.2, §7.3): three rows of the shared row anatomy, an icon and two
// lines, read as one busy list. The bell's notifications and the
// assignment list draw the same rows, so the list keeps its place when the
// copy lands.

/**
 * Three skeleton rows of the attention row anatomy, read as one busy list
 * with the list's name. Internal: the attention lists render it before
 * their first copy, beside `tm-attention-row-styles`.
 *
 * @example
 * ```html
 * <tm-attention-row-styles />
 * <tm-attention-skeleton-rows label="Notifications" />
 * ```
 */
@Component({
  selector: 'tm-attention-skeleton-rows',
  imports: [TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="tm-attention-rows" aria-busy="true" [attr.aria-label]="label()">
      @for (index of rows; track index) {
        <li class="tm-attention-row" aria-hidden="true">
          <tm-skeleton class="tm-attention-row__icon" shape="block" width="16px" height="16px" />
          <span class="tm-attention-row__main">
            <tm-skeleton width="60%" />
            <tm-skeleton width="85%" />
          </span>
        </li>
      }
    </ul>
  `,
  styles: `
    :host {
      display: block;
    }
  `,
})
export class ɵTmAttentionSkeletonRows {
  /** The rows' keys. */
  protected readonly rows = [0, 1, 2];

  /** The busy list's accessible name, such as "Notifications". */
  readonly label = input.required<string>();
}
