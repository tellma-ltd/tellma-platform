// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ComponentHarness,
  HarnessPredicate,
  TestKey,
  type BaseHarnessFilters,
  type TestElement,
} from '@angular/cdk/testing';

import { TmMenuHarness } from './menu-harness';

/** How {@link TmGridHarness.openEditor} opens the editor session. */
export type TmGridEditorOpenVia = 'enter' | 'f2' | 'type';

/** How {@link TmGridHarness.commitEditor} commits the open session. */
export type TmGridEditorCommitVia = 'enter' | 'tab';

/** A cell address: view-space row index and data-column index (both 0-based). */
export interface TmGridCellCoordinate {
  /** The view-space row index (the `data-row` attribute). */
  readonly row: number;
  /** The data-column index (the `data-col` attribute). */
  readonly col: number;
}

/** Filters for locating a {@link TmGridCellHarness}. */
export interface TmGridCellHarnessFilters extends BaseHarnessFilters {
  /** The cell's view-space row index (matches the `data-row` attribute). */
  rowIndex?: number;
  /** The cell's data-column index (matches the `data-col` attribute). */
  colIndex?: number;
}

/** Filters for locating a {@link TmGridRowHarness}. */
export interface TmGridRowHarnessFilters extends BaseHarnessFilters {
  /**
   * The row's 1-based `aria-rowindex`. The column-header row is 1, so the
   * first data row is 2 (view-space row index + 2 in general).
   */
  ariaRowIndex?: number;
}

/**
 * Dispatches a modifier-carrying pointer press on an element. The CDK's
 * `TestElement.click` attaches modifier keys to the mouse events only, but
 * the grid's selection gestures read them from `pointerdown` — so the
 * harness synthesizes the pointer pair itself on the underlying DOM
 * element (available in DOM-backed environments such as the testbed).
 */
function dispatchPointerPress(target: TestElement, init: PointerEventInit): void {
  const element = (target as unknown as { element?: unknown }).element;
  if (!(element instanceof Element)) {
    throw new Error(
      'TmGridHarness: modifier clicks require a DOM-backed TestElement ' +
        '(e.g. TestbedHarnessEnvironment).',
    );
  }
  const base: PointerEventInit = {
    bubbles: true,
    cancelable: true,
    composed: true,
    button: 0,
    isPrimary: true,
    ...init,
  };
  element.dispatchEvent(new PointerEvent('pointerdown', base));
  element.dispatchEvent(new PointerEvent('pointerup', base));
}

/**
 * Harness for a single rendered `tm-grid` cell. Locate through
 * {@link TmGridHarness.getCell} or with {@link TmGridCellHarness.with}
 * filters; the grid virtualizes rows, so only rendered cells resolve.
 */
export class TmGridCellHarness extends ComponentHarness {
  /** The selector for a rendered grid cell. */
  static hostSelector = '[role="gridcell"]';

  /** Gets a predicate that filters cells by view coordinates. */
  static with(options: TmGridCellHarnessFilters = {}): HarnessPredicate<TmGridCellHarness> {
    return new HarnessPredicate(TmGridCellHarness, options)
      .addOption(
        'rowIndex',
        options.rowIndex,
        async (harness, rowIndex) => (await harness.getRowIndex()) === rowIndex,
      )
      .addOption(
        'colIndex',
        options.colIndex,
        async (harness, colIndex) => (await harness.getColIndex()) === colIndex,
      );
  }

  /** The cell's view-space row index (from `data-row`). */
  async getRowIndex(): Promise<number> {
    return Number(await (await this.host()).getAttribute('data-row'));
  }

  /** The cell's data-column index (from `data-col`). */
  async getColIndex(): Promise<number> {
    return Number(await (await this.host()).getAttribute('data-col'));
  }

  /** Gets the cell's rendered text, trimmed. */
  async getText(): Promise<string> {
    return (await (await this.host()).text()).trim();
  }

  /** Whether the cell is the active cell (holds the roving `tabindex="0"`). */
  async isActive(): Promise<boolean> {
    return (await (await this.host()).getAttribute('tabindex')) === '0';
  }

  /** Whether the cell lies inside a selection range (`aria-selected`). */
  async isSelected(): Promise<boolean> {
    return (await (await this.host()).getAttribute('aria-selected')) === 'true';
  }

  /** Gets the cell's resolved `text-align` (e.g. `right` for number columns). */
  async getAlign(): Promise<string> {
    return (await this.host()).getCssValue('text-align');
  }

  /** Clicks the cell (a real pointer press: activates and selects it). */
  async click(): Promise<void> {
    return (await this.host()).click();
  }

  /**
   * Double-clicks the cell: two real pointer presses followed by a
   * `dblclick`. A readonly grid does not react to it today.
   */
  async doubleClick(): Promise<void> {
    const host = await this.host();
    await host.click();
    await host.click();
    await host.dispatchEvent('dblclick');
  }
}

/**
 * Harness for a single rendered `tm-grid` row. Matches the column-header
 * row too (`aria-rowindex` 1); filter with
 * {@link TmGridRowHarness.with} to target a data row.
 */
export class TmGridRowHarness extends ComponentHarness {
  /** The selector for a rendered grid row (including the header row). */
  static hostSelector = '[role="row"]';

  /** Gets a predicate that filters rows by their 1-based `aria-rowindex`. */
  static with(options: TmGridRowHarnessFilters = {}): HarnessPredicate<TmGridRowHarness> {
    return new HarnessPredicate(TmGridRowHarness, options).addOption(
      'ariaRowIndex',
      options.ariaRowIndex,
      async (harness, ariaRowIndex) => (await harness.getAriaRowIndex()) === ariaRowIndex,
    );
  }

  /** The row's 1-based `aria-rowindex` (the column-header row is 1). */
  async getAriaRowIndex(): Promise<number> {
    return Number(await (await this.host()).getAttribute('aria-rowindex'));
  }

  /** Gets harnesses for the row's rendered cells, in column order. */
  async getCells(filter?: HarnessPredicate<TmGridCellHarness>): Promise<TmGridCellHarness[]> {
    return this.locatorForAll(filter ?? TmGridCellHarness)();
  }

  /**
   * Gets the row header's text: the 1-based row number, or `*` on the
   * new-row placeholder. Data rows only — the column-header row has none.
   */
  async getRowHeaderText(): Promise<string> {
    return (await (await this.locatorFor('[role="rowheader"]')()).text()).trim();
  }

  /** Whether the row is the new-row placeholder. */
  async isPlaceholder(): Promise<boolean> {
    const classes = (await (await this.host()).getAttribute('class')) ?? '';
    return classes.includes('tm-grid__row--placeholder');
  }
}

/**
 * Harness for `tm-grid`: reads the rendered structure (counts, headers,
 * cells, overlays) and drives it like a user (pointer presses with
 * modifiers, keyboard). The grid virtualizes rows, so cell lookups resolve
 * for rendered rows only; coordinates are view-space (`data-row`) and
 * data-column (`data-col`) indices, both 0-based.
 */
export class TmGridHarness extends ComponentHarness {
  /** The selector for the `tm-grid` host element. */
  static hostSelector = 'tm-grid';

  /** The grid's scroller (`role="grid"`, or `treegrid` on `tm-tree-grid`). */
  protected readonly gridElement = this.locatorFor('[role="grid"], [role="treegrid"]');

  /**
   * The full ARIA row count (`aria-rowcount`): all view rows — data rows
   * plus the new-row placeholder when present — plus the column-header row.
   */
  async getRowCount(): Promise<number> {
    return Number(await (await this.gridElement()).getAttribute('aria-rowcount'));
  }

  /**
   * The full ARIA column count (`aria-colcount`): the data columns plus the
   * row-header column.
   */
  async getColCount(): Promise<number> {
    return Number(await (await this.gridElement()).getAttribute('aria-colcount'));
  }

  /** Counts the currently rendered data rows (the virtualized window). */
  async getRenderedRowCount(): Promise<number> {
    return (await this.locatorForAll('[role="rowgroup"] [role="row"]')()).length;
  }

  /** Gets the column header labels, in display order (row-header corner excluded). */
  async getHeaderTexts(): Promise<string[]> {
    const headers = await this.locatorForAll('[role="columnheader"][data-col]')();
    return Promise.all(headers.map(async (header) => (await header.text()).trim()));
  }

  /** Gets the harness for the rendered cell at the given coordinates. */
  async getCell(rowIndex: number, colIndex: number): Promise<TmGridCellHarness> {
    return this.locatorFor(TmGridCellHarness.with({ rowIndex, colIndex }))();
  }

  /** Gets the trimmed text of the rendered cell at the given coordinates. */
  async getCellText(rowIndex: number, colIndex: number): Promise<string> {
    return (await this.getCell(rowIndex, colIndex)).getText();
  }

  /**
   * The coordinates of the active cell (the one holding the roving
   * `tabindex="0"`), or `null` when no cell is active — including while
   * focus is parked on the container after Escape.
   */
  async getActiveCell(): Promise<TmGridCellCoordinate | null> {
    const active = await this.locatorForOptional('[role="gridcell"][tabindex="0"]')();
    if (active === null) {
      return null;
    }
    return {
      row: Number(await active.getAttribute('data-row')),
      col: Number(await active.getAttribute('data-col')),
    };
  }

  /**
   * Clicks a cell like a user: focuses the grid, then presses the cell —
   * the cell becomes the active cell and the selection collapses to it.
   */
  async clickCell(rowIndex: number, colIndex: number): Promise<void> {
    await (await this.gridElement()).focus();
    await (await this.getCell(rowIndex, colIndex)).click();
  }

  /** Shift+clicks a cell: extends the selection from the anchor to it. */
  async shiftClickCell(rowIndex: number, colIndex: number): Promise<void> {
    await this.modifierClickCell(rowIndex, colIndex, { shiftKey: true });
  }

  /**
   * Mod+clicks a cell (Ctrl, or ⌘ on Apple platforms — both are sent, the
   * grid reads its platform's modifier): adds a selection range at the cell
   * without dropping the existing ranges.
   */
  async modClickCell(rowIndex: number, colIndex: number): Promise<void> {
    await this.modifierClickCell(rowIndex, colIndex, { ctrlKey: true, metaKey: true });
  }

  /** Selects a rectangular range: click `from`, then Shift+click `to`. */
  async selectRange(from: TmGridCellCoordinate, to: TmGridCellCoordinate): Promise<void> {
    await this.clickCell(from.row, from.col);
    await this.shiftClickCell(to.row, to.col);
  }

  /** Clicks a row header: selects the row and activates its first cell. */
  async clickRowHeader(rowIndex: number): Promise<void> {
    await (await this.gridElement()).focus();
    await (await this.locatorFor(`[role="rowheader"][data-row="${rowIndex}"]`)()).click();
  }

  /** Clicks a column header: selects the column and activates its top cell. */
  async clickColumnHeader(colIndex: number): Promise<void> {
    await (await this.gridElement()).focus();
    await (await this.locatorFor(`[role="columnheader"][data-col="${colIndex}"]`)()).click();
  }

  /** Clicks the select-all corner. */
  async clickCorner(): Promise<void> {
    await (await this.gridElement()).focus();
    await (await this.locatorFor('[data-tm-corner]')()).click();
  }

  /**
   * Sends keys through the grid's keyboard pipeline: focuses the active
   * cell (or the grid container when no cell is active) and types there.
   */
  async pressKeys(...keys: (string | TestKey)[]): Promise<void> {
    const active = await this.locatorForOptional('[role="gridcell"][tabindex="0"]')();
    const target = active ?? (await this.gridElement());
    await target.focus();
    await target.sendKeys(...keys);
  }

  /** Whether the loading overlay is up (`aria-busy` on the grid). */
  async isLoading(): Promise<boolean> {
    return (await (await this.gridElement()).getAttribute('aria-busy')) === 'true';
  }

  /**
   * Gets the empty overlay's trimmed text (built-in or projected), or
   * `null` while the overlay is not shown.
   */
  async getEmptyText(): Promise<string | null> {
    const overlay = await this.locatorForOptional('[data-tm-empty]')();
    return overlay === null ? null : (await overlay.text()).trim();
  }

  /** Whether the new-row placeholder row is rendered. */
  async hasPlaceholderRow(): Promise<boolean> {
    return (await this.locatorForOptional('.tm-grid__row--placeholder')()) !== null;
  }

  // ---- editing (editable grids only) ----

  /**
   * Opens an editor session on a cell: clicks the cell, then presses the
   * opening key. `via: 'enter'` and `'f2'` open in *edit* mode (the full
   * display text, caret at the end); `'type'` opens in *enter* mode by
   * typing `seed` — its first character is the type-to-edit keystroke that
   * replaces the content, the rest is typed into the editor. Boolean cells
   * toggle instead of opening a session; readonly cells are a no-op.
   */
  async openEditor(
    rowIndex: number,
    colIndex: number,
    via: TmGridEditorOpenVia = 'enter',
    seed?: string,
  ): Promise<void> {
    await this.clickCell(rowIndex, colIndex);
    if (via === 'enter') {
      await this.pressKeys(TestKey.ENTER);
      return;
    }
    if (via === 'f2') {
      await this.pressKeys(TestKey.F2);
      return;
    }
    if (seed === undefined || seed.length === 0) {
      throw new Error('TmGridHarness.openEditor: via "type" requires a non-empty seed.');
    }
    await this.pressKeys(seed[0]);
    if (seed.length > 1) {
      await this.typeInEditor(seed.slice(1));
    }
  }

  /** Whether an editor session is open (the editing cell renders its outlet). */
  async isEditorOpen(): Promise<boolean> {
    return (await this.locatorForOptional('[data-tm-editor]')()) !== null;
  }

  /**
   * The open editor's current text: the value of the input rendered inside
   * the editing cell (`[data-tm-editor] input` — the built-in text editor,
   * or any input-hosting registered editor). Throws when no editor is open
   * or the mounted editor renders no input (e.g. the enum select).
   */
  async getEditorText(): Promise<string> {
    const input = await this.locatorFor('[data-tm-editor] input')();
    return input.getProperty<string>('value');
  }

  /**
   * Types into the open editor's input through native key/input events,
   * appending at the end of its current content (use `'type'` seeding or
   * the editor's own selection semantics to replace). Input-hosting
   * editors only, as {@link getEditorText}.
   */
  async typeInEditor(text: string): Promise<void> {
    const input = await this.locatorFor('[data-tm-editor] input')();
    await input.sendKeys(text);
  }

  /**
   * Commits the open session: Enter commits and moves down (or back to the
   * tab run's origin column), Tab commits and moves the selection to the
   * next editable cell without opening an editor (§8.2). The key is sent
   * through the editor so it bubbles to the grid exactly like a user's
   * keystroke. Note: an OPEN dropdown panel owns Enter — commit an enum
   * editor by activating an option instead.
   */
  async commitEditor(via: TmGridEditorCommitVia = 'enter'): Promise<void> {
    await this.sendEditorKey(via === 'enter' ? TestKey.ENTER : TestKey.TAB);
  }

  /**
   * Cancels the open session via Escape — the model is never written. On
   * an enum editor with an open panel the first Escape only closes the
   * panel (the two-stage Esc, §8.4); call again to cancel the session.
   */
  async cancelEditor(): Promise<void> {
    await this.sendEditorKey(TestKey.ESCAPE);
  }

  // ---- error tally (the editable status bar, §10) ----

  /**
   * The error count shown by the status-bar tally chip, or 0 while the
   * chip is absent (no errors, or readonly mode — the bar renders in
   * editable mode only).
   */
  async getErrorCount(): Promise<number> {
    const chip = await this.locatorForOptional('[data-tm-status-chip]')();
    return chip === null ? 0 : this.leadingCount(await chip.text());
  }

  /**
   * The pending-resolution count shown in the status bar while async paste
   * resolutions are in flight, or 0 while none are.
   */
  async getPendingCount(): Promise<number> {
    const pending = await this.locatorForOptional('.tm-grid__status-pending')();
    return pending === null ? 0 : this.leadingCount(await pending.text());
  }

  /**
   * Clicks the status bar's next-error button: activates and scrolls to
   * the next errored cell in row-major order, cycling. Requires at least
   * one error (the buttons render with the chip).
   */
  async tallyNext(): Promise<void> {
    await (await this.locatorFor('[data-tm-status-next]')()).click();
  }

  /** Clicks the previous-error button — {@link tallyNext} in reverse. */
  async tallyPrevious(): Promise<void> {
    await (await this.locatorFor('[data-tm-status-prev]')()).click();
  }

  // ---- context menu (§8.5) ----

  /**
   * Opens the grid's context menu via Shift+F10 — at the given cell after
   * clicking it, or at the current active cell when no coordinates are
   * given — and returns the menu's harness. The menu panel is portaled
   * into the CDK overlay (outside the grid), so the harness is resolved
   * through the document root.
   */
  async openContextMenu(rowIndex?: number, colIndex?: number): Promise<TmMenuHarness> {
    if (rowIndex !== undefined && colIndex !== undefined) {
      await this.clickCell(rowIndex, colIndex);
    }
    const active = await this.locatorForOptional('[role="gridcell"][tabindex="0"]')();
    const target = active ?? (await this.gridElement());
    await target.focus();
    await target.sendKeys({ shift: true }, TestKey.F10);
    return this.documentRootLocatorFactory().locatorFor(TmMenuHarness)();
  }

  /** Sends one key through the open editor (the input when it has one). */
  private async sendEditorKey(key: TestKey): Promise<void> {
    const input = await this.locatorForOptional('[data-tm-editor] input')();
    const target = input ?? (await this.locatorFor('[data-tm-editor]')());
    await target.sendKeys(key);
  }

  /** The first integer in a localized tally text ("3 errors" → 3). */
  private leadingCount(text: string): number {
    const match = /\d+/.exec(text);
    return match === null ? 0 : Number(match[0]);
  }

  private async modifierClickCell(
    rowIndex: number,
    colIndex: number,
    init: PointerEventInit,
  ): Promise<void> {
    await (await this.gridElement()).focus();
    const cell = await this.getCell(rowIndex, colIndex);
    dispatchPointerPress(await cell.host(), init);
    await this.forceStabilize();
  }
}
