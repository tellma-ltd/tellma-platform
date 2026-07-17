// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * `rows`, `rowId`, `statuses`, and `total` are members of the consuming
 * component — a template literal cannot express function-valued inputs, and
 * `rowId` is required. Real usage looks like:
 *
 * ```ts
 * readonly rows: readonly Product[] = [...];
 * readonly rowId = (row: Product): number => row.id;
 * readonly statuses: readonly string[] = ['Draft', 'Posted', 'Void'];
 * readonly total = (row: Product): number => row.qty * row.price;
 * ```
 */

/**
 * A readonly grid over a rows array: bind `data` + `rowId`, give the grid a
 * stable `gridId`, and declare one `tm-grid-column` per column in display
 * order. The `type` picks formatting, alignment, and clipboard behavior.
 */
export const ReadonlyGrid = {
  template: `
    <tm-grid gridId="products" [data]="rows" [rowId]="rowId" style="block-size: 320px">
      <tm-grid-column key="name" header="Name" [flex]="2" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="90" />
      <tm-grid-column key="active" type="boolean" header="Active" [width]="80" />
    </tm-grid>
  `,
};

/**
 * An `enum` column maps raw values to option labels (add `optionLabel` /
 * `optionValue` when options are objects), and a column without `key` is a
 * readonly computed column driven by the `value` accessor.
 */
export const EnumAndComputedColumns = {
  template: `
    <tm-grid gridId="order-lines" [data]="rows" [rowId]="rowId" style="block-size: 320px">
      <tm-grid-column key="name" header="Item" [flex]="1" />
      <tm-grid-column key="status" type="enum" header="Status" [options]="statuses" [width]="110" />
      <tm-grid-column type="number" header="Total" [value]="total" [width]="110" />
    </tm-grid>
  `,
};

/**
 * A custom cell rendered through `*tmGridDisplay` — here a record link.
 * The context gives the cell's value, the row, and its `rowId`; copy and
 * find still use the column's text representation.
 */
export const RecordLinkColumn = {
  template: `
    <tm-grid gridId="invoices" [data]="rows" [rowId]="rowId" style="block-size: 320px">
      <tm-grid-column key="name" header="Invoice" [flex]="1">
        <a *tmGridDisplay="let value; let id = rowId" [attr.href]="'/invoices/' + id">{{ value }}</a>
      </tm-grid-column>
      <tm-grid-column key="qty" type="number" header="Lines" [width]="90" />
    </tm-grid>
  `,
};

/**
 * The built-in overlays and their template overrides: `loading` keeps the
 * headers and sets `aria-busy`; a bound, loaded, zero-row readonly grid
 * shows the empty state (`*tmGridEmpty` / `*tmGridLoading` replace the
 * defaults).
 */
export const LoadingAndEmptyStates = {
  template: `
    <tm-grid gridId="search-results" [data]="[]" [rowId]="rowId" style="block-size: 200px">
      <tm-grid-column key="name" header="Name" [flex]="1" />
      <span *tmGridEmpty>No results — widen the date range.</span>
    </tm-grid>

    <tm-grid gridId="report-lines" [data]="[]" [rowId]="rowId" loading style="block-size: 200px">
      <tm-grid-column key="name" header="Name" [flex]="1" />
      <span *tmGridLoading>Crunching the report…</span>
    </tm-grid>
  `,
};
