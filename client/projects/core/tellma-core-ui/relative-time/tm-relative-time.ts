// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, DestroyRef, inject, input } from '@angular/core';

import { TM_NOW, TmL10n } from '@tellma/core-ui';
import { TmTooltip } from '@tellma/core-ui/tooltip';

import { TmMinuteTicker } from './internal/minute-ticker';

/**
 * How long ago an instant was — "2 min ago" — kept current: every
 * instance on the page re-renders from one shared timer on each minute
 * boundary, which stops while the page is hidden and catches up when it
 * shows again. Hovering shows the absolute date and time.
 *
 * The text follows the active locale and the display time zone. For a
 * time of day ("Updated 10:42") use `TmL10n.formatTime` instead.
 *
 * @tmGroup indicator
 * @tmA11yNotes A `<time datetime>` element read as text; the absolute
 *   date and time is its accessible description (the tooltip's text), so
 *   assistive technology hears it without hovering.
 */
@Component({
  selector: 'tm-relative-time',
  imports: [TmTooltip],
  template: `<time [attr.datetime]="value()" [tmTooltip]="absolute()">{{ text() }}</time>`,
  styleUrl: './tm-relative-time.css',
  host: { class: 'tm-relative-time' },
})
export class TmRelativeTime {
  private readonly l10n = inject(TmL10n);
  private readonly now = inject(TM_NOW);
  private readonly ticker = inject(TmMinuteTicker);

  /** The instant, as an ISO 8601 string with `Z` or an offset. */
  readonly value = input.required<string>();

  // Not named `style`: Angular routes a `style` attribute or `[style]`
  // binding to the element's styling, so an input of that name never
  // receives a value.
  /**
   * The wording: "2 min ago" (`short`, the default), "2 minutes ago"
   * (`long`), or the tightest form (`narrow`).
   */
  readonly textStyle = input<'long' | 'short' | 'narrow'>('short');

  /** The relative text, re-read on every tick against a fresh clock reading. */
  protected readonly text = computed(() => {
    this.ticker.tick();
    return this.l10n.formatRelativeTime(this.value(), { now: this.now(), style: this.textStyle() });
  });

  /** The absolute date and time for the tooltip, labelled with the zone when it differs. */
  protected readonly absolute = computed(() =>
    this.l10n.formatDateTime(this.value(), { dateStyle: 'medium' }),
  );

  constructor() {
    inject(DestroyRef).onDestroy(this.ticker.subscribe());
  }
}
