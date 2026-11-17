// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TmButton } from '@tellma/core-ui/button';
import { TmChart, type TmChartPointActivation, type TmChartSpec } from '@tellma/core-ui/chart';

/** Months of the second half of 2026. */
const MONTHS = ['Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'].map((label) => ({ label: `${label} 2026` }));

/** Customer types. */
const TYPES = [{ label: 'Corporate' }, { label: 'Retail' }, { label: 'Government' }];

/** A deterministic amount per month and customer type. */
function amount(month: number, type: number): number {
  return 40000 + ((month * 7919 + type * 104729) % 50000);
}

/** Sales by month and customer type, a row per point. */
const BY_MONTH_AND_TYPE = MONTHS.flatMap((_, month) =>
  TYPES.map((_, type) => [month, type, amount(month, type)] as const),
);

/** Sales by month alone. */
const BY_MONTH = MONTHS.map((_, month) => [month, null, amount(month, 0)] as const);

/** The sales measure: whole numbers. */
const SALES = { label: 'Sales', format: { maxDecimals: 0 } };

/** Stacked columns: months by customer type, with a target line. */
const COLUMN: TmChartSpec = {
  type: 'column',
  title: 'Sales by month',
  dimensions: [
    { label: 'Month', members: MONTHS },
    { label: 'Customer type', members: TYPES },
  ],
  measure: SALES,
  rows: BY_MONTH_AND_TYPE,
  options: { stack: 'stacked', thresholds: [{ value: 200000, tone: 'positive' }] },
};

/** Bars: accounts by code and name, labelled. */
const BAR: TmChartSpec = {
  type: 'bar',
  title: 'Balances by account',
  dimensions: [
    {
      label: 'Account',
      members: [
        { label: '1100 Cash', parts: ['1100', ' ', 'Cash'] },
        { label: '1200 Receivables', parts: ['1200', ' ', 'Receivables'] },
        { label: '2100 Payables', parts: ['2100', ' ', 'Payables'] },
      ],
    },
  ],
  measure: SALES,
  rows: [
    [0, null, 125000],
    [1, null, 98000],
    [2, null, -42000],
  ],
  options: { labels: true, palette: 'diverging' },
};

/** Lines with markers, a series per customer type. */
const LINE: TmChartSpec = {
  type: 'line',
  title: 'Sales trend',
  dimensions: COLUMN.dimensions,
  measure: SALES,
  rows: BY_MONTH_AND_TYPE,
  options: { markers: true, legend: 'top' },
};

/** A normalized area stack. */
const AREA: TmChartSpec = {
  type: 'area',
  title: 'Sales mix',
  dimensions: COLUMN.dimensions,
  measure: SALES,
  rows: BY_MONTH_AND_TYPE,
  options: { stack: 'normalized', legend: 'end' },
};

/** A pie, labelled with each slice's share. */
const PIE: TmChartSpec = {
  type: 'pie',
  title: 'Sales by customer type',
  dimensions: [{ label: 'Customer type', members: TYPES }],
  measure: SALES,
  rows: TYPES.map((_, type) => [type, null, amount(0, type)] as const),
  options: { labels: true },
};

/** A doughnut on the sequential ramp. */
const DOUGHNUT: TmChartSpec = {
  type: 'doughnut',
  title: 'Sales by month',
  dimensions: [{ label: 'Month', members: MONTHS }],
  measure: SALES,
  rows: BY_MONTH,
  options: { palette: 'sequential', legend: 'start' },
};

/** One value with thresholds; the reached one shows as a badge. */
const CARD: TmChartSpec = {
  type: 'card',
  title: 'Revenue this month',
  dimensions: [],
  measure: SALES,
  rows: [[null, null, 168500]],
  options: {
    thresholds: [
      { value: 150000, tone: 'positive' },
      { value: 250000, tone: 'negative' },
    ],
  },
};

/** No points: the host's no-data state shows. */
const EMPTY: TmChartSpec = { ...COLUMN, title: 'Nothing yet', rows: [] };

/** Arabic months and customer types, with a negative month. */
const ARABIC: TmChartSpec = {
  type: 'column',
  title: 'المبيعات حسب الشهر',
  dimensions: [
    { label: 'الشهر', members: [{ label: 'يوليو' }, { label: 'أغسطس' }, { label: 'سبتمبر' }] },
    {
      label: 'نوع العميل',
      members: [{ label: 'شركات' }, { label: 'B2C أفراد', parts: ['B2C', ' ', 'أفراد'] }],
    },
  ],
  measure: { label: 'المبيعات', format: { maxDecimals: 0 } },
  rows: [
    [0, 0, 120000],
    [0, 1, -35000],
    [1, 0, 98000],
    [1, 1, 41000],
    [2, 0, 143000],
    [2, 1, 52000],
  ],
  options: { labels: true },
};

/** Charts formatted in Egyptian Arabic, whose digits are Arabic-Indic. */
@Component({
  selector: 'app-demo-chart-arabic',
  imports: [TmButton, TmChart],
  providers: [{ provide: TM_ACTIVE_LOCALE, useValue: signal('ar-EG') }],
  template: `
    <tm-chart class="demo-chart" data-testid="chart-arabic" [spec]="spec" [(asTable)]="asTable" />
    <button tmButton variant="secondary" data-testid="chart-arabic-table" (click)="asTable.set(!asTable())">
      {{ asTable() ? 'عرض كمخطط' : 'عرض كجدول' }}
    </button>
  `,
  styles: `
    .demo-chart {
      block-size: 280px;
    }
  `,
})
class ArabicCharts {
  /** The chart's spec. */
  readonly spec = ARABIC;
  /** Whether the table view shows. */
  readonly asTable = signal(false);
}

/**
 * tm-chart: every type, each palette, the legend's sides, a threshold, a
 * card with a reached threshold, a no-data state, the table view's toggle
 * and a chart in Arabic. The last drill shows below.
 */
@Component({
  imports: [ArabicCharts, TmButton, TmChart],
  template: `
    <h2>Chart (tm-chart)</h2>

    <!-- The table toggle for the first chart. -->
    <div class="demo-actions">
      <button tmButton variant="secondary" data-testid="chart-toggle-table" (click)="asTable.set(!asTable())">
        {{ asTable() ? 'Show as chart' : 'Show as table' }}
      </button>
    </div>

    <!-- Every type, two to a row. -->
    <div class="demo-grid">
      <tm-chart
        class="demo-chart"
        data-testid="chart-column"
        gridId="showcase-chart-column"
        [spec]="column"
        [(asTable)]="asTable"
        (pointActivate)="last.set($event)"
      />
      <tm-chart class="demo-chart" data-testid="chart-bar" [spec]="bar" (pointActivate)="last.set($event)" />
      <tm-chart class="demo-chart" data-testid="chart-line" [spec]="line" (pointActivate)="last.set($event)" />
      <tm-chart class="demo-chart" data-testid="chart-area" [spec]="area" (pointActivate)="last.set($event)" />
      <tm-chart class="demo-chart" data-testid="chart-pie" [spec]="pie" (pointActivate)="last.set($event)" />
      <tm-chart
        class="demo-chart"
        data-testid="chart-doughnut"
        [spec]="doughnut"
        (pointActivate)="last.set($event)"
      />
      <tm-chart data-testid="chart-card" [spec]="card" (pointActivate)="last.set($event)" />
      <tm-chart class="demo-chart" data-testid="chart-empty" [spec]="empty">
        <p class="demo-empty">No sales yet.</p>
      </tm-chart>
    </div>

    <!-- A chart in Arabic. -->
    <h3>Arabic</h3>
    <app-demo-chart-arabic />

    <!-- The last drill, for the browser tests to read. -->
    <p>
      Last activation:
      <output data-testid="chart-activation">{{ describe(last()) }}</output>
    </p>
  `,
  styles: `
    .demo-actions {
      display: flex;
      gap: 8px;
      margin-block-end: 12px;
    }

    .demo-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(420px, 1fr));
      gap: 16px;
    }

    .demo-chart {
      block-size: 280px;
    }

    .demo-empty {
      color: var(--text-secondary);
    }
  `,
})
export class ChartStory {
  /** The stacked columns. */
  readonly column = COLUMN;
  /** The labelled bars. */
  readonly bar = BAR;
  /** The lines. */
  readonly line = LINE;
  /** The normalized areas. */
  readonly area = AREA;
  /** The pie. */
  readonly pie = PIE;
  /** The doughnut. */
  readonly doughnut = DOUGHNUT;
  /** The card. */
  readonly card = CARD;
  /** The chart without points. */
  readonly empty = EMPTY;
  /** Whether the first chart shows its table view. */
  readonly asTable = signal(false);
  /** The last point opened. */
  readonly last = signal<TmChartPointActivation | null>(null);

  /** The last activation as text: its members, and whether it asked for a new tab. */
  describe(activation: TmChartPointActivation | null): string {
    if (activation === null) {
      return 'none';
    }
    const [first, second] = activation.members;
    return `${first ?? '-'}/${second ?? '-'}${activation.newTab ? ' (new tab)' : ''}`;
  }
}
