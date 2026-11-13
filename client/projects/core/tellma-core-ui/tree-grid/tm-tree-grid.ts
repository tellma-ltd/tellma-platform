// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, input, model } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import { TmGridBase, ɵTmGridView, type ɵTmGridTreeConfig } from '@tellma/core-ui/grid';

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
 * your own array. The expansion is remembered per content in the grid
 * state store, or kept by you through the two-way `expandedIds` with
 * `expansionSource="model"`. Keyboard expand/collapse is
 * Alt+ArrowRight/ArrowLeft on the active row. In editable mode the context menu adds "Insert child
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
 *   of the active cell moves activation (and focus) to that ancestor. A
 *   row whose children are loading is already `aria-expanded="true"`.
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
export class TmTreeGrid<T> extends TmGridBase<T> {
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
   * expander before any child exists. An expanded such row with no loaded
   * children has them loaded through `loadChildren`.
   */
  readonly hasChildren = input<((row: T) => boolean) | undefined>(undefined);
  /**
   * Loads the children of a batch of expanded rows: append the fetched rows
   * to the bound array (the grid never creates rows it did not mint) and
   * resolve. Every expanded `hasChildren` row with no loaded children that
   * the same moment collects arrives in one call, in data order — a
   * content's first bind, a restored or written `expandedIds`, the rows a
   * resolved batch brings (so a remembered deep expansion loads one batch
   * per level), or the expands a user makes within one task. The rows show
   * expanded with a spinner in reserved space beside the expander while
   * loading (no layout shift), and collapsing meanwhile is honored. A
   * rejection collapses the batch's rows, forgets their expansion, and
   * announces the failure once.
   */
  readonly loadChildren = input<((rows: readonly T[]) => Promise<void>) | undefined>(undefined);
  /**
   * How deep the tree starts expanded when content first binds: 0 = all
   * collapsed, 1 = roots expanded, … `undefined` = fully expanded. A
   * remembered expansion state (same `gridId` + `contentKey`) wins over
   * the default on restore. Ignored when `expansionSource` is `'model'`.
   */
  readonly defaultExpandedDepth = input<number | undefined>(undefined);
  /**
   * Who keeps the expansion, read when content binds. `'store'` (the
   * default): the grid state store remembers it per content, seeded from
   * `defaultExpandedDepth`, and `expandedIds` is neither read nor written.
   * `'model'`: `expandedIds` alone is the expansion, and the store's slice
   * is left untouched.
   */
  readonly expansionSource = input<'store' | 'model'>('store');
  /**
   * The expanded row ids, two-way, when `expansionSource` is `'model'`.
   * The set is kept whole: an id whose row is not loaded or not present yet
   * stays in it and expands when its row arrives. The grid emits a fresh
   * set on every change it makes (a user expand or collapse, an ancestor a
   * reveal expands, a failed load); a set you write replaces the expansion
   * — rows it leaves out collapse, rows it adds expand and load their
   * children — and is not echoed back.
   */
  readonly expandedIds = model<ReadonlySet<TmRowId>>(new Set());

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
      expansionSource: () => this.expansionSource(),
      expandedIds: () => this.expandedIds,
    };
  }
}
