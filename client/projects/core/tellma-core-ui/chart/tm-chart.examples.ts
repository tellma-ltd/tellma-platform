// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * The specs and the two-way signal are members of the consuming component.
 * Real usage looks like:
 *
 * ```ts
 * readonly salesSpec: TmChartSpec = {
 *   type: 'column',
 *   title: 'Sales by month',
 *   dimensions: [{ label: 'Month', members: [{ label: 'Jul' }, { label: 'Aug' }] }],
 *   measure: { label: 'Sales', format: { maxDecimals: 0 } },
 *   rows: [[0, null, 41000], [1, null, 52000]],   // [member, member, value] per point
 * };
 * readonly salesAsTable = signal(false);
 * ```
 */

/** A chart from its spec: the plot, named and described for assistive technology. */
export const Basic = {
  template: `
    <tm-chart style="block-size: 280px" [spec]="salesSpec" />
  `,
};

/** The table view kept in page state, behind a toggle the page owns. */
export const AsTable = {
  template: `
    <tm-chart style="block-size: 280px" [spec]="salesSpec" [(asTable)]="salesAsTable" />
    <button tmButton variant="secondary" (click)="salesAsTable.set(!salesAsTable())">
      {{ salesAsTable() ? 'Show as chart' : 'Show as table' }}
    </button>
  `,
};

/** A chart whose points drill: a click on a mark or Enter on a table row opens the point. */
export const Drilling = {
  template: `
    <tm-chart style="block-size: 280px" [spec]="salesSpec" (pointActivate)="onPoint($event)" />
  `,
};

/** One headline value as a card; a reached threshold shows as a badge. */
export const Card = {
  template: `
    <tm-chart [spec]="revenueCardSpec" />
  `,
};

/** No points: the projected content stands in for the plot. */
export const NoData = {
  template: `
    <tm-chart style="block-size: 280px" [spec]="emptySalesSpec">
      <p>No sales yet.</p>
    </tm-chart>
  `,
};
