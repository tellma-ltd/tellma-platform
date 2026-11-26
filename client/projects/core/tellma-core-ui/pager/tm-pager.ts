// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { booleanAttribute, Component, computed, inject, input, model, output } from '@angular/core';
import ChevronLeft from 'lucide/dist/esm/icons/chevron-left.mjs';
import ChevronRight from 'lucide/dist/esm/icons/chevron-right.mjs';

import {
  TM_ACTIVE_LOCALE,
  TM_UI_ARGUMENT_FORMATTER,
  TM_UI_TRANSLATE,
  type TmControlSize,
  ɵtmControlSize,
} from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmOption, TmSelect } from '@tellma/core-ui/select';

import {
  ɵtmPagerReservedCh,
  ɵtmPagerStatus,
  ɵtmPagerWidestStatus,
  type ɵTmPagerStatus,
} from './internal/tm-pager-status';

/**
 * Paging status and controls. Range mode pages through a list and shows
 * "1–20 / 55"; item mode steps through single items and shows "3 / 293".
 *
 * In range mode the buttons write `page` (1-based) and the size select
 * writes `pageSize`; in item mode the buttons emit `previous` and `next`
 * and the host moves `index`. A click while a page loads writes `page`
 * again, so a consumer's `resource()` supersedes the earlier request: the
 * pager never waits.
 *
 * The status reserves the width of the widest text its total can produce
 * ("120–120 / 120" for 120 items), and the size select the width of its
 * widest option, so paging never moves the buttons. A capped count
 * (`totalIsLowerBound`) reads "1–20 / 1,000+" and keeps Next enabled;
 * `maxPage` disables Next from that page on, even then. An unknown total
 * hides the status and keeps Next enabled.
 *
 * @tmGroup collection
 * @tmA11yNotes Native buttons named "Previous page" and "Next page" (or
 *   "Previous item" and "Next item"), and a `tm-select` named "Page
 *   size". The status reads as a sentence, "Items 1 to 20 of 55"; its
 *   visible fraction is hidden from assistive technology and set
 *   left-to-right in both directions.
 */
@Component({
  selector: 'tm-pager',
  imports: [TmButton, TmIcon, TmOption, TmSelect],
  template: `
    <!-- The status: the visible fraction (left-to-right, tabular) and the
         sentence a screen reader reads instead. Its width is reserved for
         the widest status of the total, so paging never moves what
         follows. -->
    <span class="tm-pager__status" [style.min-inline-size.ch]="reservedCh()">
      @if (status(); as current) {
        <span class="tm-pager__fraction" dir="ltr" aria-hidden="true">{{ current.text }}</span>
        <span class="tm-pager__sentence">{{ current.sentence }}</span>
      }
    </span>

    <!-- Previous and next, with chevrons that mirror in right-to-left text. -->
    <button
      tmButton
      type="button"
      variant="ghost"
      class="tm-pager__button tm-pager__previous"
      [size]="resolvedSize()"
      [disabled]="previousDisabled()"
      [attr.aria-label]="previousLabel()"
      (click)="onPrevious()"
    >
      <tm-icon [svg]="previousGlyph" />
    </button>
    <button
      tmButton
      type="button"
      variant="ghost"
      class="tm-pager__button tm-pager__next"
      [size]="resolvedSize()"
      [disabled]="nextDisabled()"
      [attr.aria-label]="nextLabel()"
      (click)="onNext()"
    >
      <tm-icon [svg]="nextGlyph" />
    </button>

    <!-- The size select, in range mode with sizes to offer. A hidden copy
         of every option's text shares its grid cell, so the select is as
         wide as its widest option whatever it shows. -->
    @if (showsSizes()) {
      <span class="tm-pager__size">
        <tm-select
          class="tm-pager__select"
          [value]="pageSize()"
          [size]="resolvedSize()"
          [disabled]="disabled()"
          [aria-label]="pageSizeLabel()"
          (selectionChange)="onPageSize($event)"
        >
          @for (option of sizeOptions(); track option.value) {
            <tm-option [value]="option.value" [label]="option.label">{{ option.label }}</tm-option>
          }
        </tm-select>
        <span class="tm-pager__size-sizer" aria-hidden="true">
          @for (option of sizeOptions(); track option.value) {
            <span>{{ option.label }}</span>
          }
        </span>
      </span>
    }
  `,
  styleUrl: './tm-pager.css',
  host: {
    class: 'tm-pager',
    '[attr.data-size]': 'resolvedSize()',
    '[attr.data-mode]': 'mode()',
  },
})
export class TmPager {
  /** The library's string resolver. */
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The ICU argument formatter, for the short size labels' bare numbers. */
  private readonly formatArgument = inject(TM_UI_ARGUMENT_FORMATTER);

  /** The formatting locale the size labels' digits follow. */
  private readonly locale = inject(TM_ACTIVE_LOCALE);

  /** `range` pages through a list ("1–20 / 55"); `item` steps through items ("3 / 293"). Default `range`. */
  readonly mode = input<'range' | 'item'>('range');

  /** Range mode: the current page, 1-based. The buttons write it. */
  readonly page = model(1);

  /** Range mode: the items per page. The size select writes it. */
  readonly pageSize = model(20);

  /** Range mode: the sizes the select offers; empty, no size select. Default none. */
  readonly pageSizes = input<readonly number[]>([]);

  /** Item mode: the current item's position, 1-based. */
  readonly index = input(1);

  /** The number of items, in both modes; `null` when unknown (the status hides and Next stays enabled). */
  readonly total = input<number | null>(null);

  /** Whether `total` is a capped count: "1–20 / 1,000+", "52 / 10,000+", and Next stays enabled. */
  readonly totalIsLowerBound = input(false, { transform: booleanAttribute });

  /**
   * Range mode: the last page Next may reach; Next is disabled from this
   * page on, even with a lower-bound total. `null`, no cap.
   */
  readonly maxPage = input<number | null>(null);

  /** The size select's text: `long` reads "50 per page", `short` reads "50". Default `long`. */
  readonly sizeLabel = input<'short' | 'long'>('long');

  /** Disables the buttons and the size select; the pager keeps its geometry. */
  readonly disabled = input(false, { transform: booleanAttribute });

  /** The size step of the buttons and the select. Unset, the workspace default (`lg` on a phone). */
  readonly size = input<TmControlSize | undefined>(undefined);

  /** Item mode: the previous button was clicked. */
  readonly previous = output<void>();

  /** Item mode: the next button was clicked. */
  readonly next = output<void>();

  /** The size step the pager renders at. */
  protected readonly resolvedSize = ɵtmControlSize(this.size);

  /** The previous glyph, mirrored in right-to-left text so it points back along the line. */
  protected readonly previousGlyph = { node: ChevronLeft, mirrorInRtl: true };

  /** The next glyph, mirrored in right-to-left text. */
  protected readonly nextGlyph = { node: ChevronRight, mirrorInRtl: true };

  /** The status's visible text and its sentence, or `null` while the total is unknown. */
  protected readonly status = computed(() => {
    const status = ɵtmPagerStatus({
      mode: this.mode(),
      page: this.page(),
      pageSize: this.pageSize(),
      index: this.index(),
      total: this.total(),
      totalIsLowerBound: this.totalIsLowerBound(),
    });
    return status === null ? null : { text: this.text(status), sentence: this.sentence(status) };
  });

  /** The status's reserved width in `ch`: the widest text the total can produce. */
  protected readonly reservedCh = computed(() => {
    const total = this.total();
    if (total === null) {
      return 0;
    }
    // Past a lower-bound total, range mode reaches the last row of maxPage.
    const maxPage = this.maxPage();
    const reach =
      this.mode() === 'range' && maxPage !== null ? maxPage * Math.max(1, this.pageSize()) : null;
    const widest = ɵtmPagerWidestStatus(this.mode(), total, this.totalIsLowerBound(), reach);
    return ɵtmPagerReservedCh(this.text(widest));
  });

  /** Whether Previous is disabled: at the first page or item, or while the pager is disabled. */
  protected readonly previousDisabled = computed(
    () => this.disabled() || (this.mode() === 'range' ? this.page() : this.index()) <= 1,
  );

  /**
   * Whether Next is disabled: at `maxPage` (range mode), at the end of a
   * known exact total, or while the pager is disabled.
   */
  protected readonly nextDisabled = computed(() => {
    if (this.disabled()) {
      return true;
    }
    const total = this.total();
    const exact = total !== null && !this.totalIsLowerBound();
    if (this.mode() === 'item') {
      return exact && this.index() >= total;
    }
    const maxPage = this.maxPage();
    if (maxPage !== null && this.page() >= maxPage) {
      return true;
    }
    return exact && this.page() * this.pageSize() >= total;
  });

  /** "Previous page", or "Previous item" in item mode. */
  protected readonly previousLabel = computed(() =>
    this.translate(this.mode() === 'range' ? 'pager.previousPage' : 'pager.previousItem')(),
  );

  /** "Next page", or "Next item" in item mode. */
  protected readonly nextLabel = computed(() =>
    this.translate(this.mode() === 'range' ? 'pager.nextPage' : 'pager.nextItem')(),
  );

  /** The size select's name, "Page size". */
  protected readonly pageSizeLabel = computed(() => this.translate('pager.pageSize')());

  /** Whether the size select shows: range mode with sizes to offer. */
  protected readonly showsSizes = computed(
    () => this.mode() === 'range' && this.pageSizes().length > 0,
  );

  /** The size select's options: each size with its text, "20 per page" or "20". */
  protected readonly sizeOptions = computed(() => {
    const long = this.sizeLabel() === 'long';
    const locale = this.locale();
    return this.pageSizes().map((value) => ({
      value,
      label: long
        ? this.translate('pager.perPage', { size: value })()
        : this.formatArgument('number', value, '', locale),
    }));
  });

  /** Range mode writes the previous page; item mode asks the host to step back. */
  protected onPrevious(): void {
    if (this.mode() === 'range') {
      this.page.set(Math.max(1, this.page() - 1));
    } else {
      this.previous.emit();
    }
  }

  /** Range mode writes the next page; item mode asks the host to step on. */
  protected onNext(): void {
    if (this.mode() === 'range') {
      this.page.set(this.page() + 1);
    } else {
      this.next.emit();
    }
  }

  /**
   * Writes the chosen page size, and the page that keeps the first visible
   * row in view: page 3 at 50 (rows 101–150) becomes page 6 at 20 (rows
   * 101–120).
   */
  protected onPageSize(size: number): void {
    const previous = this.pageSize();
    if (size === previous || size <= 0) {
      return;
    }
    const firstRow = (Math.max(1, this.page()) - 1) * Math.max(1, previous);
    this.pageSize.set(size);
    this.page.set(Math.floor(firstRow / size) + 1);
  }

  /** The status's visible text, in the locale's digits. */
  private text(status: ɵTmPagerStatus): string {
    return this.translate(`pager.${status.key}`, status.params)();
  }

  /** The status's hidden sentence, "Items 1 to 20 of 55". */
  private sentence(status: ɵTmPagerStatus): string {
    const key = status.key.replace(/^(range|item)/, '$1Sentence');
    return this.translate(`pager.${key}`, status.params)();
  }
}
