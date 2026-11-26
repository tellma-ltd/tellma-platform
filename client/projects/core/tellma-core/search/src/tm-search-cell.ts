// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, input, type InputSignal } from '@angular/core';
import type { TmColumnPlan, TmPlanRow } from '@tellma/core';

// The cell hatch (spec 0030 §2.2): a column declared `{ path, display }`
// names a lazy component, which the host loads beside the rows and renders
// in each of the column's cells with these three inputs. A cell is static:
// no focusable content but a link, one line at the row's height, no
// fetching, and only selected paths read.

/**
 * The base of a column's custom cell, which a `display` hatch's component
 * extends: the cell's value, its row and its column.
 *
 * @example
 * ```ts
 * @Component({ selector: 'acme-credit-cell', imports: [TmProgressBar],
 *   template: `<tm-progress-bar [value]="percent()" [label]="text()" />` })
 * export default class CreditCell extends TmSearchCell<TmDecimal> {
 *   readonly percent = computed(() => Number(this.value()) / 1000);
 *   readonly text = computed(() => String(this.value()));
 * }
 * ```
 */
@Directive()
export abstract class TmSearchCell<V = unknown> {
  /** The cell's value: the column's value of the row; in a tile, the row's id. */
  readonly value: InputSignal<V> = input.required<V>();

  /** The row. */
  readonly row: InputSignal<TmPlanRow> = input.required<TmPlanRow>();

  /** The column; `undefined` in a tile. */
  readonly column: InputSignal<TmColumnPlan | undefined> = input<TmColumnPlan | undefined>(undefined);
}
