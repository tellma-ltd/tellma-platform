// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, input } from '@angular/core';

import { TM_FORM_FIELD_DEFAULTS, type TmControlSize } from '@tellma/core-ui';

/** The outline a {@link TmSkeleton} draws. */
export type TmSkeletonShape = 'line' | 'block' | 'circle' | 'table';

/** The count to render: a whole number of at least 1, else the fallback. */
function count(value: number | undefined, fallback: number): number {
  return value === undefined || !Number.isFinite(value) || value < 1 ? fallback : Math.floor(value);
}

/** The indexes `0 … n - 1`, for the template's loops. */
function range(n: number): number[] {
  return Array.from({ length: n }, (_, index) => index);
}

/**
 * A static placeholder for content that is still loading, drawn with the
 * final geometry of what replaces it, so nothing moves when it arrives.
 *
 * - `line`: `lines` bars of text, 8 px tall; with more than one, the last
 *   is shorter, like the end of a paragraph.
 * - `block`: a rectangle `width` by `height`; without a `height` it fills
 *   its container's height (a chart or pivot frame).
 * - `circle`: a circle `width` across (an avatar's place).
 * - `table`: a header row, then `lines` rows of `columns` bars at the data
 *   grid's row height for the workspace's default size (a list's place).
 *
 * It does not shimmer: the page sets `aria-busy` and announces "Loading"
 * once, and the skeleton itself says nothing.
 *
 * @tmGroup indicator
 * @tmA11yNotes Hidden from assistive technology (`aria-hidden`); the page
 *   region it sits in carries `aria-busy="true"` and the one polite
 *   "Loading" status. Static, so reduced motion needs nothing.
 */
@Component({
  selector: 'tm-skeleton',
  // A block or a circle is the host itself; a line is a stack of bars; a
  // table is a header row and body rows of one bar per cell.
  template: `
    @switch (shape()) {
      @case ('line') {
        @for (index of lineIndexes(); track index) {
          <span class="tm-skeleton__bar"></span>
        }
      }
      @case ('table') {
        <span class="tm-skeleton__row tm-skeleton__row--header">
          @for (column of columnIndexes(); track column) {
            <span class="tm-skeleton__cell"><span class="tm-skeleton__bar"></span></span>
          }
        </span>
        @for (row of rowIndexes(); track row) {
          <span class="tm-skeleton__row">
            @for (column of columnIndexes(); track column) {
              <span class="tm-skeleton__cell"><span class="tm-skeleton__bar"></span></span>
            }
          </span>
        }
      }
    }
  `,
  styleUrl: './tm-skeleton.css',
  host: {
    class: 'tm-skeleton',
    'aria-hidden': 'true',
    '[class.tm-skeleton--line]': 'shape() === "line"',
    '[class.tm-skeleton--block]': 'shape() === "block"',
    '[class.tm-skeleton--circle]': 'shape() === "circle"',
    '[class.tm-skeleton--table]': 'shape() === "table"',
    '[attr.data-size]': 'shape() === "table" ? rowSize : null',
    '[style.inline-size]': 'width()',
    '[style.block-size]': 'height() ?? null',
    '[style.--tm-skeleton-columns]': 'columnIndexes().length',
  },
})
export class TmSkeleton {
  /** The outline. Default `line`. */
  readonly shape = input<TmSkeletonShape>('line');

  /** The width, as a CSS length. Default `100%`. */
  readonly width = input('100%');

  /**
   * The height, as a CSS length. Unset, a line and a table take their
   * natural height, a circle matches its width, and a block fills its
   * container's height.
   */
  readonly height = input<string | undefined>(undefined);

  /**
   * For `line`, how many bars (default 1); for `table`, how many body rows
   * (default 8).
   */
  readonly lines = input<number | undefined>(undefined);

  /** For `table`, the cells per row. Default 4. */
  readonly columns = input(4);

  /** The data grid's size step, which sets a table's row height. */
  protected readonly rowSize: TmControlSize = inject(TM_FORM_FIELD_DEFAULTS).size;

  /** The bars of a `line` skeleton. */
  protected readonly lineIndexes = computed(() => range(count(this.lines(), 1)));

  /** The body rows of a `table` skeleton. */
  protected readonly rowIndexes = computed(() => range(count(this.lines(), 8)));

  /** The cells of each `table` row. */
  protected readonly columnIndexes = computed(() => range(count(this.columns(), 4)));
}
