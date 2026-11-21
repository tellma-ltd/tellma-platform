// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { booleanAttribute, Component, computed, inject, input, output } from '@angular/core';
import { ChevronLeft, ChevronRight, CircleX, Info, TriangleAlert, X } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmIcon } from '@tellma/core-ui/icon';

/** The tone of a {@link TmBannerBar}. */
export type TmBannerTone = 'info' | 'warning' | 'error';

/** The default glyph of each tone, drawn through tm-icon. */
const TONE_GLYPHS = { info: Info, warning: TriangleAlert, error: CircleX } as const;

/**
 * One app-wide message in a 32px row: "A new version is available",
 * "You are offline". It shows a tone icon, one line of text that
 * ellipsizes, and optionally an action button, a "1 of 3" pager with
 * previous and next buttons (when `position.count` is above 1) and a
 * dismiss button.
 *
 * The bar is presentational: which message shows, when, and what the
 * buttons do belong to its host. It is not a live region; the host
 * announces each message once, when it is queued.
 *
 * @tmGroup indicator
 * @tmA11yNotes No live role. A visually hidden tone prefix ("Warning:")
 *   precedes the text, so the tone is not carried by colour alone. The
 *   action, the pager's buttons ("Previous message", "Next message") and
 *   "Dismiss" are native buttons, each at least 24px square.
 */
@Component({
  selector: 'tm-banner-bar',
  imports: [TmIcon],
  // The icon, the message (with its hidden tone prefix), then the controls
  // that are on: the action, the pager and the dismiss button.
  template: `
    <tm-icon class="tm-banner-bar__icon" [name]="icon()" [svg]="icon() === undefined ? glyph() : undefined" />
    <span class="tm-banner-bar__message"
      ><span class="tm-banner-bar__tone">{{ tonePrefix() }} </span>{{ message() }}</span
    >
    @if (actionLabel(); as label) {
      <button type="button" class="tm-banner-bar__action" (click)="action.emit()">
        {{ label }}
      </button>
    }
    @if (pager(); as page) {
      <span class="tm-banner-bar__pager">
        <button
          type="button"
          class="tm-banner-bar__button tm-banner-bar__previous"
          [attr.aria-label]="previousLabel()"
          (click)="previous.emit()"
        >
          <tm-icon [svg]="previousGlyph" />
        </button>
        <span class="tm-banner-bar__position">{{ page }}</span>
        <button
          type="button"
          class="tm-banner-bar__button tm-banner-bar__next"
          [attr.aria-label]="nextLabel()"
          (click)="next.emit()"
        >
          <tm-icon [svg]="nextGlyph" />
        </button>
      </span>
    }
    @if (dismissible()) {
      <button
        type="button"
        class="tm-banner-bar__button tm-banner-bar__dismiss"
        [attr.aria-label]="dismissLabel()"
        (click)="dismiss.emit()"
      >
        <tm-icon [svg]="dismissGlyph" />
      </button>
    }
  `,
  styleUrl: './tm-banner-bar.css',
  host: {
    class: 'tm-banner-bar',
    '[attr.data-tone]': 'tone()',
  },
})
export class TmBannerBar {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The tone: the fill, the border and the default icon. Default `info`. */
  readonly tone = input<TmBannerTone>('info');

  /** The message, one line; it ellipsizes when the bar is too narrow. */
  readonly message = input.required<string>();

  /** An icon by registry name; unset, the tone's own. */
  readonly icon = input<string | undefined>(undefined);

  /** The action button's label ("Reload", "Learn more"); `null`, no action. */
  readonly actionLabel = input<string | null>(null);

  /** Adds a "Dismiss" button. */
  readonly dismissible = input(false, { transform: booleanAttribute });

  /**
   * Where this message sits among the host's messages (`index` counts from
   * 1). With a `count` above 1, the bar shows "1 of 3" between previous
   * and next buttons, which are always enabled: the host wraps around.
   */
  readonly position = input<{ readonly index: number; readonly count: number } | null>(null);

  /** The action button was clicked. */
  readonly action = output<void>();

  /** The dismiss button was clicked. */
  readonly dismiss = output<void>();

  /** The previous button was clicked. */
  readonly previous = output<void>();

  /** The next button was clicked. */
  readonly next = output<void>();

  /** The previous glyph, mirrored in RTL so it points back along the line. */
  protected readonly previousGlyph = { node: ChevronLeft, mirrorInRtl: true };

  /** The next glyph, mirrored in RTL. */
  protected readonly nextGlyph = { node: ChevronRight, mirrorInRtl: true };

  /** The dismiss glyph. */
  protected readonly dismissGlyph = X;

  /** The tone's default glyph. */
  protected readonly glyph = computed(() => TONE_GLYPHS[this.tone()]);

  /** The hidden tone prefix ("Warning:"), shared with tm-alert. */
  protected readonly tonePrefix = computed(() => this.translate(`alert.${this.tone()}`)());

  /** "1 of 3" while the host has more than one message, else `null`. */
  protected readonly pager = computed(() => {
    const position = this.position();
    return position === null || position.count <= 1
      ? null
      : this.translate('banner.position', { index: position.index, count: position.count })();
  });

  /** "Previous message". */
  protected readonly previousLabel = this.translate('banner.previous');

  /** "Next message". */
  protected readonly nextLabel = this.translate('banner.next');

  /** "Dismiss". */
  protected readonly dismissLabel = this.translate('banner.dismiss');
}
