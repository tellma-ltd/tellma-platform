// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmBadge, TmCounter, type TmBadgeTone } from '@tellma/core-ui/badge';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';

/**
 * tm-badge in every tone, with a dot and with an icon; tm-counter inline
 * (with a stepper the bounding-box spec drives from 9 to 10), capped,
 * zero and null, in each tone, and overlaid on an icon button's corner.
 */
@Component({
  imports: [TmBadge, TmButton, TmCounter, TmIcon],
  template: `
    <h2>Badges and counters</h2>

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

    <!-- An inline count in a sentence, stepped by the button beside it. -->
    <section class="row">
      <span data-testid="inline-row"
        >Unread <tm-counter [value]="count()" data-testid="counter-inline" /> messages</span
      >
      <button tmButton type="button" data-testid="increment" (click)="count.set(count() + 1)">
        Add one
      </button>
    </section>

    <!-- The cap, zero shown and hidden, null, and every tone. -->
    <section class="row">
      <tm-counter [value]="120" data-testid="counter-capped" />
      <tm-counter [value]="1200" [max]="999" data-testid="counter-custom-max" />
      <tm-counter [value]="0" showZero data-testid="counter-zero" />
      <tm-counter [value]="0" data-testid="counter-hidden-zero" />
      <tm-counter [value]="null" data-testid="counter-null" />
      <tm-counter [value]="7" tone="accent" data-testid="counter-accent" />
      <tm-counter [value]="3" tone="attention" data-testid="counter-attention" />
    </section>

    <!-- Overlay counters ride the button's corner; the name carries the count. -->
    <section class="row">
      <button
        tmButton
        variant="ghost"
        type="button"
        class="corner"
        aria-label="Notifications, 3 new"
        data-testid="overlay-button"
      >
        <tm-icon name="plus" />
        <tm-counter
          [value]="3"
          tone="attention"
          placement="overlay"
          data-testid="counter-overlay"
        />
      </button>
      <button tmButton variant="ghost" type="button" class="corner" aria-label="Jobs, 120 queued">
        <tm-icon name="plus" />
        <tm-counter [value]="120" placement="overlay" />
      </button>
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
    .corner {
      position: relative;
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

  /** The stepped inline count; it starts at 9 so one step crosses to two digits. */
  protected readonly count = signal(9);
}
