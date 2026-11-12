// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  Component,
  computed,
  inject,
  input,
  model,
  ViewEncapsulation,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { ChevronDown, ChevronRight } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmCounter } from '@tellma/core-ui/badge';
import { TmIcon } from '@tellma/core-ui/icon';

/** The heading levels a {@link TmCard} can render its heading at. */
export type TmCardHeadingLevel = 2 | 3 | 4 | 5 | 6;

/**
 * `invalid`'s transform: `undefined` (and `null`) mean "not bound", which
 * reserves no box for the error dot; anything else is read as a boolean
 * attribute, so a bare `invalid` attribute is `true`.
 */
function invalidAttribute(value: unknown): boolean | undefined {
  return value === undefined || value === null ? undefined : booleanAttribute(value);
}

let nextCardId = 0;

/**
 * Frames a block of content under an optional heading: card fill, a
 * hairline and medium radius, no shadow. The header holds the heading, an
 * optional count, and the `[tmCardActions]` content at its inline end;
 * `[tmCardSubtitle]` adds a second header line in 12 px secondary text,
 * whose first child ellipsizes while the rest keep their width (a
 * description before "Updated 10:42"). The body is the projected content,
 * and `[tmCardFooter]` content follows it.
 *
 * A `collapsible` card is a disclosure: its heading becomes a button with a
 * chevron that toggles `expanded`. A collapsed body leaves the DOM, unless
 * `preserveContent` keeps it rendered, `hidden` and `inert`, so the fields
 * inside stay registered with their form. (Content projected into a card
 * is created by its parent either way; a body that should not exist until
 * first shown belongs in a `@defer` block.)
 *
 * `invalid` draws a 6 px error dot after the heading, expanded and
 * collapsed, in a box every card with `invalid` bound reserves, so the dot
 * never moves the header; the heading's accessible name then reads
 * "{heading}, has errors".
 *
 * @tmGroup layout
 * @tmA11yNotes The heading is a native `h2`–`h6` (`headingLevel`). A
 *   collapsible card follows the disclosure pattern: the heading holds a
 *   native button with `aria-expanded` and `aria-controls`, toggled by
 *   Enter or Space. While `invalid`, the button's (or the plain heading's)
 *   name is "{heading}, has errors"; the dot itself is hidden from
 *   assistive technology. A collapsed body kept by `preserveContent` is
 *   `hidden` and `inert`, so nothing inside is focusable or read.
 */
@Component({
  selector: 'tm-card',
  imports: [NgTemplateOutlet, TmCounter, TmIcon],
  // The stylesheet reaches the projected slot content (the subtitle's
  // children, the actions), so it is not encapsulated; every selector is
  // namespaced under .tm-card.
  encapsulation: ViewEncapsulation.None,
  template: `
    <!-- The header: the heading row (heading, count, actions), then the
         subtitle line. The stylesheet hides it when it holds nothing. -->
    <div class="tm-card__header">
      <div class="tm-card__heading-row">
        @if (heading() !== undefined) {
          <!-- A native heading at the requested level. -->
          @switch (headingLevel()) {
            @case (3) {
              <h3 class="tm-card__heading"><ng-container [ngTemplateOutlet]="headingContent" /></h3>
            }
            @case (4) {
              <h4 class="tm-card__heading"><ng-container [ngTemplateOutlet]="headingContent" /></h4>
            }
            @case (5) {
              <h5 class="tm-card__heading"><ng-container [ngTemplateOutlet]="headingContent" /></h5>
            }
            @case (6) {
              <h6 class="tm-card__heading"><ng-container [ngTemplateOutlet]="headingContent" /></h6>
            }
            @default {
              <h2 class="tm-card__heading"><ng-container [ngTemplateOutlet]="headingContent" /></h2>
            }
          }
          @if (count() !== null) {
            <tm-counter class="tm-card__count" [value]="count()" />
          }
        }
        <div class="tm-card__actions"><ng-content select="[tmCardActions]" /></div>
      </div>
      <div class="tm-card__subtitle"><ng-content select="[tmCardSubtitle]" /></div>
    </div>

    <!-- The collapsible region: the body, then the footer. A collapsed body
         leaves the DOM unless preserveContent keeps it hidden and inert. -->
    @if (regionRendered()) {
      <div
        class="tm-card__region"
        [id]="regionId"
        [hidden]="collapsed()"
        [attr.inert]="collapsed() ? '' : null"
      >
        <div class="tm-card__body"><ng-content /></div>
        <div class="tm-card__footer"><ng-content select="[tmCardFooter]" /></div>
      </div>
    }

    <!-- The heading's content: the disclosure button when collapsible,
         else the title itself. -->
    <ng-template #headingContent>
      @if (collapsible()) {
        <button
          type="button"
          class="tm-card__toggle"
          [attr.aria-expanded]="expanded()"
          [attr.aria-controls]="regionRendered() ? regionId : null"
          (click)="toggle()"
        >
          <tm-icon class="tm-card__chevron" [svg]="expanded() ? expandedGlyph : collapsedGlyph" />
          <ng-container [ngTemplateOutlet]="title" />
        </button>
      } @else {
        <ng-container [ngTemplateOutlet]="title" />
      }
    </ng-template>

    <!-- The title: while invalid, the visible text is hidden from assistive
         technology and a visually hidden "{heading}, has errors" names the
         heading instead. The dot's box exists whenever invalid is bound. -->
    <ng-template #title>
      <span class="tm-card__title" [attr.aria-hidden]="invalid() ? 'true' : null">{{
        heading()
      }}</span>
      @if (invalid()) {
        <span class="tm-card__error-name">{{ errorName() }}</span>
      }
      @if (invalid() !== undefined) {
        <span class="tm-card__dot" aria-hidden="true"></span>
      }
    </ng-template>
  `,
  styleUrl: './tm-card.css',
  host: {
    class: 'tm-card',
    '[class.tm-card--collapsed]': 'collapsed()',
    '[class.tm-card--flush]': 'flush()',
    '[class.tm-card--invalid]': 'invalid() === true',
  },
})
export class TmCard {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The heading text; unset, the card has no heading (and cannot collapse). */
  readonly heading = input<string | undefined>(undefined);

  /** The heading's level, `h2` to `h6`. Default 2. */
  readonly headingLevel = input<TmCardHeadingLevel>(2);

  /** A count shown in an inline counter after the heading; `null` (default), none. */
  readonly count = input<number | null>(null);

  /** Makes the card a disclosure: the heading toggles the body. */
  readonly collapsible = input(false, { transform: booleanAttribute });

  /** Whether a collapsible card's body shows. Default `true`; two-way bindable. */
  readonly expanded = model(true);

  /**
   * Keeps a collapsed body rendered, `hidden` and `inert`, instead of
   * taking it out of the DOM — so form fields inside stay registered.
   * Default `false`.
   */
  readonly preserveContent = input(false, { transform: booleanAttribute });

  /**
   * Draws the error dot after the heading and names the heading
   * "{heading}, has errors". Bound at all (even to `false`), the card
   * reserves the dot's box, so the dot coming and going moves nothing.
   */
  readonly invalid = input<boolean | undefined, unknown>(undefined, {
    transform: invalidAttribute,
  });

  /** Removes the body's padding, for content that fills the card (a grid). */
  readonly flush = input(false, { transform: booleanAttribute });

  /** The region's id, the disclosure button's `aria-controls`. */
  protected readonly regionId = `tm-card-region-${nextCardId++}`;

  /** The chevron of an expanded card. */
  protected readonly expandedGlyph = ChevronDown;

  /** The chevron of a collapsed card, pointing along the reading direction. */
  protected readonly collapsedGlyph = { node: ChevronRight, mirrorInRtl: true };

  /** Whether the body is collapsed (only a collapsible card collapses). */
  protected readonly collapsed = computed(() => this.collapsible() && !this.expanded());

  /** Whether the body region is in the DOM. */
  protected readonly regionRendered = computed(() => !this.collapsed() || this.preserveContent());

  /** "{heading}, has errors": the heading's name while the card is invalid. */
  protected readonly errorName = computed(() =>
    this.translate('card.hasErrors', { heading: this.heading() ?? '' })(),
  );

  /** Toggles a collapsible card. */
  protected toggle(): void {
    this.expanded.set(!this.expanded());
  }
}
