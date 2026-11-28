// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmChart, type TmChartSpec } from '@tellma/core-ui/chart';

/** One value: a card needs no engine. */
const CARD: TmChartSpec = {
  type: 'card',
  title: 'Open orders',
  dimensions: [],
  measure: { label: 'Open orders' },
  rows: [[null, null, 42]],
};

/** The first plot on the page, which loads the engine. */
const COLUMN: TmChartSpec = {
  type: 'column',
  title: 'Orders by region',
  dimensions: [{ label: 'Region', members: [{ label: 'North' }, { label: 'South' }] }],
  measure: { label: 'Orders' },
  rows: [
    [0, null, 30],
    [1, null, 12],
  ],
};

/**
 * tm-chart's lazy engine: a card shows first, with no engine; the button
 * adds the page's first plot, which loads it.
 */
@Component({
  imports: [TmButton, TmChart],
  template: `
    <h2>Chart (the engine loads on the first plot)</h2>

    <tm-chart data-testid="chart-lazy-card" [spec]="card" />

    <div class="demo-actions">
      <button tmButton variant="secondary" data-testid="chart-lazy-add" (click)="plot.set(true)">
        Add a column chart
      </button>
    </div>

    @if (plot()) {
      <tm-chart class="demo-chart" data-testid="chart-lazy-column" [spec]="column" />
    }
  `,
  styles: `
    .demo-actions {
      margin-block: 12px;
    }

    .demo-chart {
      block-size: 240px;
      max-inline-size: 480px;
    }
  `,
})
export class ChartLazyStory {
  /** The card. */
  readonly card = CARD;
  /** The column chart. */
  readonly column = COLUMN;
  /** Whether the column chart is on the page. */
  readonly plot = signal(false);
}
