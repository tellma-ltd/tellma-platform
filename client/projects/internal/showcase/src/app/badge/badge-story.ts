// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmBadge, type TmBadgeTone } from '@tellma/core-ui/badge';

/**
 * tm-badge in every tone, with a dot and with an icon, and labels whose
 * descenders and Arabic dots the pill must not clip. tm-counter has its own
 * story.
 */
@Component({
  imports: [TmBadge],
  template: `
    <h2>Badges</h2>

    <!-- Every tone, plain, with a dot and with an icon. -->
    <section class="row" data-testid="badges">
      @for (tone of tones; track tone) {
        <tm-badge [tone]="tone" [attr.data-testid]="'badge-' + tone">{{ tone }}</tm-badge>
      }
    </section>
    <section class="row">
      <tm-badge tone="info" dot data-testid="badge-dot">Syncing</tm-badge>
      <tm-badge tone="error" icon="circle-alert" data-testid="badge-icon">2 problems</tm-badge>
      <tm-badge tone="success" dot>Active</tm-badge>
      <tm-badge tone="warning" dot>Held</tm-badge>
    </section>

    <!-- Glyphs that reach below the baseline: Latin descenders and the
         dots under Arabic letters, which the label must not clip. -->
    <section class="row">
      <tm-badge tone="warning" data-testid="badge-descenders">Typing quietly gjpqy</tm-badge>
      <tm-badge tone="info" lang="ar" data-testid="badge-arabic">قيد المراجعة يجب</tm-badge>
    </section>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 16px;
      margin-block-end: 16px;
    }
  `,
})
export class BadgeStory {
  /** Every badge tone, in the order the story shows them. */
  protected readonly tones: readonly TmBadgeTone[] = [
    'neutral',
    'accent',
    'info',
    'success',
    'warning',
    'error',
  ];
}
