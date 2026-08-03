// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, ErrorHandler, inject, signal, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { applyEach, disabled, form, readonly, required } from '@angular/forms/signals';

import {
  TM_PARSE_ERROR,
  type TmCellEditor,
  type TmLabelResolution,
} from '@tellma/core-ui/contracts';
import {
  provideTellmaUi,
  provideTmCalendar,
  TM_CELL_EDITOR_HOST,
  TM_ERROR_DISPLAY,
} from '@tellma/core-ui';
import { tmUmalquraCalendar } from '@tellma/core-ui/calendar-umalqura';
import type { TmCalendar } from '@tellma/core-ui/l10n';
import type { TmEntitySearchResult } from '@tellma/core-ui/entity-picker';
import { TmModalRef } from '@tellma/core-ui/modal';
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
      <tm-grid-column
        key="qty"
        type="number"
        header="Qty"
        [maxDecimals]="qtyMaxDecimals()"
        [width]="100"
      />
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
      // Row 3's name (Gamma) is field-readonly — the field beats the column too.
      readonly(line.name, { when: ({ value }) => value() === 'Gamma' });
    });
  });
  readonly readonly = signal(false);
  /** Display cap on qty's fraction digits (undefined ⇒ unbounded default). */
  readonly qtyMaxDecimals = signal<number | undefined>(undefined);
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
      <!-- A built-in text editor alongside, so a session of each kind can run. -->
      <tm-grid-column key="unit" header="Unit" [width]="140" />
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

function pointerPress(target: Element): void {
  const init: PointerEventInit = { bubbles: true, cancelable: true, button: 0, pointerType: 'mouse' };
  target.dispatchEvent(new PointerEvent('pointerdown', init));
  target.dispatchEvent(new PointerEvent('pointerup', init));
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

  it('a number column rounds the display but edits the full-precision value', async () => {
    const { fixture, host, scroller } = await setup();
    host.model.set([{ id: 1, name: 'Alpha', qty: 1.2345, active: true, unit: 'kg' }]);
    host.qtyMaxDecimals.set(2);
    await stable(fixture);

    // The cell shows the value rounded to two places...
    expect(cellAt(scroller, 0, 1)!.textContent!.trim()).toBe('1.23');

    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,1) qty
    await stable(fixture);
    keydown(scroller, 'F2');
    // ...but the editor opens on the UNROUNDED value, so display rounding can
    // never be committed back over the model's real number.
    const input = editorInput(scroller) as HTMLInputElement;
    expect(input.value).toBe('1.2345');

    // A PRISTINE Enter (nothing typed) commits nothing — the programmatic
    // precision survives an F2 + Enter pass untouched.
    keydown(input, 'Enter');
    await stable(fixture);
    expect(host.model()[0].qty).toBe(1.2345);
  });

  it('number cells mount tmNumber, and an EDITED commit rounds to the column scale', async () => {
    const { fixture, host, scroller } = await setup();
    host.qtyMaxDecimals.set(2);
    await stable(fixture);
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,1) qty
    await stable(fixture);
    keydown(scroller, 'F2');

    const input = editorInput(scroller) as HTMLInputElement;
    // The built-in number editor: numeric mobile keypad + text type guard.
    expect(input.getAttribute('inputmode')).toBe('decimal');
    expect(input.type).toBe('text');

    input.focus();
    input.value = '1.005678';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await stable(fixture);
    keydown(input, 'Enter');
    await stable(fixture);
    // Model = display: the commit is the display-rounded value.
    expect(host.model()[0].qty).toBe(1.01);
    expect(cellAt(scroller, 0, 1)!.textContent!.trim()).toBe('1.01');
  });

  it('an over-precision number commit becomes a precision invalid input', async () => {
    const { fixture, host, scroller } = await setup();
    await stable(fixture);
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,1) qty
    await stable(fixture);
    keydown(scroller, 'F2');

    const input = editorInput(scroller) as HTMLInputElement;
    input.focus();
    input.value = '12345678901234567';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await stable(fixture);
    keydown(input, 'Enter');
    await stable(fixture);

    expect(host.model()[0].qty).toBeNull();
    let cell = cellAt(scroller, 0, 1) as HTMLElement;
    expect(cell.textContent!.trim()).toBe('12345678901234567');
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);

    // Enter moved the active cell down; return so the error overlay
    // describes the errored cell, and assert the message names the actual
    // problem, not a generic parse failure.
    keydown(scroller, 'ArrowUp');
    await stable(fixture);
    cell = cellAt(scroller, 0, 1) as HTMLElement;
    const message = document.getElementById(cell.getAttribute('aria-describedby')!);
    expect(message?.textContent).toContain('at most 15 digits');
  });

  it('a PASTE rounds to the column scale and rejects over-precision the same way', async () => {
    // The typed-commit path is covered above; paste is a second, separate
    // call site of the same normalizeValue closure, and only engine-level
    // stubs covered it — a wiring regression there would let a spreadsheet
    // paste write full precision straight past the invariant.
    const { fixture, host, scroller } = await setup();
    host.qtyMaxDecimals.set(2);
    await stable(fixture);
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,1) qty
    await stable(fixture);

    const rounding = new DataTransfer();
    rounding.setData('text/plain', '1.005678\r\n');
    scroller.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: rounding, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(host.model()[0].qty).toBe(1.01);
    expect(cellAt(scroller, 0, 1)!.textContent!.trim()).toBe('1.01');

    const overflow = new DataTransfer();
    overflow.setData('text/plain', '12345678901234567\r\n');
    scroller.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: overflow, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(host.model()[0].qty).toBeNull();
    const cell = cellAt(scroller, 0, 1) as HTMLElement;
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
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

  it('field-level readonly beats column editability', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    // Move to (2,0): name Gamma, field-readonly by the schema.
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowDown');
    await stable(fixture);

    const cell = cellAt(scroller, 2, 0) as HTMLElement;
    expect(cell.classList.contains('tm-grid__cell--readonly')).toBe(true);
    const event = keydown(scroller, 'Z');
    expect(editorInput(scroller)).toBeNull(); // no editor — a no-op, not a swallow
    expect(event.defaultPrevented).toBe(false);
    expect(host.model()[2].name).toBe('Gamma'); // never entered edit, never written
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

  it('Enter after typing in the placeholder advances onto the NEW placeholder and stays', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'End', { ctrlKey: true });
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'Home'); // the placeholder's first cell (3,0)
    await stable(fixture);

    keydown(scroller, 'N');
    typeInto(editorInput(scroller) as HTMLInputElement, 'Line A');
    keydown(editorInput(scroller) as HTMLInputElement, 'Enter');
    await stable(fixture);

    expect(host.model()[3].name).toBe('Line A');
    // Enter moved one row down onto the FRESH placeholder — and the commit's
    // own rows-changed reconcile must not yank the active cell back up onto
    // the materialized row (the placeholder is absent from the order snapshot,
    // so the remap once mistook it for a vanished row).
    const target = cellAt(scroller, 4, 0) as HTMLElement;
    expect(target.getAttribute('tabindex')).toBe('0');
    expect(document.activeElement).toBe(target);
  });

  it('a model write immediately followed by stepping onto the placeholder keeps it active', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowRight');
    keydown(scroller, 'ArrowRight'); // (2,2) — the last data row's boolean
    await stable(fixture);

    keydown(scroller, ' '); // toggle = a model write (its reconcile is pending)
    keydown(scroller, 'ArrowDown'); // onto the placeholder before it lands
    await stable(fixture);
    expect(host.model()[2].active).toBe(false);
    const target = cellAt(scroller, 3, 2) as HTMLElement;
    expect(target.getAttribute('tabindex')).toBe('0');
    expect(document.activeElement).toBe(target);
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

  it('a boolean cell toggles on the glyph press, but the empty cell space starts a range drag', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);
    const cell = cellAt(scroller, 0, 2) as HTMLElement; // active (boolean) column
    const glyph = cell.querySelector('.tm-grid-bool') as HTMLElement;
    expect(host.model()[0].active).toBe(true);

    // A press on the empty cell space (target = the cell, not the glyph) is a
    // range-drag gesture, not a toggle — the value must not change.
    pointerPress(cell);
    await stable(fixture);
    expect(host.model()[0].active).toBe(true);

    // A press on the glyph itself toggles.
    pointerPress(glyph);
    await stable(fixture);
    expect(host.model()[0].active).toBe(false);
  });

  it('a freshly materialized new row keeps untouched required cells quiet until edited', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller); // (0,0)
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowDown'); // (3,0) = the new-row placeholder
    keydown(scroller, 'ArrowRight');
    keydown(scroller, 'ArrowRight'); // (3,2) = active (boolean)
    await stable(fixture);
    keydown(scroller, ' '); // toggling materializes the row
    await stable(fixture);
    expect(host.model().length).toBe(4);

    // Name is required but never touched on the new row — it must stay quiet
    // (before the fix it lit up the instant the row materialized). A field
    // that IS edited still surfaces its error — see the required-field test.
    expect(cellAt(scroller, 3, 0)!.classList.contains('tm-grid__cell--error')).toBe(false);
    expect(cellAt(scroller, 3, 0)!.getAttribute('aria-invalid')).toBeNull();
    expect(host.grid().errorCount()).toBe(0);
  });

  it('defers to the injected TmErrorDisplayPolicy (a suppress-all policy hides even dirty errors)', async () => {
    // The default policy surfaces a cleared required field (see the
    // required-field test). A custom suppress-all policy must hide the very
    // same error — proving the grid asks the injected policy rather than a
    // hardcoded touched/dirty check.
    TestBed.configureTestingModule({
      providers: [
        provideTellmaUi(),
        { provide: TM_ERROR_DISPLAY, useValue: (): boolean => false },
      ],
    });
    const fixture = TestBed.createComponent(EditHost);
    await stable(fixture);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;

    await activateOrigin(fixture, scroller); // (0,0) = Name
    keydown(scroller, 'Delete'); // clears 'Alpha' → null: invalid AND dirty
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBeNull();
    expect(fixture.componentInstance.grid().errorCount()).toBe(0); // hidden by the policy
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--error')).toBe(false);
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

  it('in-editor Ctrl+Z stays native — the grid never steals it for history undo', async () => {
    const { fixture, host, scroller } = await setup();
    await activateOrigin(fixture, scroller);

    // Commit edit A on (0,0); focus moves down to the next editable cell.
    keydown(scroller, 'R');
    typeInto(editorInput(scroller) as HTMLInputElement, 'Renamed');
    keydown(editorInput(scroller) as HTMLInputElement, 'Enter');
    await stable(fixture);
    expect(host.model()[0].name).toBe('Renamed');

    // Open a fresh editor on the next row and type into it.
    keydown(scroller, 'S');
    const input = editorInput(scroller) as HTMLInputElement;
    typeInto(input, 'Second');

    // Ctrl+Z here is the input's own text-undo. The editing keymap returns
    // null for modified keys, so the grid must neither consume the event nor
    // run a history undo (which would revert the committed edit A).
    const event = keydown(input, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(event.defaultPrevented).toBe(false); // grid stayed out of the way
    expect(host.model()[0].name).toBe('Renamed'); // committed edit A untouched
    expect(editorInput(scroller)).not.toBeNull(); // session still open
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
    // Focus stays in the grid after the delete takes the active cell's DOM node
    // with it — otherwise focus falls to the page and a later Mod+Z (undo)
    // would land in whatever control got focus instead of the grid.
    expect(scroller.contains(document.activeElement)).toBe(true);
    expect((document.activeElement as HTMLElement).matches('[data-tm-cell]')).toBe(true);
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

  it('a template editor commits a value equal to the PREVIOUS session’s open text', async () => {
    // The pristine-baseline check exists so F2 + Enter never rewrites a
    // display-rounded cell. It must not survive its own session: a stale
    // baseline would make the next editor's genuine edit look pristine and
    // silently drop the write.
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(CustomEditorHost);
    await stable(fixture);
    const host = fixture.componentInstance;
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    await activateOrigin(fixture, scroller);

    // Session 1: F2 the EMPTY unit cell (baseline ''), then abandon it.
    keydown(scroller, 'ArrowRight'); // (0,1) unit — null, so the open text is ''
    await stable(fixture);
    keydown(scroller, 'F2');
    await stable(fixture);
    const builtIn = scroller.querySelector<HTMLInputElement>('[data-tm-editor] input');
    expect(builtIn?.value).toBe('');
    keydown(builtIn!, 'Escape');
    await stable(fixture);

    // Session 2: clear the populated name cell through the template editor.
    keydown(scroller, 'ArrowLeft'); // back to (0,0) name — 'Alpha'
    await stable(fixture);
    keydown(scroller, 'F2');
    await stable(fixture);
    const custom = scroller.querySelector<HTMLInputElement>('[data-tm-editor] .test-editor');
    expect(custom).not.toBeNull();
    custom!.value = '';
    custom!.dispatchEvent(new Event('input', { bubbles: true }));
    keydown(custom!, 'Enter');
    await stable(fixture);

    expect(host.model()[0].name).toBe(''); // the clear reached the model
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

describe('tm-grid date columns (built-in defaults)', () => {
  interface DateLine {
    readonly id: number;
    readonly due: string | null;
  }

  @Component({
    imports: [TmGrid, TmGridColumn],
    template: `
      <tm-grid gridId="date-spec-grid" [field]="f" [rowId]="rowId" style="block-size: 300px">
        <tm-grid-column key="due" type="date" header="Due" [width]="140" />
      </tm-grid>
    `,
  })
  class DateHost {
    readonly model = signal<DateLine[]>([
      { id: 1, due: '2026-03-05' },
      { id: 2, due: null },
    ]);
    readonly f = form(this.model, () => {});
    readonly rowId = (row: DateLine): number => row.id;
  }

  async function setupDates(calendar?: TmCalendar) {
    TestBed.configureTestingModule({
      providers: [
        provideTellmaUi(),
        ...(calendar === undefined ? [] : [provideTmCalendar(calendar)]),
      ],
    });
    const fixture = TestBed.createComponent(DateHost);
    await stable(fixture);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    return { fixture, host: fixture.componentInstance, scroller };
  }

  it('displays via the built-in format (locale numeric, no [format] required)', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { scroller } = await setupDates();
    expect(cellAt(scroller, 0, 0)!.textContent!.trim()).toBe('3/5/2026');
    expect(cellAt(scroller, 1, 0)!.textContent!.trim()).toBe('');
    expect(warn).not.toHaveBeenCalledWith(expect.stringContaining("type 'date' has no [format]"));
    warn.mockRestore();
  });

  it('a paste copied under an unavailable source calendar never misparses as Gregorian', async () => {
    const { fixture, host, scroller } = await setupDates();
    await activateOrigin(fixture, scroller);

    // Hijri 23/9/1445 from an ar-SA Umm al-Qura grid: year 1445 is a
    // PLAUSIBLE Gregorian year — without the calendar stamp this would
    // silently write 1445-09-23. The pack is not installed here, so the
    // honest outcome is an invalid input.
    const hijri = new DataTransfer();
    hijri.setData('text/plain', '23/9/1445\r\n');
    hijri.setData(
      'text/html',
      `<table data-tm-grid='{"v":1,"locale":"ar-SA","calendar":"islamic-umalqura",` +
        `"cols":[{"key":"due","type":"date"}]}'>` +
        `<tbody><tr><td>23/9/1445</td></tr></tbody></table>`,
    );
    scroller.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: hijri, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    // The honest outcome: an invalid input (model cleared, text kept in
    // error) — NEVER the plausible-Gregorian 1445-09-23 write.
    expect(host.model()[0].due).toBeNull();
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--error')).toBe(true);

    // The same shape stamped 'gregory' parses through the source-locale rung.
    const gregorian = new DataTransfer();
    gregorian.setData('text/plain', '25/12/2027\r\n');
    gregorian.setData(
      'text/html',
      `<table data-tm-grid='{"v":1,"locale":"en-GB","calendar":"gregory",` +
        `"cols":[{"key":"due","type":"date"}]}'>` +
        `<tbody><tr><td>25/12/2027</td></tr></tbody></table>`,
    );
    scroller.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: gregorian, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(host.model()[0].due).toBe('2027-12-25'); // en-GB day-first order honored
  });

  it('a foreign paste reads as GREGORIAN even when the grid displays Umm al-Qura', async () => {
    // Excel and friends carry no metadata and serialize Gregorian dates.
    // Read in the ambient Hijri calendar instead, `9/23/2024` would be
    // Hijri year 2024 — centuries away — and be written silently. The
    // display stays Hijri; only the READING is Gregorian.
    const { fixture, host, scroller } = await setupDates(tmUmalquraCalendar());
    await activateOrigin(fixture, scroller);

    const foreign = new DataTransfer();
    foreign.setData('text/plain', '9/23/2024\r\n'); // no text/html, no meta
    scroller.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: foreign, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(host.model()[0].due).toBe('2024-09-23');
  });

  it('a foreign paste the Gregorian rung cannot read falls back to the display calendar', async () => {
    // Hijri-only text (month 13 does not exist in Gregorian) still lands:
    // the foreign rung yields only to a reading that actually works.
    const { fixture, host, scroller } = await setupDates(tmUmalquraCalendar());
    await activateOrigin(fixture, scroller);

    const hijriOnly = new DataTransfer();
    hijriOnly.setData('text/plain', '13/16/1447\r\n');
    scroller.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: hijriOnly, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(host.model()[0].due).not.toBe('2024-09-23');
  });

  it('mounts tm-date-picker as the editor; typed text commits through the built-in parse', async () => {
    const { fixture, host, scroller } = await setupDates();
    await activateOrigin(fixture, scroller);

    // Type-to-edit seeds the picker's input.
    keydown(scroller, '3');
    await stable(fixture);
    const input = scroller.querySelector<HTMLInputElement>('.tm-date-picker__input');
    expect(input).not.toBeNull();
    expect(input!.value).toBe('3');

    typeInto(input!, '3/12/2026');
    await stable(fixture);
    keydown(input!, 'Enter');
    await stable(fixture);
    expect(host.model()[0].due).toBe('2026-03-12'); // ISO in the model
    expect(cellAt(scroller, 0, 0)!.textContent!.trim()).toBe('3/12/2026');
  });

  it('unreadable text becomes a parse invalid input with the model cleared', async () => {
    const { fixture, host, scroller } = await setupDates();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'x');
    await stable(fixture);
    const input = scroller.querySelector<HTMLInputElement>('.tm-date-picker__input')!;
    typeInto(input, 'not a date');
    await stable(fixture);
    keydown(input, 'Enter');
    await stable(fixture);
    expect(host.model()[0].due).toBeNull();
    const cell = cellAt(scroller, 0, 0) as HTMLElement;
    expect(cell.textContent!.trim()).toBe('not a date');
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
  });

  it('Alt+ArrowDown opens the cell-anchored popup; the two-stage Esc composes', async () => {
    const { fixture, host, scroller } = await setupDates();
    await activateOrigin(fixture, scroller);

    // Open the editor (F2 edit mode: display text seeded), then the popup.
    keydown(scroller, 'F2');
    await stable(fixture);
    const input = scroller.querySelector<HTMLInputElement>('.tm-date-picker__input')!;
    keydown(input, 'ArrowDown', { altKey: true });
    await stable(fixture);
    const popup = document.querySelector('.tm-date-popup') as HTMLElement;
    expect(popup).not.toBeNull();
    expect(popup.querySelector('[data-tm-day="5"][aria-selected="true"]')).not.toBeNull();

    // Esc №1: the popup consumes it — the session stays open.
    keydown(popup, 'Escape');
    await stable(fixture);
    expect(document.querySelector('.tm-date-popup')).toBeNull();
    expect(scroller.querySelector('.tm-date-picker__input')).not.toBeNull();

    // Esc №2 reaches the grid and cancels the session without writing.
    keydown(scroller.querySelector('.tm-date-picker__input')!, 'Escape');
    await stable(fixture);
    expect(scroller.querySelector('.tm-date-picker__input')).toBeNull();
    expect(host.model()[0].due).toBe('2026-03-05');
  });

  it('a popup day selection commits and closes — no Enter needed', async () => {
    const { fixture, host, scroller } = await setupDates();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'F2');
    await stable(fixture);
    const input = scroller.querySelector<HTMLInputElement>('.tm-date-picker__input')!;
    keydown(input, 'ArrowDown', { altKey: true });
    await stable(fixture);

    // Pointing at a day IS the edit (the enum option's contract): a mouse
    // user has no reason to press Enter afterwards, so the pick must not
    // sit uncommitted waiting for one.
    (document.querySelector('.tm-date-popup [data-tm-day="20"]') as HTMLButtonElement).click();
    await stable(fixture);
    expect(scroller.querySelector('.tm-date-picker__input')).toBeNull();
    expect(host.model()[0].due).toBe('2026-03-20');
  });
});

describe('tm-grid entity columns (built-in tm-entity-picker editor)', () => {
  interface Agent {
    readonly id: number;
    readonly name: string;
  }
  const AGENTS: readonly Agent[] = [
    { id: 1, name: 'Adam Brown' },
    { id: 2, name: 'Adam Brown' },
    { id: 3, name: 'Alice Green' },
    { id: 4, name: 'Alan Grey' },
    { id: 5, name: 'Bob Stone' },
  ];
  const searchAgents = (query: string): readonly Agent[] => {
    const q = query.trim().toLowerCase();
    return q === '' ? AGENTS : AGENTS.filter((a) => a.name.toLowerCase().includes(q));
  };

  interface AgentLine {
    readonly id: number;
    readonly name: string | null;
    readonly agentId: number | null;
    readonly otherId: number | null;
  }

  interface ResolveDeferred {
    resolve(results: ReadonlyMap<string, TmLabelResolution<number>>): void;
    reject(reason: unknown): void;
  }

  /** A minimal modal page for the mid-edit modal round trip. */
  @Component({
    template: `
      <button data-testid="page-pick" (click)="pick()">pick</button>
      <button data-testid="page-close" (click)="ref.close()">close</button>
    `,
  })
  class AgentPage {
    readonly ref = inject(TmModalRef) as TmModalRef<{ id: number; label: string } | null>;
    pick(): void {
      this.ref.close({ id: 3, label: 'Alice Green' });
    }
  }

  @Component({
    imports: [TmGrid, TmGridColumn],
    template: `
      <tm-grid
        gridId="entity-spec-grid"
        [field]="f"
        [rowId]="rowId"
        [newRow]="makeRow"
        style="block-size: 300px"
      >
        <tm-grid-column key="name" header="Name" [width]="120" />
        <tm-grid-column
          key="agentId"
          type="entity"
          header="Agent"
          [search]="recordedSearch"
          [itemId]="agentId"
          [itemLabel]="agentLabel"
          [format]="agentFormat"
          [parse]="agentParse"
          [resolvePastedLabels]="resolveAgents"
          [advancedSearch]="advancedPage"
          [width]="160"
        />
        <tm-grid-column
          key="otherId"
          type="entity"
          header="Other"
          [search]="recordedSearch"
          [itemId]="agentId"
          [itemLabel]="agentLabel"
          [format]="agentFormat"
          [width]="160"
        />
      </tm-grid>
      <input id="outside-entity" />
    `,
  })
  class EntityHost {
    readonly model = signal<AgentLine[]>([
      { id: 1, name: 'Alpha', agentId: 5, otherId: null },
      { id: 2, name: 'Beta', agentId: null, otherId: null },
    ]);
    readonly f = form(this.model);
    private nextId = 100;
    readonly rowId = (row: AgentLine): number => row.id;
    readonly makeRow = (): AgentLine => ({
      id: this.nextId++,
      name: null,
      agentId: null,
      otherId: null,
    });

    readonly advancedPage = AgentPage;
    readonly agentId = (item: Agent): number => item.id;
    readonly agentLabel = (item: Agent): string => item.name;
    readonly agentFormat = (value: number | null): string =>
      value === null ? '' : (AGENTS.find((a) => a.id === value)?.name ?? `#${String(value)}`);
    /** Parses only the manual `#N` form; anything else falls to the resolver. */
    readonly agentParse = (text: string): number | typeof TM_PARSE_ERROR => {
      const match = /^#(\d+)$/.exec(text.trim());
      return match === null ? TM_PARSE_ERROR : Number(match[1]);
    };

    /** Every search call's query, recorded. */
    readonly searchCalls: string[] = [];
    /** Swaps the synchronous directory search for a spec-controlled one. */
    readonly searchOverride = signal<
      | ((
          query: string,
          signal: AbortSignal,
        ) => TmEntitySearchResult<Agent> | Promise<TmEntitySearchResult<Agent>>)
      | null
    >(null);
    readonly recordedSearch = (
      query: string,
      signal: AbortSignal,
    ): TmEntitySearchResult<Agent> | Promise<TmEntitySearchResult<Agent>> => {
      const override = this.searchOverride();
      if (override !== null) {
        return override(query, signal);
      }
      this.searchCalls.push(query);
      return searchAgents(query);
    };

    /** Every resolver call, recorded; each returns a spec-controlled promise. */
    readonly resolveCalls: Array<{ labels: string[]; deferred: ResolveDeferred }> = [];
    readonly resolveAgents = (
      labels: string[],
    ): Promise<ReadonlyMap<string, TmLabelResolution<number>>> =>
      new Promise((resolve, reject) => {
        this.resolveCalls.push({ labels, deferred: { resolve, reject } });
      });
  }

  async function setupEntity(): Promise<{
    fixture: ComponentFixture<EntityHost>;
    host: EntityHost;
    scroller: HTMLElement;
  }> {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(EntityHost);
    await stable(fixture);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    return { fixture, host: fixture.componentInstance, scroller };
  }

  /** The mounted picker's input, if an editor session is open. */
  function pickerInput(scroller: HTMLElement): HTMLInputElement | null {
    return scroller.querySelector<HTMLInputElement>('.tm-entity-picker__input');
  }

  /** Navigates to the agent cell (0,1) from the origin. */
  async function activateAgentCell(
    fixture: ComponentFixture<unknown>,
    scroller: HTMLElement,
  ): Promise<void> {
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight');
    await stable(fixture);
  }

  it('opening and closing the picker on the NEW-ROW placeholder materializes nothing', async () => {
    // Any commit at all on a placeholder session materializes the row, so a
    // clean entity cell — whose value channel is authoritative and would
    // otherwise take the commitValue branch — must be recognized as
    // untouched and cancel instead. Otherwise F2 + Enter on the `*` row
    // appends a blank row the user never typed into, with an undo entry.
    const { fixture, host, scroller } = await setupEntity();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'ArrowDown'); // (2,0) = the placeholder row
    keydown(scroller, 'ArrowRight'); // its agent cell
    await stable(fixture);
    expect(host.model()).toHaveLength(2);

    keydown(scroller, 'F2');
    await stable(fixture);
    const input = pickerInput(scroller);
    expect(input).not.toBeNull();
    expect(input!.value).toBe('');
    keydown(input!, 'Enter');
    await stable(fixture);
    await stable(fixture);
    expect(pickerInput(scroller)).toBeNull();
    expect(host.model()).toHaveLength(2); // no phantom row
    // …and nothing was pushed onto the history either.
    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()).toHaveLength(2);
  });

  it('F2 opens the picker quietly on the display text; a pristine Enter commits nothing', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'F2');
    await stable(fixture);
    const input = pickerInput(scroller);
    expect(input).not.toBeNull();
    expect(input!.value).toBe('Bob Stone'); // the column format's display text
    expect(document.querySelector('.tm-entity-picker__panel')).toBeNull(); // no dropdown
    expect(host.searchCalls).toHaveLength(0); // …and no search
    // Pristine Enter: the editor closes, nothing is written, no resolution.
    keydown(input!, 'Enter');
    await stable(fixture);
    await stable(fixture);
    expect(pickerInput(scroller)).toBeNull();
    expect(host.model()[0].agentId).toBe(5);
    expect(host.resolveCalls).toHaveLength(0);
    expect(host.searchCalls).toHaveLength(0);
  });

  it('type-to-edit seeds the picker and searches the seed immediately', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    const input = pickerInput(scroller);
    expect(input!.value).toBe('A');
    expect(host.searchCalls).toEqual(['A']);
    expect(document.querySelector('.tm-entity-picker__panel')).not.toBeNull();
  });

  it('Enter on the auto-highlighted result commits the pick and closes with NO move', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    const input = pickerInput(scroller)!;
    typeInto(input, 'Alice');
    await stable(fixture);
    await stable(fixture);
    keydown(input, 'Enter');
    await stable(fixture);
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(3);
    expect(pickerInput(scroller)).toBeNull(); // closed…
    expect(document.activeElement).toBe(cellAt(scroller, 0, 1)); // …no move
    expect(host.resolveCalls).toHaveLength(0); // the pick IS the edit
  });

  it('Tab with a highlighted result commits AND moves to the next cell', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    const input = pickerInput(scroller)!;
    typeInto(input, 'Alice');
    await stable(fixture);
    await stable(fixture);
    keydown(input, 'Tab');
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(3);
    expect(pickerInput(scroller)).toBeNull();
    expect(document.activeElement).toBe(cellAt(scroller, 0, 2)); // commit-and-move
  });

  it('Alt+ArrowDown from navigation opens editor AND browse dropdown in one press', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'ArrowDown', { altKey: true });
    await stable(fixture);
    await stable(fixture);
    expect(pickerInput(scroller)).not.toBeNull();
    expect(document.querySelector('.tm-entity-picker__panel')).not.toBeNull();
    expect(host.searchCalls).toEqual(['']); // the pristine browse query
    // The committed row mirrors as selected in the browse list.
    const selected = document.querySelector('[aria-selected="true"]');
    expect(selected?.textContent).toContain('Bob Stone');
  });

  it('Alt+ArrowDown mid-session opens the dropdown; the two-stage Esc composes', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'F2');
    await stable(fixture);
    const input = pickerInput(scroller)!;
    keydown(input, 'ArrowDown', { altKey: true });
    await stable(fixture);
    await stable(fixture);
    expect(document.querySelector('.tm-entity-picker__panel')).not.toBeNull();

    // Esc №1: the picker consumes it — the dropdown closes, the session stays.
    keydown(input, 'Escape');
    await stable(fixture);
    expect(document.querySelector('.tm-entity-picker__panel')).toBeNull();
    expect(pickerInput(scroller)).not.toBeNull();

    // Esc №2 reaches the grid and cancels the session without writing.
    keydown(pickerInput(scroller)!, 'Escape');
    await stable(fixture);
    expect(pickerInput(scroller)).toBeNull();
    expect(host.model()[0].agentId).toBe(5);
  });

  it('a commit with a fresh unique on-screen result auto-picks with NO resolver call', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Alice Gr');
    await stable(fixture);
    // Blur-commit: the unique on-screen result is read synchronously.
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(3);
    expect(pickerInput(scroller)).toBeNull();
    expect(host.resolveCalls).toHaveLength(0);
    expect(cellAt(scroller, 0, 1)!.textContent!.trim()).toBe('Alice Green');
  });

  it('a commit whose search is still IN FLIGHT is decided by that search, not the resolver', async () => {
    // The editor's own search is the same question the user was asking; it
    // is already paid for, and its answer outlives the editor. Handing a
    // partial query to a label resolver instead spends a second round trip
    // to get a worse answer.
    const { fixture, host, scroller } = await setupEntity();
    const pendingSearches: Array<(items: readonly Agent[]) => void> = [];
    host.searchOverride.set((query) => {
      host.searchCalls.push(query);
      return new Promise((resolve) => pendingSearches.push(resolve));
    });
    await stable(fixture);
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Alice'); // unique, but nothing has come back yet
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(pickerInput(scroller)).toBeNull(); // the editor is gone…
    expect(scroller.querySelector('.tm-grid__cell-spin')).not.toBeNull();
    expect(cellAt(scroller, 0, 1)!.textContent!.trim()).toBe('Alice');

    // …and the search it left behind was NOT aborted with it.
    pendingSearches[pendingSearches.length - 1](searchAgents('Alice'));
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(3);
    expect(host.resolveCalls).toHaveLength(0);
    expect(scroller.querySelector('.tm-grid__cell-spin')).toBeNull();
    expect(cellAt(scroller, 0, 1)!.classList.contains('tm-grid__cell--error')).toBe(false);
  });

  it('undo while an ADOPTED search is still running aborts it', async () => {
    // Adoption detaches the request from the editor, so the picker's own
    // abort sites no longer reach it. The grid took ownership; undo while
    // pending is where it has to prove it.
    const { fixture, host, scroller } = await setupEntity();
    const signals: AbortSignal[] = [];
    host.searchOverride.set(
      (query, signal) => (
        host.searchCalls.push(query),
        signals.push(signal),
        new Promise<readonly Agent[]>(() => undefined)
      ),
    );
    await stable(fixture);
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Alice');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    const adopted = signals[signals.length - 1];
    expect(adopted.aborted).toBe(false); // it survived the editor's teardown…
    expect(scroller.querySelector('.tm-grid__cell-spin')).not.toBeNull();

    scroller.focus();
    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(adopted.aborted).toBe(true); // …and the undo it belongs to killed it
    expect(host.model()[0].agentId).toBe(5);
    expect(scroller.querySelector('.tm-grid__cell-spin')).toBeNull();
  });

  it('an ABORTED adopted search does not then start the round trip it cancelled', async () => {
    // An abort reaches the continuation as a search FAILURE — the picker
    // cannot tell the two apart — and a failure normally falls through to
    // the resolver. Undo must not therefore invoke the consumer's resolver
    // after cancelling, against a request that no longer exists.
    const { fixture, host, scroller } = await setupEntity();
    host.searchOverride.set(
      (query, signal) =>
        new Promise<readonly Agent[]>((_, reject) => {
          host.searchCalls.push(query);
          signal.addEventListener('abort', () => reject(new Error('aborted')));
        }),
    );
    await stable(fixture);
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Alice');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(scroller.querySelector('.tm-grid__cell-spin')).not.toBeNull();

    scroller.focus();
    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    await stable(fixture);
    expect(host.resolveCalls).toHaveLength(0); // nothing was started by the cancellation
    expect(host.model()[0].agentId).toBe(5);
    expect(scroller.querySelector('.tm-grid__cell-spin')).toBeNull();
  });

  it('a multi-hit the resolver CAN name still reaches it — ambiguity is only the fallback', async () => {
    // A consumer type-ahead that matches codes as well as names returns
    // several rows for a code, none of them labelled with it. That code is
    // exactly what the identity resolver knows, so a search dead end must
    // not be the last word.
    const { fixture, host, scroller } = await setupEntity();
    host.searchOverride.set((query) => {
      host.searchCalls.push(query);
      return query === 'AG-1' ? [AGENTS[0], AGENTS[2]] : searchAgents(query);
    });
    await stable(fixture);
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'AG-1');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.resolveCalls).toHaveLength(1);
    host.resolveCalls[0].deferred.resolve(new Map([['AG-1', { value: 4 }]]));
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(4);
    expect(cellAt(scroller, 0, 1)!.classList.contains('tm-grid__cell--error')).toBe(false);
  });

  it("…and the search's ambiguous verdict stands when the resolver comes back empty", async () => {
    // The fallback is what keeps the better message: "matches more than
    // one" is true and useful where the resolver's "no match" is neither.
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Al'); // two names, neither labelled 'Al'
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.resolveCalls).toHaveLength(1);
    host.resolveCalls[0].deferred.resolve(new Map([['Al', { error: 'notFound' }]]));
    await stable(fixture);
    expect(host.model()[0].agentId).toBeNull();
    const message = fixture.nativeElement.textContent as string;
    expect(message).toContain('more than one');
  });

  it('a TRUNCATED page never commits: the duplicate may be past the cap', async () => {
    // 'Adam Brown' names two agents. A capped page showing one of them and
    // an unrelated row looks like a unique exact match and is not one.
    const { fixture, host, scroller } = await setupEntity();
    host.searchOverride.set((query) => {
      host.searchCalls.push(query);
      return query === 'Adam Brown'
        ? { items: [AGENTS[0], AGENTS[3]], hasMore: true }
        : searchAgents(query);
    });
    await stable(fixture);
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Adam Brown');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.model()[0].agentId).not.toBe(1); // NOT committed off a partial page
    expect(host.resolveCalls).toHaveLength(1); // handed to the identity authority
  });

  it('a unique EXACT-label match wins over the ranking when several rows match', async () => {
    // 'Adam Brown' matches two rows and both ARE that label: still
    // ambiguous. 'Bob Stone' matches one. The discriminator is identity,
    // not the size of the result set.
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Adam Brown');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    await stable(fixture);
    expect(host.resolveCalls).toHaveLength(0);
    expect(host.model()[0].agentId).toBeNull();
    expect(cellAt(scroller, 0, 1)!.classList.contains('tm-grid__cell--error')).toBe(true);
  });

  it('an entity column with NO resolver still resolves typed text through its search', async () => {
    // The otherId column has search but no resolver. Before the search
    // rung, ANY typed text on it that the SYNCHRONOUS fast path could not
    // catch was a definitive invalid input — including text its own search
    // resolves uniquely a moment later.
    const { fixture, host, scroller } = await setupEntity();
    const pendingSearches: Array<(items: readonly Agent[]) => void> = [];
    host.searchOverride.set((query) => {
      host.searchCalls.push(query);
      return new Promise((resolve) => pendingSearches.push(resolve));
    });
    await stable(fixture);
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,2) otherId
    await stable(fixture);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'lice Gre');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(scroller.querySelector('.tm-grid__cell-spin')).not.toBeNull();
    pendingSearches[pendingSearches.length - 1](searchAgents('lice Gre'));
    await stable(fixture);
    expect(host.model()[0].otherId).toBe(3);
    expect(cellAt(scroller, 0, 2)!.classList.contains('tm-grid__cell--error')).toBe(false);
  });

  it('unresolved commit text flows through the resolver — exactly one call, after teardown', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    // A CODE: the name search finds nothing for it, so the editor's own
    // search cannot decide the commit and the resolver — which may index
    // codes, aliases, and inactive records the type-ahead never offers — is
    // the authority.
    typeInto(pickerInput(scroller)!, 'AG-0001');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    // The editor tore down BEFORE the resolver ran; the cell shows pending.
    expect(pickerInput(scroller)).toBeNull();
    expect(host.resolveCalls).toHaveLength(1);
    expect(host.resolveCalls[0].labels).toEqual(['AG-0001']);
    expect(scroller.querySelector('.tm-grid__cell-spin')).not.toBeNull();
    expect(host.model()[0].agentId).toBeNull(); // cleared while pending

    host.resolveCalls[0].deferred.resolve(new Map([['AG-0001', { value: 1 }]]));
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(1);
    expect(scroller.querySelector('.tm-grid__cell-spin')).toBeNull();

    // ONE undo restores the pre-edit value.
    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(5);
  });

  it('notFound keeps the raw text as an invalid input with the localized message', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'Z');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Zebra');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    host.resolveCalls[0].deferred.resolve(new Map([['Zebra', { error: 'notFound' }]]));
    await stable(fixture);
    expect(host.model()[0].agentId).toBeNull();
    const cell = cellAt(scroller, 0, 1)!;
    expect(cell.textContent!.trim()).toBe('Zebra'); // raw text kept in place
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
  });

  it('an errored entity cell can be CLEARED from its own editor', async () => {
    // The invalid input is the only thing left to clear: the cell's value is
    // already null, so emptying the editor moves no value at all. A pristine
    // check that only compares values would read that as "nothing happened"
    // and cancel — leaving the user with a red cell full of text they cannot
    // get rid of without leaving the editor.
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'Z');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Zebra');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    host.resolveCalls[0].deferred.resolve(new Map([['Zebra', { error: 'notFound' }]]));
    await stable(fixture);
    expect(cellAt(scroller, 0, 1)!.classList.contains('tm-grid__cell--error')).toBe(true);

    // F2 on the errored cell (still the active one) opens on its RAW text;
    // emptying it commits.
    scroller.focus();
    keydown(scroller, 'F2');
    await stable(fixture);
    expect(pickerInput(scroller)!.value).toBe('Zebra');
    typeInto(pickerInput(scroller)!, '');
    await stable(fixture);
    keydown(pickerInput(scroller)!, 'Enter');
    await stable(fixture);
    await stable(fixture);
    const cell = cellAt(scroller, 0, 1)!;
    expect(cell.textContent!.trim()).toBe('');
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(false);
    expect(host.model()[0].agentId).toBeNull();
    expect(host.resolveCalls).toHaveLength(1); // the empty string never resolves
  });

  it('the consumer parse rung commits #N synchronously with no resolver call', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, '#');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, '#4');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(4);
    expect(host.resolveCalls).toHaveLength(0);
  });

  it('a column with no resolver records the invalid input directly — never a raw-text write', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,2) otherId — search, no parse, no resolver
    await stable(fixture);
    keydown(scroller, 'Z');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Zebra');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.model()[0].otherId).toBeNull();
    expect(scroller.querySelector('.tm-grid__cell-spin')).toBeNull(); // no pending
    const cell = cellAt(scroller, 0, 2)!;
    expect(cell.textContent!.trim()).toBe('Zebra');
    expect(cell.classList.contains('tm-grid__cell--error')).toBe(true);
  });

  it('an emptied editor clears the cell (never parses or resolves the empty string)', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'F2');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, '');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.model()[0].agentId).toBeNull();
    expect(host.resolveCalls).toHaveLength(0);
    expect(cellAt(scroller, 0, 1)!.classList.contains('tm-grid__cell--error')).toBe(false);
  });

  it('undo while the resolution is pending aborts it and restores the value', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'AG-0001'); // a code: only the resolver knows it
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.resolveCalls).toHaveLength(1);
    scroller.focus();
    keydown(scroller, 'z', { ctrlKey: true });
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(5);
    expect(scroller.querySelector('.tm-grid__cell-spin')).toBeNull();
    // The late outcome is discarded.
    host.resolveCalls[0].deferred.resolve(new Map([['AG-0001', { value: 1 }]]));
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(5);
  });

  it('a manual re-edit over the pending cell supersedes the late resolution', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'A');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'AG-0001'); // a code: only the resolver knows it
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    // Re-edit while pending: the parse rung commits #4 synchronously.
    scroller.focus();
    keydown(scroller, '#');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, '#4');
    await stable(fixture);
    (document.getElementById('outside-entity') as HTMLInputElement).focus();
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(4);
    host.resolveCalls[0].deferred.resolve(new Map([['AG-0001', { value: 1 }]]));
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(4); // the stale outcome never landed
  });

  it('a mid-edit modal holds the session open; a pick commits through it with no move', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'F2');
    await stable(fixture);
    const magnifier = scroller.querySelector<HTMLButtonElement>('.tm-entity-picker__magnifier');
    expect(magnifier).not.toBeNull();
    magnifier!.click();
    await stable(fixture);
    // The modal is up and the edit session is still open behind it.
    expect(document.querySelector('[data-testid="page-pick"]')).not.toBeNull();
    expect(pickerInput(scroller)).not.toBeNull();

    (document.querySelector('[data-testid="page-pick"]') as HTMLButtonElement).click();
    await stable(fixture);
    await stable(fixture);
    expect(host.model()[0].agentId).toBe(3); // the pick committed the cell…
    expect(pickerInput(scroller)).toBeNull(); // …and closed the editor
    expect(document.activeElement).toBe(cellAt(scroller, 0, 1)); // no move
  });

  it('dev mode throws for a no-config entity column and for search without accessors', async () => {
    @Component({
      imports: [TmGrid, TmGridColumn],
      template: `
        <tm-grid gridId="entity-noconfig-grid" [field]="f" [rowId]="rowId" style="block-size: 200px">
          <!-- [parse] makes the column editable, so the open reaches the guard. -->
          <tm-grid-column key="agentId" type="entity" header="Agent" [format]="fmt" [parse]="parse" />
        </tm-grid>
      `,
    })
    class NoConfigHost {
      readonly model = signal<AgentLine[]>([
        { id: 1, name: null, agentId: 5, otherId: null },
      ]);
      readonly f = form(this.model);
      readonly rowId = (row: AgentLine): number => row.id;
      readonly fmt = (value: number | null): string => String(value ?? '');
      readonly parse = (): number | typeof TM_PARSE_ERROR => TM_PARSE_ERROR;
    }
    // The throw happens inside an Angular-wrapped keydown listener, so it
    // routes through ErrorHandler rather than surfacing here.
    const captured: unknown[] = [];
    TestBed.configureTestingModule({
      rethrowApplicationErrors: false, // the capture below IS the assertion
      providers: [
        provideTellmaUi(),
        {
          provide: ErrorHandler,
          useValue: {
            handleError: (error: unknown) => captured.push(error),
          } satisfies ErrorHandler,
        },
      ],
    });
    const fixture = TestBed.createComponent(NoConfigHost);
    await stable(fixture);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'Enter');
    await stable(fixture);
    expect(captured.some((e) => String(e).includes("of type 'entity' has no editor"))).toBe(true);
    expect(scroller.querySelector('[data-tm-editor] input')).toBeNull();
  });

  it('dev mode throws when [search] is bound without [itemId]/[itemLabel]', async () => {
    @Component({
      imports: [TmGrid, TmGridColumn],
      template: `
        <tm-grid gridId="entity-halfconfig-grid" [field]="f" [rowId]="rowId" style="block-size: 200px">
          <tm-grid-column
            key="agentId"
            type="entity"
            header="Agent"
            [format]="fmt"
            [search]="search"
          />
        </tm-grid>
      `,
    })
    class HalfConfigHost {
      readonly model = signal<AgentLine[]>([
        { id: 1, name: null, agentId: 5, otherId: null },
      ]);
      readonly f = form(this.model);
      readonly rowId = (row: AgentLine): number => row.id;
      readonly fmt = (value: number | null): string => String(value ?? '');
      readonly search = (query: string): readonly Agent[] => searchAgents(query);
    }
    const captured: unknown[] = [];
    TestBed.configureTestingModule({
      rethrowApplicationErrors: false, // the capture below IS the assertion
      providers: [
        provideTellmaUi(),
        {
          provide: ErrorHandler,
          useValue: {
            handleError: (error: unknown) => captured.push(error),
          } satisfies ErrorHandler,
        },
      ],
    });
    const fixture = TestBed.createComponent(HalfConfigHost);
    await stable(fixture);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'Enter');
    await stable(fixture);
    expect(captured.some((e) => String(e).includes('without [itemId]/[itemLabel]'))).toBe(true);
  });

  it('a modal dismissal returns to the intact edit session', async () => {
    const { fixture, host, scroller } = await setupEntity();
    await activateAgentCell(fixture, scroller);
    keydown(scroller, 'F2');
    await stable(fixture);
    typeInto(pickerInput(scroller)!, 'Ali');
    await stable(fixture);
    scroller.querySelector<HTMLButtonElement>('.tm-entity-picker__magnifier')!.click();
    await stable(fixture);
    (document.querySelector('[data-testid="page-close"]') as HTMLButtonElement).click();
    await stable(fixture);
    await stable(fixture);
    expect(pickerInput(scroller)).not.toBeNull(); // the session survived
    expect(pickerInput(scroller)!.value).toBe('Ali'); // the text survived
    expect(host.model()[0].agentId).toBe(5); // nothing was written
  });
});
