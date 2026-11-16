// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 *
 * `treeRows`, `treeRowId`, `treeTile`, `treeParentId`, `treeHasChildren`,
 * `loadTreeChildren` and the two-way signal are members of the consuming
 * component, which imports `TmTilesTree` beside `TmTiles` — a template
 * literal cannot express function-valued inputs. Real usage looks like:
 *
 * ```ts
 * readonly treeRowId = (row: Account): number => row.id;
 * readonly treeParentId = (row: Account): number | null => row.parentId;
 * readonly treeHasChildren = (row: Account): boolean => row.hasChildren;
 * readonly treeTile: TmTileLayout<Account> = { title: (a) => a.name, bottomStart: (a) => a.code };
 * readonly loadTreeChildren = (rows: readonly Account[]): Promise<void> => this.store.loadChildren(rows);
 * readonly treeExpandedIds = signal<ReadonlySet<TmRowId>>(new Set());
 * ```
 */

/** A tree of tiles over flat rows: `parentId` makes the hierarchy, and children load in batches. */
export const Tree = {
  template: `
    <tm-tiles
      style="block-size: 320px"
      ariaLabel="Accounts"
      [data]="treeRows"
      [rowId]="treeRowId"
      [layout]="treeTile"
      [parentId]="treeParentId"
      [hasChildren]="treeHasChildren"
      [loadChildren]="loadTreeChildren"
      [(expandedIds)]="treeExpandedIds"
    />
  `,
};
