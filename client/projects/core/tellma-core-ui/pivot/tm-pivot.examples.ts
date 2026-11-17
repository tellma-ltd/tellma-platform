// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * `pivotModel` and the two-way signals are members of the consuming
 * component — a template literal cannot express a model of callbacks. Real
 * usage looks like:
 *
 * ```ts
 * readonly pivotModel: TmPivotModel = {
 *   rows,                                    // pre-order members, the total (id 0) included
 *   columns,
 *   measures: ['Amount'],
 *   rowLabel: 'Center',
 *   columnLevelLabels: ['Quarter', 'Month'],
 *   attributeLabels: [],
 *   text: (row, column, measure) => this.format(cells.get(row * stride + column)?.[measure]),
 *   widest: (measure) => this.format(widestValue[measure]),
 * };
 * ```
 */

/** A pivot over two member trees: Enter or a click on a value cell drills, a twisty expands. */
export const Basic = {
  template: `
    <tm-pivot
      style="block-size: 360px"
      ariaLabel="Sales by center"
      [model]="pivotModel"
      [(expandedRows)]="expandedRows"
      [(expandedColumns)]="expandedColumns"
      (cellActivate)="onCell($event)"
    />
  `,
};

/** The user's column widths kept in page state, so a resize survives a new result. */
export const RememberedWidths = {
  template: `
    <tm-pivot
      style="block-size: 360px"
      ariaLabel="Sales by center"
      [model]="pivotModel"
      [(widths)]="pivotWidths"
    />
  `,
};

/** A pivot that only reads: value cells are not drill targets. */
export const ReadOnly = {
  template: `
    <tm-pivot
      style="block-size: 360px"
      ariaLabel="Sales by center"
      [model]="pivotModel"
      [activatable]="false"
      size="md"
    />
  `,
};
