// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  type InputSignal,
  type InputSignalWithTransform,
  type OnInit,
  signal,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { TmCard } from '@tellma/core-ui/card';
import { TmLocalization } from '@tellma/core';

import { injectTmDetailsRecord, tmDevMode } from './tm-details-record';
import { TM_DETAILS_SCOPE, TmDetailsScope } from './tm-details-scope';

// A card of the details band (spec 0031 §6.1), on the component library's
// tm-card: a grid of fields, then the sections below it, such as tabs. It
// keeps a collapsed body rendered, hidden and inert, so its fields stay
// registered and a field's focus() can expand it; it shows an error dot
// while a field beneath it shows an error; and the record remembers its
// expanded state and its body's scroll by its id. Not encapsulated: its
// rules reach the layout's fields and sections; every selector is
// namespaced under .tm-details-card.

/**
 * A card of a details layout: an optional heading, a grid of `tm-field`s,
 * then the children marked `tmDetailsCardSection`. It can be half the
 * band's width, collapsible, of a fixed scrolling height, or flush.
 *
 * @example
 * ```html
 * <tm-details-card titleKey="Acme_Customer_Terms" width="half" columns="1">
 *   <tm-field path="CreditLimit" />
 *   <tm-field path="Currency" />
 * </tm-details-card>
 * ```
 */
@Component({
  selector: 'tm-details-card',
  imports: [TmCard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  providers: [{ provide: TM_DETAILS_SCOPE, useFactory: () => inject(TmDetailsCard).scope }],
  host: {
    class: 'tm-details-card',
    '[class.tm-details-card--half]': "width() === 'half'",
  },
  styles: `
    .tm-details-card {
      display: block;
      min-inline-size: 0;
    }

    /* The body: the grid, then the sections; a fixed size scrolls inside. */
    .tm-details-card__body {
      container-type: inline-size;
    }

    .tm-details-card__body--scrolls {
      overflow: auto;
    }

    /* The field grid: 10 × 18 px gaps and the card's body padding. */
    .tm-details-card__grid {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: 10px 18px;
      padding: var(--card-body-padding);
    }

    .tm-details-card__grid:empty {
      display: none;
    }

    .tm-details-card__grid--flush {
      padding: 0;
    }

    .tm-details-card__grid--2 {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }

    /* Two columns while the card is at least sm wide. */
    @container (width >= 576px) {
      .tm-details-card__grid--auto {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }

    /* Anything in the grid that is not a field spans every column. */
    .tm-details-card__grid > * {
      grid-column: 1 / -1;
    }

    /* A section: full width, unpadded, under a hairline unless it opens the body. */
    .tm-details-card__section {
      display: block;
      border-block-start: var(--border-width) solid var(--divider);
    }

    .tm-details-card__grid:empty + .tm-details-card__section {
      border-block-start: none;
    }
  `,
  template: `
    <tm-card
      [heading]="title()"
      [collapsible]="collapsible()"
      [(expanded)]="expanded"
      [invalid]="scope.invalid()"
      preserveContent
      flush
    >
      <!-- The body: the fields' grid, then the sections. -->
      <div
        #body
        class="tm-details-card__body"
        [class.tm-details-card__body--scrolls]="scrollable() !== undefined"
        [style.block-size.px]="scrollable() ?? null"
      >
        <div
          class="tm-details-card__grid"
          [class.tm-details-card__grid--flush]="flush()"
          [class.tm-details-card__grid--2]="columns() === 2"
          [class.tm-details-card__grid--auto]="columns() === 'auto'"
        >
          <ng-content />
        </div>
        <ng-content select="[tmDetailsCardSection]" />
      </div>
    </tm-card>
  `,
})
export class TmDetailsCard implements OnInit {
  readonly #l10n = inject(TmLocalization);
  readonly #record = injectTmDetailsRecord();
  readonly #destroyRef = inject(DestroyRef);
  readonly #injector = inject(Injector);

  /** The heading's key; no header when omitted. */
  readonly titleKey: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The card's id, under which the record remembers its state; required when it is collapsible or scrollable. */
  readonly id: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The card's width: the band's, or half of it while the band is at least md wide. */
  readonly width: InputSignal<'full' | 'half'> = input<'full' | 'half'>('full');

  /** The field grid's columns; `auto` is 2 while the card is at least sm wide, else 1. */
  readonly columns: InputSignal<1 | 2 | 'auto'> = input<1 | 2 | 'auto'>('auto');

  /** Whether the heading collapses and expands the body. */
  readonly collapsible: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether a collapsible card starts collapsed, unless the record remembers otherwise. */
  readonly collapsed: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** A fixed body height in pixels, which scrolls inside. */
  readonly scrollable: InputSignal<number | undefined> = input<number | undefined>(undefined);

  /** Whether the field grid has no padding. */
  readonly flush: InputSignalWithTransform<boolean, unknown> = input(false, { transform: booleanAttribute });

  /** Whether the body is expanded. */
  protected readonly expanded = signal(true);

  /** The heading, or `undefined` for none. */
  protected readonly title = computed(() => {
    const key = this.titleKey();
    return key === undefined ? undefined : this.#l10n.ɵtext(key)();
  });

  /** The body, which scrolls when the card is scrollable. */
  private readonly body = viewChild.required<ElementRef<HTMLElement>>('body');

  /** What the card holds: its fields' and sections' errors, and the expansion a focused field asks for. */
  readonly scope: TmDetailsScope = new TmDetailsScope(
    inject(TM_DETAILS_SCOPE, { optional: true, skipSelf: true }),
    () => this.expanded.set(true),
    this.#destroyRef,
  );

  /** Remembers the card's expanded state by its id. */
  constructor() {
    effect(() => {
      const expanded = this.expanded();
      const id = this.id();
      if (id !== undefined && this.collapsible()) {
        untracked(() => this.#record.view.cards.update((cards) => (cards[id] === expanded ? cards : { ...cards, [id]: expanded })));
      }
    });
  }

  /** Takes the remembered or the initial state, and registers a scrollable body. */
  ngOnInit(): void {
    const id = this.id();
    if (id === undefined && (this.collapsible() || this.scrollable() !== undefined) && tmDevMode()) {
      throw new Error('A collapsible or scrollable tm-details-card needs an id, under which the page remembers its state.');
    }
    if (this.collapsible()) {
      const remembered = id === undefined ? undefined : untracked(this.#record.view.cards)[id];
      this.expanded.set(remembered ?? !this.collapsed());
    }
    if (id !== undefined && this.scrollable() !== undefined) {
      afterNextRender(() => this.#record.registerScroller(id, this.body().nativeElement, this.#destroyRef), { injector: this.#injector });
    }
  }
}

/**
 * Marks a child of a `tm-details-card` that renders after the field grid,
 * full width, with no padding and a hairline above, such as the record's
 * tabs.
 *
 * @example
 * ```html
 * <tm-details-card>
 *   <tm-field path="Name" />
 *   <tm-details-tabs tmDetailsCardSection>…</tm-details-tabs>
 * </tm-details-card>
 * ```
 */
@Directive({ selector: '[tmDetailsCardSection]', host: { class: 'tm-details-card__section' } })
export class TmDetailsCardSection {
  /** The card the section belongs to, or `null` outside one. */
  readonly card: TmDetailsCard | null = inject(TmDetailsCard, { optional: true });
}
