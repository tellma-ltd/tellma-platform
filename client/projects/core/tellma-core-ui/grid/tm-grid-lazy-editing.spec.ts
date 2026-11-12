// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, ErrorHandler, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { form } from '@angular/forms/signals';

import type { TmLabelResolution } from '@tellma/core-ui/contracts';
import { provideTellmaUi } from '@tellma/core-ui';

import {
  ɵTM_GRID_EDITING_LOAD_TIMEOUT,
  ɵTM_GRID_EDITING_LOADER,
  type ɵTmGridEditingCode,
} from './internal/grid-core';
import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';

/** One grid row. */
interface Line {
  /** The row's identity. */
  readonly id: number;
  /** A text column. */
  readonly name: string | null;
  /** An enum column. */
  readonly unit: string | null;
  /** An entity column with the built-in picker and a resolver. */
  readonly agentId: number | null;
  /** An entity column with the built-in picker and no resolver. */
  readonly memberId: number | null;
  /** An entity column bound to no editor at all. */
  readonly ownerId: number | null;
}

/** An agent the entity columns search and resolve. */
interface Agent {
  /** The agent's id. */
  readonly id: number;
  /** The agent's label. */
  readonly label: string;
}

/** The agent directory; two agents share a label. */
const AGENTS: readonly Agent[] = [
  { id: 11, label: 'Alice' },
  { id: 12, label: 'Bob' },
  { id: 13, label: 'Dana' },
  { id: 14, label: 'Dana' },
];

/** Three fresh rows. */
function makeLines(): Line[] {
  return [
    { id: 1, name: 'Alpha', unit: 'kg', agentId: 11, memberId: null, ownerId: null },
    { id: 2, name: 'Beta', unit: null, agentId: null, memberId: null, ownerId: null },
    { id: 3, name: 'Gamma', unit: 'pcs', agentId: 12, memberId: null, ownerId: null },
  ];
}

// ---- hosts: a readonly list grid, and a field-bound grid whose readonly flag flips ----

@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid gridId="lazy-list-grid" [data]="rows" [rowId]="rowId" style="block-size: 240px">
      <tm-grid-column key="name" header="Name" [width]="140" />
      <tm-grid-column key="unit" type="enum" header="Unit" [options]="units" [width]="100" />
    </tm-grid>
  `,
})
/** A readonly list: rows bound through `data`. */
class ListHost {
  /** The bound rows. */
  readonly rows = makeLines();
  /** The enum column's options. */
  readonly units = ['kg', 'pcs'];
  /** Reads a row's identity. */
  readonly rowId = (row: Line): number => row.id;
}

@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    @if (shown()) {
      <tm-grid
        gridId="lazy-edit-grid"
        [field]="f"
        [rowId]="rowId"
        [readonly]="readonly()"
        style="block-size: 240px"
      >
        <tm-grid-column key="name" header="Name" [width]="120" />
        <tm-grid-column key="unit" type="enum" header="Unit" [options]="units" [width]="90" />
        <tm-grid-column
          key="agentId"
          type="entity"
          header="Agent"
          [width]="110"
          [search]="search"
          [itemId]="agentId"
          [itemLabel]="agentLabel"
          [format]="formatAgent"
          [resolvePastedLabels]="resolveAgents"
        />
        <tm-grid-column
          key="memberId"
          type="entity"
          header="Member"
          [width]="110"
          [search]="search"
          [itemId]="agentId"
          [itemLabel]="agentLabel"
          [format]="formatAgent"
        />
        <tm-grid-column key="ownerId" type="entity" header="Owner" [width]="90" />
      </tm-grid>
    }
    <input id="outside-input" />
    <button id="outside-save" type="button" (click)="saved = model()[0].name">Save</button>
    <div class="cdk-overlay-pane"><input id="overlay-input" /></div>
  `,
})
/** A field-bound grid, editable until `readonly` is set. */
class EditHost {
  /** The rows' model. */
  readonly model = signal<Line[]>(makeLines());
  /** The field tree the grid edits. */
  readonly f = form(this.model);
  /** The grid's readonly flag. */
  readonly readonly = signal(false);
  /** Whether the grid is rendered at all. */
  readonly shown = signal(true);
  /** What the outside Save button read from the model when clicked. */
  saved: string | null | undefined;
  /** How many times the resolver ran. */
  resolverCalls = 0;
  /** The enum column's options. */
  readonly units = ['kg', 'pallet', 'pcs'];
  /** Reads a row's identity. */
  readonly rowId = (row: Line): number => row.id;
  /** The entity columns' search: agents whose label contains the query. */
  readonly search = (query: string): Promise<{ items: readonly Agent[]; hasMore: boolean }> =>
    Promise.resolve({
      items: AGENTS.filter((agent) => agent.label.toLowerCase().includes(query.toLowerCase())),
      hasMore: false,
    });
  /** An agent's id. */
  readonly agentId = (agent: Agent): number => agent.id;
  /** An agent's label. */
  readonly agentLabel = (agent: Agent): string => agent.label;
  /** Displays an agent id as its label. */
  readonly formatAgent = (id: unknown): string =>
    AGENTS.find((agent) => agent.id === id)?.label ?? '';
  /** Resolves typed or pasted labels to agent ids, counting its calls. */
  readonly resolveAgents = (
    labels: string[],
  ): Promise<ReadonlyMap<string, TmLabelResolution<number>>> => {
    this.resolverCalls += 1;
    return Promise.resolve(
      new Map(
        labels.map((label): [string, TmLabelResolution<number>] => {
          const agent = AGENTS.find((candidate) => candidate.label === label);
          return [label, agent === undefined ? { error: 'notFound' } : { value: agent.id }];
        }),
      ),
    );
  };
}

// ---- helpers ----

/** The real editing code, loaded the way the grid's default loader does. */
function realCode(): Promise<ɵTmGridEditingCode> {
  return import('@tellma/core-ui/grid-editing');
}

/** A loader that counts its calls and loads the real code at once. */
function countingLoader(): {
  readonly calls: () => number;
  readonly load: () => Promise<ɵTmGridEditingCode>;
} {
  let calls = 0;
  return {
    calls: () => calls,
    load: () => {
      calls += 1;
      return realCode();
    },
  };
}

/**
 * A loader held open until `release()`: the window in which the grid has
 * asked for its editing code and the code has not arrived.
 */
function gatedLoader(): {
  readonly release: () => void;
  readonly load: () => Promise<ɵTmGridEditingCode>;
} {
  let open!: () => void;
  const gate = new Promise<void>((resolve) => (open = resolve));
  return { release: () => open(), load: () => gate.then(realCode) };
}

/** Yields one macrotask. */
function macrotask(ms = 0): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Waits until the grid is stable, the editing code's load included. */
async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await macrotask();
  await fixture.whenStable();
}

/**
 * Renders without waiting for stability: while the editing code is held
 * open its load is a pending task, so `whenStable` would wait for it.
 */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  fixture.detectChanges();
  await macrotask();
  fixture.detectChanges();
}

/** Dispatches a keydown on `target` and returns it. */
function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

/** Dispatches a keydown on whatever holds focus. */
function press(key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  return keydown(document.activeElement ?? document.body, key, init);
}

/** Types text into an input at its caret, as the browser does for typed keys. */
function typeText(input: HTMLInputElement, text: string): void {
  const start = input.selectionStart ?? input.value.length;
  const end = input.selectionEnd ?? input.value.length;
  input.setRangeText(text, start, end, 'end');
  input.dispatchEvent(
    new InputEvent('input', { bubbles: true, data: text, inputType: 'insertText' }),
  );
}

/** The cell element at a view row and column. */
function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
  )!;
}

/** A mouse press and release on `target`, at its centre. */
function pointerPress(target: Element): void {
  const rect = target.getBoundingClientRect();
  const init: PointerEventInit = {
    bubbles: true,
    cancelable: true,
    button: 0,
    pointerType: 'mouse',
    clientX: rect.x + rect.width / 2,
    clientY: rect.y + rect.height / 2,
  };
  target.dispatchEvent(new PointerEvent('pointerdown', init));
  target.dispatchEvent(new PointerEvent('pointerup', init));
}

/** The dblclick event alone, at the centre of `target`. */
function dblclickOnly(target: Element): void {
  const rect = target.getBoundingClientRect();
  target.dispatchEvent(
    new MouseEvent('dblclick', {
      bubbles: true,
      clientX: rect.x + rect.width / 2,
      clientY: rect.y + rect.height / 2,
    }),
  );
}

/** A double-click on `target`: two presses, then the dblclick at its centre. */
function doubleClick(target: Element): void {
  pointerPress(target);
  pointerPress(target);
  dblclickOnly(target);
}

/** The draft input, or `null` when no draft is open. */
function draftInput(scroller: HTMLElement): HTMLInputElement | null {
  return scroller.querySelector<HTMLInputElement>('input[data-tm-draft]');
}

/** The open editor's input (never the draft's), or `null`. */
function editorInput(scroller: HTMLElement): HTMLInputElement | null {
  return scroller.querySelector<HTMLInputElement>('[data-tm-editor] input:not([data-tm-draft])');
}

/** Whether any cell is in its editing state. */
function anyEditing(scroller: HTMLElement): boolean {
  return scroller.querySelector('.tm-grid__cell--editing') !== null;
}

/** The grid's scroller. */
function scrollerOf(fixture: ComponentFixture<unknown>): HTMLElement {
  return (fixture.nativeElement as HTMLElement).querySelector('.tm-grid__scroller') as HTMLElement;
}

/** An element of the host by id. */
function byId<E extends HTMLElement>(fixture: ComponentFixture<unknown>, id: string): E {
  return (fixture.nativeElement as HTMLElement).querySelector<E>(`#${id}`)!;
}

/** Creates a host with the given loader in place of the grid's own. */
function create<H>(
  host: new () => H,
  load: () => Promise<ɵTmGridEditingCode>,
): ComponentFixture<H> {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), { provide: ɵTM_GRID_EDITING_LOADER, useValue: load }],
  });
  return TestBed.createComponent(host);
}

/**
 * Creates an editable host with an error handler that records what it is
 * given, a short load timeout, and the given loader.
 */
function createReporting(load: () => Promise<ɵTmGridEditingCode>): {
  readonly fixture: ComponentFixture<EditHost>;
  readonly errors: unknown[];
} {
  const errors: unknown[] = [];
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: ɵTM_GRID_EDITING_LOADER, useValue: load },
      { provide: ɵTM_GRID_EDITING_LOAD_TIMEOUT, useValue: 50 },
      { provide: ErrorHandler, useValue: { handleError: (error: unknown) => errors.push(error) } },
    ],
    // The failure is the app's to see through its error handler, as in
    // production, not a failure of this test.
    rethrowApplicationErrors: false,
  });
  return { fixture: TestBed.createComponent(EditHost), errors };
}

/**
 * Creates an editable host whose editing code is held open, focuses its
 * grid and activates (0,0), the Name cell.
 */
async function openWindow(): Promise<{
  readonly fixture: ComponentFixture<EditHost>;
  readonly scroller: HTMLElement;
  readonly release: () => void;
}> {
  const loader = gatedLoader();
  const fixture = create(EditHost, loader.load);
  await settle(fixture);
  const scroller = scrollerOf(fixture);
  scroller.focus();
  keydown(scroller, 'ArrowDown');
  await settle(fixture);
  return { fixture, scroller, release: loader.release };
}

/** Activates a column of row 0 by arrowing right from (0,0). */
async function toColumn(fixture: ComponentFixture<unknown>, col: number): Promise<void> {
  for (let i = 0; i < col; i++) {
    press('ArrowRight');
  }
  await settle(fixture);
}

describe('tm-grid on-demand editing code', () => {
  it('a readonly grid never asks for the editing code, whatever the user does', async () => {
    const loader = countingLoader();
    const fixture = create(ListHost, loader.load);
    await stable(fixture);
    const scroller = scrollerOf(fixture);

    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    // Every key that opens an editor on an editable grid.
    for (const key of ['Enter', 'F2', 'x', 'Backspace']) {
      keydown(scroller, key);
      await stable(fixture);
    }
    keydown(scroller, 'ArrowDown', { altKey: true });
    doubleClick(cellAt(scroller, 1, 0));
    await stable(fixture);

    expect(loader.calls()).toBe(0);
    expect(draftInput(scroller)).toBeNull();
    expect(editorInput(scroller)).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.tm-grid__status')).toBeNull();
  });

  it('a field-bound grid preloads only once it is editable, then exactly once', async () => {
    const loader = countingLoader();
    const fixture = create(EditHost, loader.load);
    fixture.componentInstance.readonly.set(true);
    await stable(fixture);
    expect(loader.calls()).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar')).toBeNull();

    // Editable: the code is preloaded ahead of any keystroke (the wait for
    // idle is part of stability), and the status bar arrives with it.
    fixture.componentInstance.readonly.set(false);
    await stable(fixture);
    expect(loader.calls()).toBe(1);
    const slot = (fixture.nativeElement as HTMLElement).querySelector('.tm-grid__status');
    expect(slot?.querySelector('tm-grid-status-bar')).not.toBeNull();

    // A flip back and forth reuses what arrived.
    fixture.componentInstance.readonly.set(true);
    await stable(fixture);
    fixture.componentInstance.readonly.set(false);
    await stable(fixture);
    expect(loader.calls()).toBe(1);
  });

  it('a key typed while the code loads opens a focused draft input; the editor takes over its text and caret', async () => {
    const { fixture, scroller, release } = await openWindow();
    // The status bar's slot holds its place before the bar arrives.
    expect((fixture.nativeElement as HTMLElement).querySelector('.tm-grid__status')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar')).toBeNull();

    const typed = keydown(scroller, 'R');
    expect(typed.defaultPrevented).toBe(true);
    const input = draftInput(scroller)!;
    expect(input.value).toBe('R');
    expect(document.activeElement).toBe(input);
    await settle(fixture);
    // The draft's input keeps focus through renders, as an editor does.
    expect(document.activeElement).toBe(input);
    expect(cellAt(scroller, 0, 0).classList.contains('tm-grid__cell--editing')).toBe(true);
    expect(editorInput(scroller)).toBeNull();

    typeText(input, 'ed');
    input.setSelectionRange(1, 1);
    release();
    await stable(fixture);
    const editor = editorInput(scroller);
    expect(editor?.value).toBe('Red');
    expect(document.activeElement).toBe(editor);
    expect(editor?.selectionStart).toBe(1);
    expect(draftInput(scroller)).toBeNull();
  });

  it('modifier and dead-key keydowns leave the draft to the browser', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'a');
    const input = draftInput(scroller)!;
    // The keydowns a real keyboard sends around Shift, AltGr (Windows:
    // Control then AltGraph), CapsLock, a dead accent and an IME.
    for (const key of [
      'Shift',
      'Control',
      'AltGraph',
      'Alt',
      'Meta',
      'CapsLock',
      'Dead',
      'Process',
    ]) {
      const event = keydown(input, key, key === 'AltGraph' ? { ctrlKey: true, altKey: true } : {});
      expect(event.defaultPrevented).toBe(false);
    }
    const altGr = keydown(input, '@', { ctrlKey: true, altKey: true, modifierAltGraph: true });
    expect(altGr.defaultPrevented).toBe(false);
    expect(draftInput(scroller)).toBe(input);
    typeText(input, 'B@é');
    await settle(fixture);
    expect(draftInput(scroller)).toBe(input);
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');

    keydown(input, 'Enter');
    expect(fixture.componentInstance.model()[0].name).toBe('aB@é');
  });

  it('Enter in a draft commits at once and moves; the next key opens the next draft', async () => {
    const { fixture, scroller, release } = await openWindow();
    keydown(scroller, 'R');
    typeText(draftInput(scroller)!, 'ed');
    press('Enter');
    // Committed synchronously: no session outlives the commit.
    expect(fixture.componentInstance.model()[0].name).toBe('Red');
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    expect(document.activeElement).toBe(cellAt(scroller, 1, 0));

    press('B');
    typeText(draftInput(scroller)!, 'c');
    release();
    await stable(fixture);
    expect(editorInput(scroller)?.value).toBe('Bc');
    expect(fixture.componentInstance.model()[0].name).toBe('Red');
  });

  it('in a typed draft the arrows commit and move, as in the editor it stands in for', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'Q');
    const down = press('ArrowDown');
    expect(down.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.model()[0].name).toBe('Q');
    await settle(fixture);
    expect(document.activeElement).toBe(cellAt(scroller, 1, 0));
  });

  it('in an F2 draft the arrows stay with the caret, and Enter commits the edited text', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'F2');
    const input = draftInput(scroller)!;
    expect(input.value).toBe('Alpha');
    expect(input.selectionStart).toBe(5);
    const left = keydown(input, 'ArrowLeft');
    expect(left.defaultPrevented).toBe(false);
    expect(draftInput(scroller)).toBe(input);

    input.setSelectionRange(5, 5);
    typeText(input, 'x');
    keydown(input, 'Enter');
    expect(fixture.componentInstance.model()[0].name).toBe('Alphax');

    // An untouched edit-mode draft writes nothing.
    press('ArrowUp');
    press('F2');
    press('Enter');
    expect(fixture.componentInstance.model()[0].name).toBe('Alphax');
  });

  it('an outside click handler sees the typed value: the blur commits before the click', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'W');
    const save = byId<HTMLButtonElement>(fixture, 'outside-save');
    save.focus(); // the commit-on-blur
    save.click();
    expect(fixture.componentInstance.saved).toBe('W');
  });

  it('a press back into the grid from outside focuses the pressed cell', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'W');
    byId<HTMLInputElement>(fixture, 'outside-input').focus();
    await settle(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('W');

    pointerPress(cellAt(scroller, 2, 0));
    await settle(fixture);
    expect(document.activeElement).toBe(cellAt(scroller, 2, 0));
  });

  it('a double-click on another cell commits the draft first, then opens that cell', async () => {
    const { fixture, scroller, release } = await openWindow();
    keydown(scroller, 'Q');
    doubleClick(cellAt(scroller, 2, 0));
    await settle(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Q');
    expect(draftInput(scroller)?.value).toBe('Gamma');
    // The press asked for the cell's focus; the draft's input keeps it.
    expect(document.activeElement).toBe(draftInput(scroller));

    release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Q');
    expect(cellAt(scroller, 2, 0).classList.contains('tm-grid__cell--editing')).toBe(true);
    expect(editorInput(scroller)?.value).toBe('Gamma');
  });

  it('an open on another cell commits the draft first, even with no press before it', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'K');
    dblclickOnly(cellAt(scroller, 2, 0));
    expect(fixture.componentInstance.model()[0].name).toBe('K');
    expect(draftInput(scroller)?.value).toBe('Gamma');
  });

  it('Escape in a draft cancels it at once', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'Z');
    const escape = press('Escape');
    expect(escape.defaultPrevented).toBe(true);
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    expect(draftInput(scroller)).toBeNull();
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));
  });

  it("undo and redo keys in a draft are the input's own and never reach the grid's undo", async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'A');
    press('Enter'); // an earlier edit, on the grid's history
    expect(fixture.componentInstance.model()[0].name).toBe('A');

    press('F2');
    const input = draftInput(scroller)!;
    for (const mod of [{ ctrlKey: true }, { metaKey: true }]) {
      for (const [key, shiftKey] of [
        ['z', false],
        ['y', false],
        ['z', true],
      ] as const) {
        expect(keydown(input, key, { ...mod, shiftKey }).defaultPrevented).toBe(false);
      }
    }
    await settle(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('A');
    expect(draftInput(scroller)).toBe(input);
  });

  it('a paste into a draft stays with its input', async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'P');
    const input = draftInput(scroller)!;
    const data = new DataTransfer();
    data.setData('text/plain', 'Pasted');
    const paste = new ClipboardEvent('paste', {
      clipboardData: data,
      bubbles: true,
      cancelable: true,
    });
    input.dispatchEvent(paste);
    expect(paste.defaultPrevented).toBe(false);
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');
    expect(draftInput(scroller)).toBe(input);
  });

  it('an IME composition opens an empty draft, and the code waits for the composition to end', async () => {
    const { fixture, scroller, release } = await openWindow();
    const composing = keydown(scroller, 'Process', { isComposing: true });
    expect(composing.defaultPrevented).toBe(false);
    const input = draftInput(scroller)!;
    expect(input.value).toBe('');
    expect(document.activeElement).toBe(input);

    input.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
    typeText(input, 'か');
    release();
    await stable(fixture);
    // Mid-composition the draft keeps the input the composition started in.
    expect(draftInput(scroller)).toBe(input);
    expect(editorInput(scroller)).toBeNull();

    input.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true, data: 'か' }));
    await settle(fixture);
    expect(editorInput(scroller)?.value).toBe('か');
    press('Enter');
    expect(fixture.componentInstance.model()[0].name).toBe('か');
  });

  it("an entity draft waits in its cell's pending state, counted as pending, then resolves through the column's search", async () => {
    const { fixture, scroller, release } = await openWindow();
    const grid = fixture.debugElement.query(By.directive(TmGrid))
      .componentInstance as TmGrid<unknown>;
    await toColumn(fixture, 2); // Agent: search and resolver
    press('B');
    typeText(draftInput(scroller)!, 'ob');
    press('Enter');
    await settle(fixture);
    expect(cellAt(scroller, 0, 2).querySelector('.tm-grid__cell-spin')).not.toBeNull();
    expect(cellAt(scroller, 0, 2).classList.contains('tm-grid__cell--error')).toBe(false);
    // A Save gated on the counts waits: the cell is pending, not in error.
    expect(grid.pendingCount()).toBe(1);
    expect(grid.errorCount()).toBe(0);

    release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].agentId).toBe(12);
    expect(grid.pendingCount()).toBe(0);
    expect(fixture.componentInstance.resolverCalls).toBe(0);
  });

  it('an entity draft on a column with no resolver resolves typed text through the search', async () => {
    const { fixture, scroller, release } = await openWindow();
    await toColumn(fixture, 3); // Member: search only
    press('B');
    typeText(draftInput(scroller)!, 'ob');
    press('Enter');
    // A label two agents share is ambiguous, as the editor would find it.
    press('D');
    typeText(draftInput(scroller)!, 'ana');
    press('Enter');
    await settle(fixture);
    expect(cellAt(scroller, 0, 3).classList.contains('tm-grid__cell--error')).toBe(false);

    release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].memberId).toBe(12);
    expect(fixture.componentInstance.model()[1].memberId).toBeNull();
    expect(cellAt(scroller, 1, 3).classList.contains('tm-grid__cell--error')).toBe(true);
    expect(cellAt(scroller, 0, 3).classList.contains('tm-grid__cell--error')).toBe(false);
  });

  it('an enum draft hands the editor its whole text, which picks the option typing it reaches', async () => {
    const { fixture, scroller, release } = await openWindow();
    await toColumn(fixture, 1); // Unit: kg, pallet, pcs
    press('p');
    typeText(draftInput(scroller)!, 'c');
    release();
    await stable(fixture);
    press('Enter');
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].unit).toBe('pcs');
  });

  it('a column with no editor opens no draft: the key acts as on any cell', async () => {
    const { fixture, scroller } = await openWindow();
    await toColumn(fixture, 4); // Owner: an entity column without a picker
    const typed = press('x');
    expect(typed.defaultPrevented).toBe(false);
    expect(draftInput(scroller)).toBeNull();
    press('Enter');
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    expect(document.activeElement).toBe(cellAt(scroller, 1, 4));
  });

  it('a readonly flip, or the grid going away, cancels the draft and the arrival opens nothing', async () => {
    const { fixture, scroller, release } = await openWindow();
    keydown(scroller, 'W');
    fixture.componentInstance.readonly.set(true);
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    fixture.componentInstance.readonly.set(false);
    await settle(fixture);

    press('V');
    await settle(fixture);
    expect(draftInput(scroller)?.value).toBe('V');
    fixture.componentInstance.shown.set(false);
    await settle(fixture);
    release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');
  });

  it('a draft commits when focus moves into an overlay, and the arrival leaves focus there', async () => {
    const { fixture, scroller, release } = await openWindow();
    keydown(scroller, 'M');
    const overlayInput = byId<HTMLInputElement>(fixture, 'overlay-input');
    overlayInput.focus();
    expect(fixture.componentInstance.model()[0].name).toBe('M');
    release();
    await stable(fixture);
    expect(editorInput(scroller)).toBeNull();
    expect(document.activeElement).toBe(overlayInput);
  });

  it('the arrival commits a draft whose input lost focus within the grid, and moves no focus', async () => {
    const { fixture, scroller, release } = await openWindow();
    keydown(scroller, 'N');
    cellAt(scroller, 0, 0).focus(); // within the grid: no commit-on-blur
    expect(draftInput(scroller)).not.toBeNull();
    release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('N');
    expect(editorInput(scroller)).toBeNull();
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));
  });

  it('a stalled load leaves the draft open and usable, and reports once', async () => {
    let available = false;
    let calls = 0;
    const { fixture, errors } = createReporting(() => {
      calls += 1;
      return available ? realCode() : new Promise<never>(() => undefined);
    });
    // The preload stalls and times out; the app becomes stable.
    await stable(fixture);
    expect(errors.filter((error) => String(error).includes('no editing code'))).toHaveLength(1);

    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    keydown(scroller, '1');
    const input = draftInput(scroller)!;
    await macrotask();
    typeText(input, '2');
    // A second attempt times out mid-typing: the draft is not touched.
    await stable(fixture);
    typeText(input, '3');
    expect(draftInput(scroller)).toBe(input);
    expect(input.value).toBe('123');
    expect(errors).toHaveLength(1);

    // Enter is a gesture too: it commits and asks again, and the code arrives.
    available = true;
    const before = calls;
    press('Enter');
    expect(fixture.componentInstance.model()[0].name).toBe('123');
    await stable(fixture);
    expect(calls).toBe(before + 1);
    press('F2');
    expect(editorInput(scroller)?.value).toBe('Beta');
  });

  it('a failed load leaves the draft whole while every key asks again, and reports once', async () => {
    let available = false;
    let calls = 0;
    const { fixture, errors } = createReporting(() => {
      calls += 1;
      return available ? realCode() : Promise.reject(new Error('chunk offline'));
    });
    await stable(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    keydown(scroller, '1');
    const input = draftInput(scroller)!;
    for (const text of ['2', '3']) {
      await macrotask();
      keydown(input, text); // a gesture: the load is asked for again
      typeText(input, text);
    }
    await stable(fixture);
    expect(calls).toBeGreaterThan(2);
    expect(input.value).toBe('123');
    expect(errors).toHaveLength(1);
    expect(String(errors[0])).toContain('chunk offline');
    press('Enter');
    expect(fixture.componentInstance.model()[0].name).toBe('123');
    await stable(fixture);

    // A paste asks again too, and the code arrives.
    available = true;
    const data = new DataTransfer();
    data.setData('text/plain', 'Pasted');
    document.activeElement!.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar'),
    ).not.toBeNull();
  });

  it('a grid destroyed while its code loads ends the load: no timer, no report', async () => {
    const { fixture, errors } = createReporting(() => new Promise<never>(() => undefined));
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    keydown(scroller, 'T'); // starts the load at once
    fixture.componentInstance.shown.set(false);
    await settle(fixture);
    await fixture.whenStable();
    await macrotask(120);
    expect(errors).toHaveLength(0);
  });
});
