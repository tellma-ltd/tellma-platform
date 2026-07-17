// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestKey, type TestElement } from '@angular/cdk/testing';

import { TmGridHarness } from './grid-harness';

/**
 * Harness for `tm-tree-grid`: everything {@link TmGridHarness} does, plus
 * the tree affordances — row levels and expansion state from the ARIA
 * attributes, expand/collapse through the accessible Alt+Arrow keyboard
 * path (the expander button is pointer-only), the expander click, and the
 * lazy-loading spinner. Row coordinates are view-space indices over the
 * VISIBLE (expanded) sequence, exactly as in the base harness.
 */
export class TmTreeGridHarness extends TmGridHarness {
  /** The selector for the `tm-tree-grid` host element. */
  static override hostSelector = 'tm-tree-grid';

  /** The rendered row at a view-space row index (throws when not rendered). */
  private async dataRow(rowIndex: number): Promise<TestElement> {
    return this.locatorFor(`[role="row"][aria-rowindex="${rowIndex + 2}"]`)();
  }

  /** The row's 1-based tree depth (`aria-level`). */
  async getLevel(rowIndex: number): Promise<number> {
    return Number(await (await this.dataRow(rowIndex)).getAttribute('aria-level'));
  }

  /**
   * Whether the row is expanded (`aria-expanded`). `false` for collapsed
   * AND non-expandable rows — leaves carry no `aria-expanded` at all.
   */
  async isExpanded(rowIndex: number): Promise<boolean> {
    return (await (await this.dataRow(rowIndex)).getAttribute('aria-expanded')) === 'true';
  }

  /**
   * Expands the row through the accessible path: activates a cell of the
   * row, then presses Alt+ArrowRight.
   */
  async expand(rowIndex: number): Promise<void> {
    await this.clickCell(rowIndex, 0);
    await this.sendAltArrow(TestKey.RIGHT_ARROW);
  }

  /**
   * Collapses the row through the accessible path: activates a cell of
   * the row, then presses Alt+ArrowLeft.
   */
  async collapse(rowIndex: number): Promise<void> {
    await this.clickCell(rowIndex, 0);
    await this.sendAltArrow(TestKey.LEFT_ARROW);
  }

  /**
   * Clicks the row's expander button (the pointer-only toggle in the
   * hierarchy column). Throws when the row renders no expander.
   */
  async clickExpander(rowIndex: number): Promise<void> {
    await (await this.gridElement()).focus();
    const expander = await this.locatorFor(
      `[role="row"][aria-rowindex="${rowIndex + 2}"] [data-tm-expander]`,
    )();
    await expander.click();
  }

  /** Whether the row's lazy children are loading (reserved-slot spinner). */
  async isLoadingChildren(rowIndex: number): Promise<boolean> {
    const spinner = await this.locatorForOptional(
      `[role="row"][aria-rowindex="${rowIndex + 2}"] [data-tm-childspin]`,
    )();
    return spinner !== null;
  }

  /**
   * Count of visible rows (the expanded flattening, the placeholder row
   * included) — `aria-rowcount` minus the column-header row.
   */
  async getVisibleRowCount(): Promise<number> {
    return (await this.getRowCount()) - 1;
  }

  /** Presses Alt+Arrow on the active cell (or the container). */
  private async sendAltArrow(key: TestKey): Promise<void> {
    const active = await this.locatorForOptional('[role="gridcell"][tabindex="0"]')();
    const target = active ?? (await this.gridElement());
    await target.focus();
    await target.sendKeys({ alt: true }, key);
  }
}
