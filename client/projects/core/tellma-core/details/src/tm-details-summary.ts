// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  type InputSignal,
  type Signal,
  signal,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { TmAlert } from '@tellma/core-ui/alert';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon, TmIconRegistry } from '@tellma/core-ui/icon';
import { TmLocalization } from '@tellma/core';
import Check from 'lucide/dist/esm/icons/check.mjs';

// The error summary (spec 0031 §8.4): every problem that blocked a save, in
// the status strip. Collapsed, it is one row, the heading and Show problems;
// expanded, its list drops over the band, at most half the band's height,
// and scrolls inside. A failed Save opens it expanded with focus on the
// heading. Escape, Tab past its end, or activating an item collapses it. An
// item never leaves while the user edits: once its error clears, it shows
// a check and "Fixed" in its box, so nothing moves under the pointer.

/** One problem the summary lists. */
export interface TmDetailsSummaryItem {
  /** What it says: "{label}: {message}", or the child row's cell before the message. */
  readonly text: string;
  /** Whether its error has cleared since the save. */
  readonly fixed: Signal<boolean>;
  /** What activating it does, such as focusing its field; `null` for an item shown as plain text. */
  readonly activate: (() => void) | null;
}

/** The items listed at most; the rest are counted. */
const SHOWN = 10;

/** Counts the summaries, so each list gets its own id. */
let nextSummary = 0;

/** One failed save's summary: its count, the items it lists, and whether it is expanded. */
export class TmDetailsSummaryState {
  /** The problems the save found. */
  readonly count: number;

  /** The items listed, the first ten. */
  readonly items: readonly TmDetailsSummaryItem[];

  /** The problems beyond the listed ones. */
  readonly more: number;

  /** Whether the list shows. */
  readonly expanded = signal(true);

  /**
   * Holds one save's problems.
   *
   * @param items - Every problem, in order.
   */
  constructor(items: readonly TmDetailsSummaryItem[]) {
    this.count = items.length;
    this.items = items.slice(0, SHOWN);
    this.more = items.length - this.items.length;
  }
}

/** The error summary in the status strip. Internal: the details host renders it after a failed save. */
@Component({
  selector: 'tm-details-summary',
  imports: [TmAlert, TmButton, TmIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  template: `
    <!-- The row: the heading, which a failed Save focuses, and the list's toggle. -->
    <tm-alert class="tm-details-summary__alert" kind="error">
      <span class="tm-details-summary__row">
        <span #headingTarget class="tm-details-summary__heading" tabindex="-1" [id]="ids.heading">{{ heading() }}</span>
        <button
          #toggleTarget
          tmButton
          class="tm-details-summary__toggle"
          type="button"
          variant="ghost"
          size="sm"
          [attr.aria-expanded]="expanded()"
          [attr.aria-controls]="ids.list"
          (click)="toggle()"
        >
          {{ text('show') }}<tm-icon class="tm-details-summary__chevron" name="chevron-down" />
        </button>
      </span>
    </tm-alert>
    <!-- The list, over the band: each item a button that goes to its field, or plain text, with its Fixed box. -->
    @if (expanded()) {
      <ul class="tm-details-summary__list" [id]="ids.list" [attr.aria-labelledby]="ids.heading" [style.max-block-size.px]="maxHeight()">
        @for (item of summary().items; track $index) {
          <li class="tm-details-summary__item">
            @if (item.activate) {
              <button class="tm-details-summary__link" type="button" (click)="activate(item)">{{ item.text }}</button>
            } @else {
              <span class="tm-details-summary__text">{{ item.text }}</span>
            }
            <span class="tm-details-summary__fixed">
              @if (item.fixed()) {
                <tm-icon name="check" />{{ text('fixed') }}
              }
            </span>
          </li>
        }
        @if (summary().more > 0) {
          <li class="tm-details-summary__more">{{ more() }}</li>
        }
      </ul>
    }
  `,
  styles: `
    /* The summary fills the strip's row; its list hangs below it. */
    .tm-details-summary {
      position: relative;
      display: block;
      flex: 1 1 auto;
      align-self: stretch;
      min-inline-size: 0;
    }

    .tm-details .tm-details-summary__alert.tm-alert {
      align-items: center;
      block-size: 100%;
      padding-block: 0;
      border-width: 0 0 var(--border-width);
      border-radius: 0;
    }

    .tm-details-summary__row {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      min-inline-size: 0;
    }

    .tm-details-summary__heading {
      flex: 1 1 auto;
      min-inline-size: 0;
      overflow: hidden;
      font-weight: var(--weight-semibold);
      color: var(--text-strong);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-details-summary__chevron {
      transition: transform var(--duration-fast);
    }

    .tm-details-summary__toggle[aria-expanded='true'] .tm-details-summary__chevron {
      transform: rotate(180deg);
    }

    /* Expanded: the list drops over the band and scrolls inside. */
    .tm-details-summary__list {
      position: absolute;
      inset-block-start: 100%;
      inset-inline: 0;
      margin: 0;
      padding: var(--space-1) 0;
      overflow: auto;
      list-style: none;
      background: var(--surface-card);
      box-shadow: var(--shadow-md);
    }

    .tm-details-summary__item,
    .tm-details-summary__more {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      min-block-size: 32px;
      padding-inline: var(--page-header-padding-x);
    }

    .tm-details-summary__link,
    .tm-details-summary__text {
      flex: 1 1 auto;
      min-inline-size: 0;
      padding: var(--space-1) 0;
      text-align: start;
      color: var(--text-body);
      overflow-wrap: anywhere;
    }

    .tm-details-summary__link {
      border: 0;
      background: none;
      font: inherit;
      color: var(--text-link);
      cursor: pointer;
    }

    .tm-details-summary__link:hover {
      text-decoration: underline;
    }

    /* The Fixed box keeps its size whether or not it shows, so fixing a field moves nothing. */
    .tm-details-summary__fixed {
      display: inline-flex;
      flex: none;
      align-items: center;
      gap: var(--space-1);
      min-inline-size: 5rem;
      color: var(--success);
    }

    .tm-details-summary__more {
      color: var(--text-muted);
    }
  `,
  host: { class: 'tm-details-summary', '(keydown)': 'keydown($event)' },
})
export class TmDetailsSummary {
  readonly #l10n = inject(TmLocalization);
  readonly #injector = inject(Injector);
  readonly #element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** The summary shown. */
  readonly summary: InputSignal<TmDetailsSummaryState> = input.required<TmDetailsSummaryState>();

  /** The heading and the list's ids. */
  protected readonly ids = { heading: `tm-details-summary-${nextSummary}`, list: `tm-details-summary-${nextSummary++}-list` };

  /** The heading, which a failed Save focuses. */
  protected readonly headingElement = viewChild.required<ElementRef<HTMLElement>>('headingTarget');

  /** The list's toggle, which takes focus when Escape closes the list. */
  protected readonly toggleElement = viewChild.required<ElementRef<HTMLElement>>('toggleTarget');

  /** Whether the list shows. */
  protected readonly expanded = computed(() => this.summary().expanded());

  /** The list's greatest height: half the band's, measured as it opens. */
  protected readonly maxHeight = signal<number | null>(null);

  /** The heading: how many problems block the save. */
  protected readonly heading = computed(() => this.#l10n.ɵtext('tm.detailsSave.heading', { count: this.summary().count })());

  /** The count of the problems beyond the listed ones. */
  protected readonly more = computed(() => this.#l10n.ɵtext('tm.detailsSave.more', { count: this.summary().more })());

  /** The summary's strings, by key under `tm.detailsSave`. */
  protected readonly text: (key: string) => string = (key) => this.#l10n.ɵtext(`tm.detailsSave.${key}`)();

  /** Registers its icon, and focuses the heading of each summary a failed save opens. */
  constructor() {
    const icons = inject(TmIconRegistry);
    if (icons.get('check') === undefined) {
      icons.register({ check: Check });
    }

    // --- Each new summary opens expanded, its heading focused (spec 0031 §8.4) ---
    effect(() => {
      this.summary();
      untracked(() =>
        afterNextRender(
          () => {
            this.#measure();
            this.headingElement().nativeElement.focus();
          },
          { injector: this.#injector },
        ),
      );
    });
  }

  /** Shows or hides the list. */
  protected toggle(): void {
    const expanded = this.summary().expanded;
    if (!untracked(expanded)) {
      this.#measure();
    }
    expanded.update((open) => !open);
  }

  /**
   * Runs an item: the list collapses, then the item goes to its field.
   *
   * @param item - The item.
   */
  protected activate(item: TmDetailsSummaryItem): void {
    this.summary().expanded.set(false);
    item.activate?.();
  }

  /**
   * Collapses the list on Escape, and on Tab past its last item.
   *
   * @param event - The key.
   */
  protected keydown(event: KeyboardEvent): void {
    const expanded = this.summary().expanded;
    if (!untracked(expanded)) {
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      expanded.set(false);
      this.toggleElement().nativeElement.focus();
      return;
    }
    if (event.key === 'Tab' && !event.shiftKey) {
      // Focus moves on past the summary as Tab does; the list closes behind it.
      const focusable = this.#element.querySelectorAll<HTMLElement>('button');
      if (focusable[focusable.length - 1] === event.target) {
        expanded.set(false);
      }
    }
  }

  /** Measures the band, so the list takes at most half its height. */
  #measure(): void {
    const body = this.#element.closest<HTMLElement>('.tm-details__body');
    this.maxHeight.set(body === null ? null : Math.floor(body.clientHeight / 2));
  }
}
