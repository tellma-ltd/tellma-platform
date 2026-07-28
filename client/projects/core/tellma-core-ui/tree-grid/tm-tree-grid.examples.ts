// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * `treeRows`, `treeRowId`, `treeParentId`, `treeHasChildren`, and
 * `loadTreeChildren` are members of the consuming component — a template
 * literal cannot express function-valued inputs, and `rowId`/`parentId`
 * are required. Real usage looks like:
 *
 * ```ts
 * readonly treeRows: readonly Account[] = [...]; // flat adjacency list
 * readonly treeRowId = (row: Account): number => row.id;
 * readonly treeParentId = (row: Account): number | null => row.parentId;
 * readonly treeHasChildren = (row: Account): boolean => row.hasChildren;
 * readonly loadTreeChildren = async (row: Account): Promise<void> => {
 *   const children = await this.api.childrenOf(row.id);
 *   this.accounts.update((rows) => [...rows, ...children]); // append; the grid re-derives
 * };
 * ```
 */

/**
 * A tree grid over a FLAT adjacency-list array: bind `data` + `rowId` like
 * `tm-grid`, add `parentId`, and the hierarchy renders in the first column
 * (indentation + expander). Rows whose parent id does not resolve render
 * as roots.
 */
export const ReadonlyTreeGrid = {
  template: `
    <tm-tree-grid
      gridId="accounts-tree"
      [data]="treeRows"
      [rowId]="treeRowId"
      [parentId]="treeParentId"
      style="block-size: 320px"
    >
      <tm-grid-column key="name" header="Account" [flex]="2" />
      <tm-grid-column key="qty" type="number" header="Entries" [width]="100" />
    </tm-tree-grid>
  `,
};

/**
 * Lazy children: `hasChildren` marks rows that can expand before any child
 * is loaded, and expanding one calls `loadChildren` — append the fetched
 * rows to your own array and resolve; a spinner shows in reserved space
 * beside the expander meanwhile. `defaultExpandedDepth` seeds how deep the
 * tree starts expanded (here: roots only).
 */
export const LazyLoadedChildren = {
  template: `
    <tm-tree-grid
      gridId="accounts-lazy"
      [data]="treeRows"
      [rowId]="treeRowId"
      [parentId]="treeParentId"
      [hasChildren]="treeHasChildren"
      [loadChildren]="loadTreeChildren"
      [defaultExpandedDepth]="1"
      style="block-size: 320px"
    >
      <tm-grid-column key="name" header="Account" [flex]="2" />
      <tm-grid-column key="qty" type="number" header="Entries" [width]="100" />
    </tm-tree-grid>
  `,
};

/**
 * The hierarchy renders in the column marked `hierarchy` instead of the
 * first, and `defaultExpandedDepth` 0 starts fully collapsed. Expand and
 * collapse from the keyboard with Alt+ArrowRight / Alt+ArrowLeft on any
 * cell of the row.
 */
export const HierarchyColumnAndCollapsedStart = {
  template: `
    <tm-tree-grid
      gridId="accounts-compact"
      [data]="treeRows"
      [rowId]="treeRowId"
      [parentId]="treeParentId"
      [defaultExpandedDepth]="0"
      style="block-size: 320px"
    >
      <tm-grid-column key="qty" type="number" header="Entries" [width]="100" />
      <tm-grid-column key="name" header="Account" hierarchy [flex]="2" />
    </tm-tree-grid>
  `,
};
