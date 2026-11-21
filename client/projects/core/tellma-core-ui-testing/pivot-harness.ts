// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  HarnessPredicate,
  TestKey,
  type BaseHarnessFilters,
  type ModifierKeys,
} from '@angular/cdk/testing';

/** The keys a pivot answers to. */
export type TmPivotKey =
  | 'ArrowUp'
  | 'ArrowDown'
  | 'ArrowLeft'
  | 'ArrowRight'
  | 'Home'
  | 'End'
  | 'PageUp'
  | 'PageDown'
  | 'Enter'
  | 'Space'
  | 'c';

/** The CDK test keys for each pivot key. */
const TEST_KEYS: Record<TmPivotKey, TestKey | string> = {
  ArrowUp: TestKey.UP_ARROW,
  ArrowDown: TestKey.DOWN_ARROW,
  ArrowLeft: TestKey.LEFT_ARROW,
  ArrowRight: TestKey.RIGHT_ARROW,
  Home: TestKey.HOME,
  End: TestKey.END,
  PageUp: TestKey.PAGE_UP,
  PageDown: TestKey.PAGE_DOWN,
  Enter: TestKey.ENTER,
  Space: ' ',
  c: 'c',
};

/** The five looks a pivot cell is drawn in. */
export type TmPivotCellStyle = 'total' | 'subtotal-1' | 'subtotal-2' | 'phantom' | 'leaf';

/** What a pivot cell is: a column header (corner included), a row header, or a value or attribute cell. */
export type TmPivotCellRole = 'columnheader' | 'rowheader' | 'gridcell';

/** The look classes, strongest first. */
const STYLES: readonly TmPivotCellStyle[] = ['total', 'subtotal-1', 'subtotal-2', 'phantom', 'leaf'];

/** Filters for {@link TmPivotCellHarness}. */
export interface TmPivotCellHarnessFilters extends BaseHarnessFilters {
  /** The cell's full text, or a pattern it matches. */
  text?: string | RegExp;
  /** The cell's role. */
  role?: TmPivotCellRole;
  /** The cell's first row in the pivot's grid, from 0: header rows first. */
  row?: number;
  /** The cell's first column in the pivot's grid, from 0: the row header first. */
  column?: number;
}

/** Harness for one rendered cell of a `tm-pivot`: a header, a row header, an attribute or a value. */
export class TmPivotCellHarness extends ComponentHarness {
  /** The selector for a rendered cell (spacers and gaps excluded). */
  static hostSelector = '.tm-pivot__table [data-c]';

  /** Gets a predicate that finds cells by text, role or position. */
  static with(options: TmPivotCellHarnessFilters = {}): HarnessPredicate<TmPivotCellHarness> {
    return new HarnessPredicate(TmPivotCellHarness, options)
      .addOption('text', options.text, (harness, text) => HarnessPredicate.stringMatches(harness.getText(), text))
      .addOption('role', options.role, async (harness, role) => (await harness.getRole()) === role)
      .addOption('row', options.row, async (harness, row) => (await harness.getRow()) === row)
      .addOption('column', options.column, async (harness, column) => (await harness.getColumn()) === column);
  }

  /** The twisty, on an expandable header. */
  private readonly twisty = this.locatorForOptional('.tm-pivot__twisty[data-tm-toggle]');
  /** The resize handle, on a header that resizes a column. */
  private readonly handle = this.locatorForOptional('[data-tm-resize]');

  /** The cell's full text, whitespace collapsed: what it shows when uncut, and what Mod+C copies. */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).replace(/\s+/g, ' ').trim();
  }

  /** The cell's role. */
  async getRole(): Promise<TmPivotCellRole> {
    return (await (await this.host()).getAttribute('role')) as TmPivotCellRole;
  }

  /** The cell's first row in the pivot's grid, from 0. */
  async getRow(): Promise<number> {
    return Number(await (await this.host()).getAttribute('data-r'));
  }

  /** The cell's first column in the pivot's grid, from 0. */
  async getColumn(): Promise<number> {
    return Number(await (await this.host()).getAttribute('data-c'));
  }

  /** The look the cell is drawn in, or `null` for a corner cell. */
  async getStyle(): Promise<TmPivotCellStyle | null> {
    const host = await this.host();
    for (const style of STYLES) {
      if (await host.hasClass(`tm-pivot__cell--${style}`)) {
        return style;
      }
    }
    return null;
  }

  /** Whether the cell's member has children: a header with `aria-expanded`, or a row header of such a row. */
  async isExpandable(): Promise<boolean> {
    return (await this.twisty()) !== null;
  }

  /** Whether the cell's member shows its children. */
  async isExpanded(): Promise<boolean> {
    const twisty = await this.twisty();
    return twisty !== null && (await twisty.hasClass('tm-pivot__twisty--open'));
  }

  /** Whether the cell is a value cell that drills on activation. */
  async isActivatable(): Promise<boolean> {
    return (await this.host()).hasClass('tm-pivot__value--link');
  }

  /** Whether the cell is a value cell the model refuses to drill. */
  async isRefused(): Promise<boolean> {
    return (await this.host()).hasClass('tm-pivot__value--refused');
  }

  /** Whether the cell is the pivot's one tab stop. */
  async isTabStop(): Promise<boolean> {
    return (await (await this.host()).getAttribute('tabindex')) === '0';
  }

  /** Whether the cell has focus. */
  async isFocused(): Promise<boolean> {
    return (await this.host()).isFocused();
  }

  /** Whether the cell sticks to the inline start while the pivot scrolls sideways. */
  async isSticky(): Promise<boolean> {
    return (await this.host()).hasClass('tm-pivot__sticky');
  }

  /** Whether a header carries a resize handle. */
  async isResizable(): Promise<boolean> {
    return (await this.handle()) !== null;
  }

  /** The cell's rendered inline size, in px. */
  async getWidth(): Promise<number> {
    return (await (await this.host()).getDimensions()).width;
  }

  /** Clicks the cell; with Mod, a value cell asks for a new tab. */
  async click(modifiers: ModifierKeys = {}): Promise<void> {
    await (await this.host()).click('center', modifiers);
  }

  /** Expands or collapses the cell's member through its twisty. */
  async toggle(): Promise<void> {
    const twisty = await this.twisty();
    if (twisty === null) {
      throw new Error('This pivot cell does not expand.');
    }
    await twisty.click();
  }

  /** Double-clicks the cell's resize handle, fitting its column. */
  async fitWidth(): Promise<void> {
    const handle = await this.handle();
    if (handle === null) {
      throw new Error('This pivot cell has no resize handle.');
    }
    await handle.dispatchEvent('dblclick');
  }

  /** Focuses the cell. */
  async focus(): Promise<void> {
    await (await this.host()).focus();
  }

  /** Presses a key on the cell, with optional modifiers. */
  async press(key: TmPivotKey, modifiers: ModifierKeys = {}): Promise<void> {
    await (await this.host()).sendKeys(modifiers, TEST_KEYS[key]);
  }
}

/** Filters for {@link TmPivotHarness}. */
export interface TmPivotHarnessFilters extends BaseHarnessFilters {
  /** The pivot's accessible name, or a pattern it matches. */
  label?: string | RegExp;
}

/**
 * Harness for `tm-pivot`: its counts, its header and row labels, its
 * rendered cells, its sticky columns and its tab stop.
 */
export class TmPivotHarness extends ComponentHarness {
  /** The pivot's host selector. */
  static hostSelector = 'tm-pivot';

  /** Gets a predicate that finds pivots by accessible name. */
  static with(options: TmPivotHarnessFilters = {}): HarnessPredicate<TmPivotHarness> {
    return new HarnessPredicate(TmPivotHarness, options).addOption('label', options.label, (harness, label) =>
      HarnessPredicate.stringMatches(harness.getLabel(), label),
    );
  }

  /** The treegrid table. */
  private readonly table = this.locatorFor('table[role="treegrid"]');

  /** The pivot's accessible name. */
  async getLabel(): Promise<string> {
    return (await (await this.table()).getAttribute('aria-label')) ?? '';
  }

  /** Every row the pivot has (`aria-rowcount`): the header rows and every visible body row. */
  async getRowCount(): Promise<number> {
    return Number(await (await this.table()).getAttribute('aria-rowcount'));
  }

  /** Every column the pivot has (`aria-colcount`): the row header, the attributes and every data column. */
  async getColumnCount(): Promise<number> {
    return Number(await (await this.table()).getAttribute('aria-colcount'));
  }

  /** The header row count. */
  async getHeaderRowCount(): Promise<number> {
    return (await this.locatorForAll('thead > tr')()).length;
  }

  /** The labels of the rendered body rows, in order. */
  async getRowLabels(): Promise<string[]> {
    const headers = await this.locatorForAll('tbody [role="rowheader"]')();
    return Promise.all(headers.map(async (header) => (await header.text()).replace(/\s+/g, ' ').trim()));
  }

  /** Each header row's rendered header texts, corner included, in order. */
  async getHeaderTexts(): Promise<string[][]> {
    const rows = await this.locatorForAll('thead > tr')();
    const texts: string[][] = [];
    for (let row = 0; row < rows.length; row++) {
      const cells = await this.locatorForAll(`thead > tr:nth-child(${row + 1}) > [role="columnheader"]`)();
      texts.push(await Promise.all(cells.map(async (cell) => (await cell.text()).replace(/\s+/g, ' ').trim())));
    }
    return texts;
  }

  /** The rendered cells that match a filter. */
  async getCells(filter: TmPivotCellHarnessFilters = {}): Promise<TmPivotCellHarness[]> {
    return this.locatorForAll(TmPivotCellHarness.with(filter))();
  }

  /** The first rendered cell that matches a filter; throws when none does. */
  async getCell(filter: TmPivotCellHarnessFilters = {}): Promise<TmPivotCellHarness> {
    return this.locatorFor(TmPivotCellHarness.with(filter))();
  }

  /** The value cell of a rendered body row, by the row's label and the data column, from 0. */
  async getValueCell(rowLabel: string | RegExp, dataColumn: number): Promise<TmPivotCellHarness> {
    const row = await (await this.getCell({ role: 'rowheader', text: rowLabel })).getRow();
    // The data columns start after the row header and the attribute columns.
    return this.getCell({ row, column: 1 + (await this.getAttributeCount()) + dataColumn });
  }

  /** The cell holding the pivot's tab stop. */
  async getTabStop(): Promise<TmPivotCellHarness> {
    return this.locatorFor(TmPivotCellHarness.with({ selector: '[tabindex="0"]' }))();
  }

  /** The attribute column count. */
  async getAttributeCount(): Promise<number> {
    return (await this.locatorForAll('thead .tm-pivot__corner--attribute')()).length;
  }

  /** How many leading columns stick to the inline start: the row header, then attributes in order. */
  async getStickyColumnCount(): Promise<number> {
    // Every header row repeats the row header's corner; each attribute's corner spans them all.
    const rowHeader = await this.locatorForOptional('thead .tm-pivot__corner--row.tm-pivot__sticky')();
    const attributes = await this.locatorForAll('thead .tm-pivot__corner--attribute.tm-pivot__sticky')();
    return (rowHeader === null ? 0 : 1) + attributes.length;
  }

  /** Presses a key on the tab stop, with optional modifiers. */
  async press(key: TmPivotKey, modifiers: ModifierKeys = {}): Promise<void> {
    await (await this.getTabStop()).press(key, modifiers);
  }
}
