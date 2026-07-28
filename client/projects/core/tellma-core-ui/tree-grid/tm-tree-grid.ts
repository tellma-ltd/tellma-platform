// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, input } from '@angular/core';

import { ɵTmGridBase, ɵTmGridView, type ɵTmGridTreeConfig } from '@tellma/core-ui/grid';

/**
 * Hierarchical data grid over a **flat** rows array (an adjacency list —
 * the natural shape of Tellma models and SQL results): `parentId` derives
 * the hierarchy, and everything `tm-grid` does — virtualized rendering,
 * Excel-style selection and keyboard navigation, spreadsheet clipboard
 * interop, undo, state memory — operates on the visible (expanded)
 * sequence unchanged. Rows with unresolvable or cyclic parents render as
 * roots (with a dev-mode warning) instead of disappearing.
 *
 * The hierarchy renders in the column marked `hierarchy` (the first column
 * by default): depth indentation, a pointer-only expander, and a reserved
 * spinner slot for lazy child loading — mark rows with `hasChildren` and
 * load on demand through `loadChildren`, appending the fetched rows to
 * your own array. Keyboard expand/collapse is Alt+ArrowRight/ArrowLeft on
 * the active row. In editable mode the context menu adds "Insert child
 * row" (`newRow(parent)` stamps the parent id), full-row cut/paste moves
 * whole subtrees re-parented through `parentIdKey`, and deleting a row
 * deletes its subtree.
 *
 * ```html
 * <tm-tree-grid gridId="accounts" [data]="accounts()" [rowId]="accountId" [parentId]="accountParentId">
 *   <tm-grid-column key="name" header="Name" [flex]="2" />
 *   <tm-grid-column key="balance" type="number" header="Balance" [width]="120" />
 * </tm-tree-grid>
 * ```
 *
 * @tmGroup grid
 * @tmA11yNotes The container is `role="treegrid"`; rows carry `aria-level`,
 *   `aria-expanded` (expandable rows), and `aria-posinset`/`aria-setsize`
 *   over their sibling sets, on top of the full virtualized
 *   `aria-rowcount`/`aria-colcount`. The expander button is pointer-only
 *   (`tabindex="-1"`, `aria-hidden`) — the accessible path is
 *   Alt+ArrowRight/ArrowLeft on the active row, and collapsing an ancestor
 *   of the active cell moves activation (and focus) to that ancestor.
 *   Lazy-load failures and row operations are announced through the live
 *   region in the active locale. Screen-reader verification beyond the
 *   ARIA mechanics remains a manual pass.
 */
@Component({
  selector: 'tm-tree-grid',
  imports: [ɵTmGridView],
  template: `<tm-grid-view [core]="core" />`,
  styleUrl: './tm-tree-grid.css',
  host: {
    class: 'tm-grid tm-grid--tree',
    '[class.tm-grid--readonly]': '!core.editable()',
    '[class.tm-grid--sm]': 'size() === "sm"',
    '[class.tm-grid--lg]': 'size() === "lg"',
    '[style.--grid-template]': 'core.gridTemplate()',
  },
})
export class TmTreeGrid<T> extends ɵTmGridBase<T> {
  /**
   * Reads a row's parent id; `null` marks a root. A parent id that does
   * not resolve to a bound row, or one that closes a cycle, renders the
   * row as a root and warns in dev mode.
   */
  readonly parentId = input.required<(row: T) => string | number | null>();
  /**
   * The model property holding the parent id. Required for editable
   * trees: full-row moves re-parent the moved subtree's root by writing
   * this property through the bound field, so validators run and undo
   * inverts it like any other cell write.
   */
  readonly parentIdKey = input<string | undefined>(undefined);
  /**
   * Marks rows whose children may not be loaded yet, so they render an
   * expander before any child exists. Expanding such a row with no loaded
   * children triggers `loadChildren`.
   */
  readonly hasChildren = input<((row: T) => boolean) | undefined>(undefined);
  /**
   * Loads a row's children on first expand: append the fetched rows to
   * the bound array (the grid never creates rows it did not mint) and
   * resolve — the node then expands over whatever children now exist. A
   * spinner occupies reserved space beside the expander while loading (no
   * layout shift), collapsing meanwhile is honored, and a rejection
   * restores the collapsed state and announces the failure.
   */
  readonly loadChildren = input<((row: T) => Promise<void>) | undefined>(undefined);
  /**
   * How deep the tree starts expanded when content first binds: 0 = all
   * collapsed, 1 = roots expanded, … `undefined` = fully expanded. A
   * remembered expansion state (same `gridId` + `contentKey`) wins over
   * the default on restore.
   */
  readonly defaultExpandedDepth = input<number | undefined>(undefined);

  /**
   * Hands the composition root this component's tree bindings. Deferred
   * closures by contract: this runs during the base constructor, before
   * the input fields above initialize.
   */
  protected override treeConfig(): ɵTmGridTreeConfig<T> {
    return {
      parentId: () => this.parentId(),
      parentIdKey: () => this.parentIdKey(),
      hasChildren: () => this.hasChildren(),
      loadChildren: () => this.loadChildren(),
      defaultExpandedDepth: () => this.defaultExpandedDepth(),
    };
  }
}
