// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { booleanAttribute, Component, computed, inject, input } from '@angular/core';

import { TM_UI_TRANSLATE, TmL10n } from '@tellma/core-ui';

/** The fills of a {@link TmCounter}; `attention` is the error fill. */
export type TmCounterTone = 'neutral' | 'accent' | 'attention';

/**
 * Where a {@link TmCounter} sits: in the flow of its line, or over its
 * parent's block-start, inline-end corner.
 */
export type TmCounterPlacement = 'inline' | 'overlay';

/**
 * A number in a 16px pill: a tab or card count, or the badge on an app-bar
 * button. Values above `max` show as "99+"; `0` and `null` render nothing
 * unless `showZero` is set. Digits follow the active locale and use
 * tabular figures; the run is left to right in every direction.
 *
 * An `inline` counter is read as text and is at least two digits wide, so
 * a count going from 9 to 10 moves nothing beside it. An `overlay` counter
 * is positioned absolutely at its parent's block-start, inline-end corner
 * (the parent must be positioned, as a `tmButton` is), inside a halo of
 * `--counter-halo`, which the host sets to the colour the counter sits on.
 * It is hidden from assistive technology: the parent's accessible name
 * carries the count ("Notifications, 3 new").
 *
 * @tmGroup indicator
 * @tmA11yNotes Inline: plain text in an isolated dir="ltr" run. Overlay:
 *   aria-hidden, so put the count in the parent's accessible name. Under
 *   forced colours every counter keeps a border, and the attention tone a
 *   system highlight fill.
 */
@Component({
  selector: 'tm-counter',
  // Only the digits run left to right: the host keeps the inherited
  // direction, so the overlay's logical corner follows the page.
  template: `<span class="tm-counter__digits" dir="ltr">{{ text() }}</span>`,
  styleUrl: './tm-counter.css',
  host: {
    class: 'tm-counter',
    '[class.tm-counter--overlay]': 'placement() === "overlay"',
    '[attr.data-tone]': 'tone()',
    '[class.tm-counter--empty]': 'text() === ""',
    '[attr.aria-hidden]': 'placement() === "overlay" ? "true" : null',
  },
})
export class TmCounter {
  private readonly l10n = inject(TmL10n);
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The count; `null` renders nothing. */
  readonly value = input<number | null>(null);

  /** The largest count shown as is; above it, "{max}+". Default 99. */
  readonly max = input(99);

  /** The fill. Default `neutral`. */
  readonly tone = input<TmCounterTone>('neutral');

  /** Inline in the line (default), or overlaid on the parent's corner. */
  readonly placement = input<TmCounterPlacement>('inline');

  /** Shows a zero count; by default `0`, like `null`, renders nothing. */
  readonly showZero = input(false, { transform: booleanAttribute });

  /** The text shown: '' renders nothing (the host collapses). */
  protected readonly text = computed(() => {
    const value = this.value();
    if (value === null || !Number.isFinite(value) || (value === 0 && !this.showZero())) {
      return '';
    }
    const max = this.max();
    return value > max
      ? this.translate('counter.overflow', { max: this.l10n.formatNumber(max) })()
      : this.l10n.formatNumber(value);
  });
}
