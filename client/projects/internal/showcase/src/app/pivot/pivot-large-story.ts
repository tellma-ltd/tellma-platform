// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmPivot, type TmPivotMember, type TmPivotModel } from '@tellma/core-ui/pivot';

/** The row count: with the columns, 1,000 × 500 cells of one measure. */
const ROW_COUNT = 1000;
/** The column count. */
const COLUMN_COUNT = 500;

/** `count` first-level members, ids from 1, without a total. */
function members(count: number, prefix: string): readonly TmPivotMember[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1,
    parent: null,
    kind: 'member' as const,
    label: `${prefix} ${i + 1}`,
  }));
}

/** Two-decimal amounts with grouping. */
const FORMAT = new Intl.NumberFormat('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * tm-pivot over a 500,000-cell cube: the windows' subject. The page counts
 * the model's `text` calls, so a test can check that only rendered cells
 * are asked for.
 */
@Component({
  imports: [TmPivot],
  template: `
    <h2>Pivot (500,000 cells)</h2>

    <tm-pivot class="demo-pivot" data-testid="pivot-large" ariaLabel="A large cube" [model]="model" />

    <!-- The count of text calls so far. -->
    <p>
      Text calls:
      <output data-testid="pivot-text-calls">{{ calls() }}</output>
    </p>
  `,
  styles: `
    .demo-pivot {
      block-size: 60vh;
    }
  `,
})
export class PivotLargeStory {
  /** The model's `text` calls so far. */
  readonly calls = signal(0);

  /** The cube: every cell has a value. */
  readonly model: TmPivotModel = {
    rows: members(ROW_COUNT, 'Account'),
    columns: members(COLUMN_COUNT, 'Day'),
    measures: ['Amount'],
    rowLabel: 'Account',
    columnLevelLabels: ['Day'],
    attributeLabels: [],
    text: (row, column) => {
      // Counted after the render that asks, so the count never feeds back into it.
      queueMicrotask(() => this.calls.update((count) => count + 1));
      return FORMAT.format(((row * 7919 + column * 104729) % 1000003) / 100);
    },
    widest: () => FORMAT.format(10000.03),
  };
}
