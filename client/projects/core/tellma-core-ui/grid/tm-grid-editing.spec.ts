// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { applyEach, disabled, form, required } from '@angular/forms/signals';

import type { TmCellEditor } from '@tellma/core-ui/contracts';
import { provideTellmaUi, TM_CELL_EDITOR_HOST } from '@tellma/core-ui';
import { TmGridHarness } from '@tellma/core-ui-testing';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';
import { TmGridEditorDef } from './tm-grid-templates';

interface Line {
  readonly id: number;
  readonly name: string | null;
  readonly qty: number | null;
  readonly active: boolean;
  readonly unit: string | null;
}

function makeLines(): Line[] {
  return [
    { id: 1, name: 'Alpha', qty: 10, active: true, unit: null },
    { id: 2, name: 'Beta', qty: 20, active: false, unit: 'kg' },
    { id: 3, name: 'Gamma', qty: 42, active: true, unit: null },
  ];
}

@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid
      gridId="edit-spec-grid"
      [field]="f"
      [rowId]="rowId"
      [newRow]="makeRow"
      [readonly]="readonly()"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="140" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="100" />
      <tm-grid-column key="active" type="boolean" header="Active" [width]="80" />
      <tm-grid-column key="unit" type="enum" header="Unit" [options]="units" [width]="100" />
    </tm-grid>
    <input id="outside-input" />
  `,
})
class EditHost {
  readonly model = signal<Line[]>(makeLines());
  readonly f = form(this.model, (lines) => {
    applyEach(lines, (line) => {
      required(line.name);
      // Row 3's qty (42) is field-disabled — the field beats the column.
      disabled(line.qty, { when: ({ value }) => value() === 42 });
    });
  });
  readonly readonly = signal(false);
  readonly units = ['kg', 'pcs', 'ltr'];
  readonly grid = viewChild.required(TmGrid);
  private nextId = 100;
  readonly rowId = (row: Line): number => row.id;
  readonly makeRow = (): Line => ({
    id: this.nextId++,
    name: null,
    qty: null,
    active: false,
    unit: null,
  });
}

/** A bare consumer control implementing TmCellEditor (DoD 14). */
@Component({
  selector: 'tm-test-cell-editor',
  template: `<input class="test-editor" (input)="onInput($event)" />`,
})
class TestCellEditor implements TmCellEditor<string | null> {
  private readonly cellHost = inject(TM_CELL_EDITOR_HOST, { optional: true });
  readonly value = signal<string | null>(null);
  readonly text = computed(() => this.value());
  constructor() {
    this.cellHost?.register(this as TmCellEditor<unknown>);
  }
  commit(): void {}
  cancel(): void {}
  focus(): void {
    document.querySelector<HTMLInputElement>('.test-editor')?.focus();
  }
  seed(text: string): void {
    this.value.set(text);
    const el = document.querySelector<HTMLInputElement>('.test-editor');
    if (el !== null) {
      el.value = text;
    }
  }
  protected onInput(event: Event): void {
    this.value.set((event.target as HTMLInputElement).value);
  }
}

@Component({
  imports: [TmGrid, TmGridColumn, TmGridEditorDef, TestCellEditor],
  template: `
    <tm-grid
      gridId="custom-editor-grid"
      [field]="f"
      [rowId]="rowId"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="140">
        <tm-test-cell-editor *tmGridEditor />
      </tm-grid-column>
    </tm-grid>
  `,
})
class CustomEditorHost {
  readonly model = signal<Line[]>(makeLines());
  readonly f = form(this.model);
  readonly rowId = (row: Line): number => row.id;
}

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
    ...init,
  });
  target.dispatchEvent(event);
  return event;
}

function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
  );
}

function editorInput(scroller: HTMLElement): HTMLInputElement | null {
  return scroller.querySelector<HTMLInputElement>('[data-tm-editor] input');
}

/** Types `text` into the open editor's input through native input events. */
function typeInto(input: HTMLInputElement, text: string): void {
  input.value = text;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

async function setup(): Promise<{
  fixture: ComponentFixture<EditHost>;
  host: EditHost;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(EditHost);
  await stable(fixture);
  const scroller = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-grid__scroller',
  ) as HTMLElement;
  return { fixture, host: fixture.componentInstance, scroller };
}

/** Activates cell (0,0) via the keyboard entry path. */
async function activateOrigin(
  fixture: ComponentFixture<unknown>,
  scroller: HTMLElement,
): Promise<void> {
  scroller.focus();
  keydown(scroller, 'ArrowDown');
  await stable(fixture);
}

describe('tm-grid (editing)', () => {
  it('type-to-edit opens a seeded editor synchronously; Enter commits through the field and moves down', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    const event = keydown(scroller, 'W');
    // The mount is synchronous (the IME contract shares this path).
    const input = editorInput(scroller);
    expect(event.defaultPrevented).toBe(true);
    expect(input).not.toBeNull();
    expect(input!.value).toBe('W');
    expect(document.activeElement).toBe(input);

    typeInto(input!, 'Widget');
    keydown(input!, 'Enter');
    await stable(fixture);
    expect(host.model()[0].name).toBe('Widget');
    expect(editorInput(scroller)).toBeNull();
    expect(document.activeElement).toBe(cellAt(scroller, 1, 0));
  });

  it('Tab commits and moves to the next editable cell without opening an editor', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'X');
    const input = editorInput(scroller) as HTMLInputElement;
    typeInto(input, 'Tabbed');
    keydown(input, 'Tab');
    await stable(fixture);
    expect(host.model()[0].name).toBe('Tabbed');
    expect(editorInput(scroller)).toBeNull();
    // Selection moved to (0,1); no editor opened on the target.
    expect(document.activeElement).toBe(cellAt(scroller, 0, 1));
  });

  it('Esc cancels the edit and never writes the model', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'z');
    const input = editorInput(scroller) as HTMLInputElement;
    typeInto(input, 'zzz');
    keydown(input, 'Escape');
    await stable(fixture);
    expect(host.model()[0].name).toBe('Alpha');
    expect(editorInput(scroller)).toBeNull();
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));
  });

  it('F2 opens in edit mode seeded with the display text, caret at the end', async () => {
    const { fixture, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'F2');
    const input = editorInput(scroller) as HTMLInputElement;
    expect(input.value).toBe('Alpha');
    expect(input.selectionStart).toBe('Alpha'.length);

    // Edit mode: horizontal arrows stay with the caret (no commit, no move).
    keydown(input, 'ArrowLeft');
    expect(editorInput(scroller)).not.toBeNull();
  });

  it('IME composition keydown opens an UNSEEDED editor without consuming the key', async () => {
    const { fixture, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    const event = keydown(scroller, 'Process', { isComposing: true });
    const input = editorInput(scroller);
    expect(event.defaultPrevented).toBe(false);
    expect(input).not.toBeNull();
    expect(input!.value).toBe(''); // unseeded — the composition supplies content
    expect(document.activeElement).toBe(input);
  });

  it('unparseable text clears the model, displays the raw text in error state, and tallies', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,1) qty
    await stable(fixture);

    keydown(scroller, 'a');
    const input = editorInput(scroller) as HTMLInputElement;
    typeInto(input, 'abc');
    keydown(input, 'Enter');
    await stable(fixture);

    expect(host.model()[0].qty).toBeNull(); // cleared value, never stale
    const cell = cellAt(scroller, 0, 1) as HTMLElement;
    expect(cell.textContent).toContain('abc'); // the raw text stays visible
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
    expect(cell.getAttribute('aria-invalid')).toBe('true');
    expect(host.grid().errorCount()).toBe(1);

    // A valid commit clears the invalid input.
    keydown(scroller, 'ArrowUp'); // back to (0,1)
    await stable(fixture);
    keydown(scroller, '5');
    keydown(editorInput(scroller) as HTMLInputElement, 'Enter');
    await stable(fixture);
    expect(host.model()[0].qty).toBe(5);
    expect(host.grid().errorCount()).toBe(0);
  });

  it('a required field error surfaces as a cell error and counts in the tally', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'Delete');
    await stable(fixture);
    expect(host.model()[0].name).toBeNull();
    const cell = cellAt(scroller, 0, 0) as HTMLElement;
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
    expect(host.grid().errorCount()).toBe(1);

    // The active errored cell describes itself via the overlay message.
    expect(cell.getAttribute('aria-describedby')).not.toBeNull();
    const message = document.getElementById(cell.getAttribute('aria-describedby')!);
    expect(message?.textContent).toContain('required');
  });

  it('field-level disabled beats column editability', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    // Move to (2,1): qty 42, disabled by the schema.
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowRight');
    await stable(fixture);

    const cell = cellAt(scroller, 2, 1) as HTMLElement;
    expect(cell.classList.contains('tm-grid__cell--readonly')).toBe(true);
    const event = keydown(scroller, '7');
    expect(editorInput(scroller)).toBeNull(); // no editor — a no-op, not a swallow
    expect(event.defaultPrevented).toBe(false);
    expect(host.model()[2].qty).toBe(42);
  });

  it('a readonly flip mid-edit cancels (model untouched); grid state survives the round trip', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'q');
    typeInto(editorInput(scroller) as HTMLInputElement, 'qqq');
    host.readonly.set(true);
    await stable(fixture);
    expect(editorInput(scroller)).toBeNull();
    expect(host.model()[0].name).toBe('Alpha'); // cancelled, never committed

    host.readonly.set(false);
    await stable(fixture);
    // The active cell (and its selection) survived the flip.
    const cell = cellAt(scroller, 0, 0) as HTMLElement;
    expect(cell.getAttribute('aria-selected')).toBe('true');
    expect(cell.getAttribute('tabindex')).toBe('0');
  });

  it('typing in the placeholder materializes exactly one row; ONE undo removes it entirely', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    // The placeholder is the last view row (marked * in its row header);
    // grid-end stops at the last DATA row, one ArrowDown steps onto it.
    keydown(scroller, 'End', { ctrlKey: true });
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'Home'); // first column
    await stable(fixture);
    expect(scroller.querySelector('[data-tm-rowhdr][data-row="3"]')?.textContent?.trim()).toBe(
      '*',
    );

    keydown(scroller, 'N');
    const input = editorInput(scroller) as HTMLInputElement;
    typeInto(input, 'New line');
    keydown(input, 'Enter');
    await stable(fixture);

    expect(host.model().length).toBe(4);
    expect(host.model()[3].name).toBe('New line');
    expect(host.model()[3].id).toBe(100); // minted by the factory
    // A fresh placeholder appeared beneath the materialized row.
    expect(scroller.querySelector('[data-tm-rowhdr][data-row="4"]')?.textContent?.trim()).toBe(
      '*',
    );

    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(host.model().length).toBe(3); // one undo removed row AND write
  });

  it('boolean cells toggle on Space and Enter, and the toggle is undoable', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight');
    keydown(scroller, 'ArrowRight'); // (0,2) active
    await stable(fixture);

    keydown(scroller, ' ');
    await stable(fixture);
    expect(host.model()[0].active).toBe(false);
    expect(editorInput(scroller)).toBeNull(); // no session for booleans

    keydown(scroller, 'Enter');
    await stable(fixture);
    expect(host.model()[0].active).toBe(true);

    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()[0].active).toBe(false);
  });

  it('Mod+Z / Mod+Y round-trip a committed edit', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'R');
    typeInto(editorInput(scroller) as HTMLInputElement, 'Renamed');
    keydown(editorInput(scroller) as HTMLInputElement, 'Enter');
    await stable(fixture);
    expect(host.model()[0].name).toBe('Renamed');

    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()[0].name).toBe('Alpha');

    keydown(scroller, 'y', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()[0].name).toBe('Renamed');
  });

  it('keyboard row insert and delete mutate the model through the field', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, '+', { ctrlKey: true, altKey: true });
    await stable(fixture);
    expect(host.model().length).toBe(4);
    expect(host.model()[0].id).toBe(100); // inserted above row 1 by the factory

    keydown(scroller, '-', { ctrlKey: true, altKey: true });
    await stable(fixture);
    expect(host.model().length).toBe(3);
    expect(host.model()[0].id).toBe(1);
  });

  it('enum cells edit through tm-select; activating an option commits and closes', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'End'); // (0,3) unit — last column
    await stable(fixture);

    keydown(scroller, 'Enter');
    await stable(fixture); // panel content lands a pass after the overlay attaches
    const select = scroller.querySelector('[data-tm-editor] tm-select');
    expect(select).not.toBeNull();
    const options = document.querySelectorAll<HTMLElement>('.tm-option__row');
    expect(options.length).toBe(3);

    options[1].click(); // 'pcs'
    await stable(fixture);
    expect(host.model()[0].unit).toBe('pcs');
    expect(scroller.querySelector('[data-tm-editor]')).toBeNull(); // closed, no move
    expect(document.activeElement).toBe(cellAt(scroller, 0, 3));
  });

  it('commit-on-blur: focusing outside the grid commits the open editor', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'B');
    typeInto(editorInput(scroller) as HTMLInputElement, 'Blurred');
    (document.getElementById('outside-input') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.model()[0].name).toBe('Blurred');
    expect(editorInput(scroller)).toBeNull();
  });

  it('Shift+F10 opens the localized context menu; Delete rows works', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'F10', { shiftKey: true });
    await stable(fixture);
    const panel = document.querySelector('.tm-menu__panel');
    expect(panel).not.toBeNull();
    const labels = [...panel!.querySelectorAll('.tm-menu__label')].map((el) =>
      el.textContent?.trim(),
    );
    expect(labels).toContain('Copy with headers');
    expect(labels).toContain('Insert 1 row above');
    expect(labels).toContain('Delete 1 row');

    const deleteItem = [...panel!.querySelectorAll<HTMLElement>('.tm-menu__item')].find((el) =>
      el.textContent!.includes('Delete 1 row'),
    ) as HTMLElement;
    deleteItem.click();
    await stable(fixture);
    expect(host.model().length).toBe(2);
    expect(host.model()[0].id).toBe(2);
    expect(document.querySelector('.tm-menu__panel')).toBeNull();
  });

  it('a consumer *tmGridEditor control registers through TM_CELL_EDITOR_HOST and commits', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(CustomEditorHost);
    await stable(fixture);
    const host = fixture.componentInstance;
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    await activateOrigin(fixture, scroller);

    keydown(scroller, 'C');
    const custom = scroller.querySelector<HTMLInputElement>('[data-tm-editor] .test-editor');
    expect(custom).not.toBeNull();
    expect(custom!.value).toBe('C'); // seeded through the registered editor

    custom!.value = 'Custom';
    custom!.dispatchEvent(new Event('input', { bubbles: true }));
    keydown(custom!, 'Enter');
    await stable(fixture);
    expect(host.model()[0].name).toBe('Custom');
    expect(scroller.querySelector('[data-tm-editor]')).toBeNull();
  });
});

describe('tm-grid (editing harness)', () => {
  it('drives the editor lifecycle: open, read, type, commit, cancel', async () => {
    const { fixture, host } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const grid = await loader.getHarness(TmGridHarness);

    // F2 opens in edit mode with the full display text.
    await grid.openEditor(0, 0, 'f2');
    expect(await grid.isEditorOpen()).toBe(true);
    expect(await grid.getEditorText()).toBe('Alpha');

    // typeInEditor appends; Enter commits and moves down.
    await grid.typeInEditor('X');
    expect(await grid.getEditorText()).toBe('AlphaX');
    await grid.commitEditor('enter');
    expect(await grid.isEditorOpen()).toBe(false);
    expect(host.model()[0].name).toBe('AlphaX');
    expect(await grid.getActiveCell()).toEqual({ row: 1, col: 0 });

    // Type-to-edit seeds the whole string; Tab commits without opening an
    // editor on the target.
    await grid.openEditor(1, 0, 'type', 'Typed');
    expect(await grid.getEditorText()).toBe('Typed');
    await grid.commitEditor('tab');
    expect(host.model()[1].name).toBe('Typed');
    expect(await grid.isEditorOpen()).toBe(false);
    expect(await grid.getActiveCell()).toEqual({ row: 1, col: 1 });

    // Escape cancels without writing.
    await grid.openEditor(0, 0, 'type', 'zzz');
    await grid.cancelEditor();
    expect(await grid.isEditorOpen()).toBe(false);
    expect(host.model()[0].name).toBe('AlphaX');
  });

  it('reads the status-bar tally and navigates errors through it', async () => {
    const { fixture, host } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const grid = await loader.getHarness(TmGridHarness);

    expect(await grid.getErrorCount()).toBe(0);
    expect(await grid.getPendingCount()).toBe(0);

    // Unparseable text on the number column becomes an invalid input.
    await grid.openEditor(0, 1, 'type', 'abc');
    await grid.commitEditor('enter');
    expect(host.model()[0].qty).toBeNull();
    expect(await grid.getErrorCount()).toBe(1);

    // The tally buttons activate the errored cell (row-major, cycling).
    await grid.clickCell(2, 0);
    await grid.tallyNext();
    expect(await grid.getActiveCell()).toEqual({ row: 0, col: 1 });
    await grid.tallyPrevious();
    expect(await grid.getActiveCell()).toEqual({ row: 0, col: 1 }); // cycles back to the only error

    // A valid commit clears the tally.
    await grid.openEditor(0, 1, 'type', '5');
    await grid.commitEditor('enter');
    expect(host.model()[0].qty).toBe(5);
    expect(await grid.getErrorCount()).toBe(0);
  });

  it('opens the context menu via Shift+F10 and returns its harness', async () => {
    const { fixture, host } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const grid = await loader.getHarness(TmGridHarness);

    const menu = await grid.openContextMenu(1, 0);
    const labels = await menu.getItemLabels();
    expect(labels).toContain('Copy with headers');
    expect(labels).toContain('Insert 1 row above');
    expect(labels).toContain('Delete 1 row');

    await menu.clickItem('Delete 1 row');
    await stable(fixture);
    expect(host.model().length).toBe(2);
    expect(host.model()[0].id).toBe(1); // row 2 (id 2) was deleted
  });
});
