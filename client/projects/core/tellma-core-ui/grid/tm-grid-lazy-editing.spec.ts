// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, ErrorHandler, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
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
  /** An entity column with the built-in picker. */
  readonly agentId: number | null;
  /** An entity column bound to no editor at all. */
  readonly ownerId: number | null;
}

/** An agent the entity column searches and resolves. */
interface Agent {
  /** The agent's id. */
  readonly id: number;
  /** The agent's label. */
  readonly label: string;
}

/** The agent directory. */
const AGENTS: readonly Agent[] = [
  { id: 11, label: 'Alice' },
  { id: 12, label: 'Bob' },
];

/** Three fresh rows. */
function makeLines(): Line[] {
  return [
    { id: 1, name: 'Alpha', unit: 'kg', agentId: 11, ownerId: null },
    { id: 2, name: 'Beta', unit: null, agentId: null, ownerId: null },
    { id: 3, name: 'Gamma', unit: 'pcs', agentId: 12, ownerId: null },
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
        <tm-grid-column key="name" header="Name" [width]="140" />
        <tm-grid-column key="unit" type="enum" header="Unit" [options]="units" [width]="100" />
        <tm-grid-column
          key="agentId"
          type="entity"
          header="Agent"
          [width]="140"
          [search]="search"
          [itemId]="agentId"
          [itemLabel]="agentLabel"
          [format]="formatAgent"
          [resolvePastedLabels]="resolveAgents"
        />
        <tm-grid-column key="ownerId" type="entity" header="Owner" [width]="100" />
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
  /** The enum column's options. */
  readonly units = ['kg', 'pcs'];
  /** Reads a row's identity. */
  readonly rowId = (row: Line): number => row.id;
  /** The entity column's search: agents whose label contains the query. */
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
  /** Resolves typed or pasted labels to agent ids. */
  readonly resolveAgents = (labels: string[]): Promise<ReadonlyMap<string, TmLabelResolution<number>>> =>
    Promise.resolve(
      new Map(
        labels.map((label): [string, TmLabelResolution<number>] => {
          const agent = AGENTS.find((candidate) => candidate.label === label);
          return [label, agent === undefined ? { error: 'notFound' } : { value: agent.id }];
        }),
      ),
    );
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

/** Waits until the grid is stable, the editing code's load included. */
async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

/**
 * Renders without waiting for stability: while the editing code is held
 * open its load is a pending task, so `whenStable` would wait for it.
 */
async function settle(fixture: ComponentFixture<unknown>): Promise<void> {
  fixture.detectChanges();
  await new Promise((resolve) => setTimeout(resolve, 0));
  fixture.detectChanges();
}

/** Dispatches a keydown on `target` and returns it. */
function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

/** Types each key on whatever holds focus (the grid, while no editor exists). */
function type(keys: readonly string[]): void {
  for (const key of keys) {
    keydown(document.activeElement ?? document.body, key);
  }
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

/** A double-click on `target`: two presses, then the dblclick at its centre. */
function doubleClick(target: Element): void {
  pointerPress(target);
  pointerPress(target);
  const rect = target.getBoundingClientRect();
  target.dispatchEvent(
    new MouseEvent('dblclick', {
      bubbles: true,
      clientX: rect.x + rect.width / 2,
      clientY: rect.y + rect.height / 2,
    }),
  );
}

/** The open editor's input, or `null`. */
function editorInput(scroller: HTMLElement): HTMLInputElement | null {
  return scroller.querySelector<HTMLInputElement>('[data-tm-editor] input');
}

/** The draft text the editing cell shows, or `null` when none shows. */
function draftText(scroller: HTMLElement): string | null {
  return scroller.querySelector('[data-tm-draft]')?.textContent ?? null;
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
function create<H>(host: new () => H, load: () => Promise<ɵTmGridEditingCode>): ComponentFixture<H> {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), { provide: ɵTM_GRID_EDITING_LOADER, useValue: load }],
  });
  return TestBed.createComponent(host);
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
    expect(editorInput(scroller)).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.tm-grid__status')).toBeNull();
  });

  it('a field-bound grid asks only once it is editable, then exactly once', async () => {
    const loader = countingLoader();
    const fixture = create(EditHost, loader.load);
    fixture.componentInstance.readonly.set(true);
    await stable(fixture);
    expect(loader.calls()).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar')).toBeNull();

    // Editable: the code is asked for at once, ahead of any keystroke, and
    // the status bar arrives with it.
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

  it('the first key typed while the code loads shows in the cell, and the editor opens with it', async () => {
    const { fixture, scroller, release } = await openWindow();
    // The status bar's slot holds its place before the bar arrives.
    expect((fixture.nativeElement as HTMLElement).querySelector('.tm-grid__status')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar')).toBeNull();

    const typed = keydown(scroller, 'R');
    await settle(fixture);
    expect(typed.defaultPrevented).toBe(true);
    expect(cellAt(scroller, 0, 0).classList.contains('tm-grid__cell--editing')).toBe(true);
    expect(draftText(scroller)).toBe('R');
    expect(editorInput(scroller)).toBeNull();

    type(['e', 'x', 'Backspace', 'd']);
    await settle(fixture);
    expect(draftText(scroller)).toBe('Red');

    release();
    await stable(fixture);
    const input = editorInput(scroller);
    expect(input?.value).toBe('Red');
    expect(document.activeElement).toBe(input);
    expect(draftText(scroller)).toBeNull();
  });

  it('Enter typed while the code loads commits the draft at once and moves', async () => {
    const { fixture, scroller, release } = await openWindow();
    type(['R', 'e', 'd', 'Enter']);
    // Committed synchronously: no session outlives the commit.
    expect(fixture.componentInstance.model()[0].name).toBe('Red');
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    expect(document.activeElement).toBe(cellAt(scroller, 1, 0));

    // The next key opens the next draft; the editor mounts with it.
    type(['B', 'c']);
    release();
    await stable(fixture);
    expect(editorInput(scroller)?.value).toBe('Bc');
    expect(fixture.componentInstance.model()[0].name).toBe('Red');
  });

  it('an outside click handler sees the typed value: the blur commits before the click', async () => {
    const { fixture } = await openWindow();
    type(['W']);
    const save = byId<HTMLButtonElement>(fixture, 'outside-save');
    save.focus(); // the commit-on-blur
    save.click();
    expect(fixture.componentInstance.saved).toBe('W');
  });

  it('a press back into the grid from outside focuses the pressed cell', async () => {
    const { fixture, scroller } = await openWindow();
    type(['W']);
    const outside = byId<HTMLInputElement>(fixture, 'outside-input');
    outside.focus();
    await settle(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('W');

    pointerPress(cellAt(scroller, 2, 0));
    await settle(fixture);
    expect(document.activeElement).toBe(cellAt(scroller, 2, 0));
  });

  it('a double-click on another cell commits the draft first, then opens that cell', async () => {
    const { fixture, scroller, release } = await openWindow();
    type(['Q']);
    doubleClick(cellAt(scroller, 2, 0));
    await settle(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Q');

    release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Q');
    expect(cellAt(scroller, 2, 0).classList.contains('tm-grid__cell--editing')).toBe(true);
    expect(editorInput(scroller)?.value).toBe('Gamma');
  });

  it('Escape typed while the code loads cancels the draft at once', async () => {
    const { fixture, scroller } = await openWindow();
    type(['Z']);
    const escape = keydown(document.activeElement!, 'Escape');
    expect(escape.defaultPrevented).toBe(true);
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    expect(draftText(scroller)).toBeNull();
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));
  });

  it('an AltGr character extends the draft, and a bare Alt chord commits it and reaches the page', async () => {
    const { fixture, scroller } = await openWindow();
    type(['a']);
    // Windows reports AltGr as Ctrl+Alt with AltGraph.
    const altGr = keydown(document.activeElement!, '@', {
      ctrlKey: true,
      altKey: true,
      modifierAltGraph: true,
    });
    type(['b']);
    await settle(fixture);
    expect(altGr.defaultPrevented).toBe(true);
    expect(draftText(scroller)).toBe('a@b');

    const chord = keydown(document.activeElement!, 's', { altKey: true });
    expect(chord.defaultPrevented).toBe(false);
    expect(fixture.componentInstance.model()[0].name).toBe('a@b');
  });

  it('Mod+V while the code loads commits the draft and leaves the paste to the grid', async () => {
    const { fixture } = await openWindow();
    type(['P']);
    const paste = keydown(document.activeElement!, 'v', { ctrlKey: true, metaKey: true });
    expect(paste.defaultPrevented).toBe(false);
    expect(fixture.componentInstance.model()[0].name).toBe('P');

    const data = new DataTransfer();
    data.setData('text/plain', 'Pasted');
    document.activeElement!.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }),
    );
    await settle(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Pasted');
  });

  it('F2 then typing while the code loads edits the cell text, and Enter commits it', async () => {
    const { fixture, scroller } = await openWindow();
    type(['F2']);
    await settle(fixture);
    expect(draftText(scroller)).toBe('Alpha');
    type(['x', 'Enter']);
    expect(fixture.componentInstance.model()[0].name).toBe('Alphax');

    // An untouched edit-mode draft writes nothing.
    type(['ArrowUp', 'F2', 'Enter']);
    expect(fixture.componentInstance.model()[0].name).toBe('Alphax');
  });

  it("an entity draft commits through the column's label resolution", async () => {
    const { fixture, scroller } = await openWindow();
    keydown(scroller, 'ArrowRight');
    keydown(scroller, 'ArrowRight'); // (0,2), Agent
    await settle(fixture);
    type(['B', 'o', 'b', 'Enter']);
    await settle(fixture);
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(fixture.componentInstance.model()[0].agentId).toBe(12);
  });

  it('a column with no editor opens no draft: the key acts as on any cell', async () => {
    const { fixture, scroller } = await openWindow();
    for (let i = 0; i < 3; i++) {
      keydown(scroller, 'ArrowRight'); // (0,3), Owner: an entity column without a picker
    }
    await settle(fixture);
    const typed = keydown(document.activeElement!, 'x');
    expect(typed.defaultPrevented).toBe(false);
    keydown(document.activeElement!, 'Enter');
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    expect(document.activeElement).toBe(cellAt(scroller, 1, 3));
  });

  it('a readonly flip, or the grid going away, cancels the draft and the arrival opens nothing', async () => {
    const { fixture, scroller, release } = await openWindow();
    type(['W']);
    fixture.componentInstance.readonly.set(true);
    await settle(fixture);
    expect(anyEditing(scroller)).toBe(false);
    fixture.componentInstance.readonly.set(false);
    await settle(fixture);

    type(['V']);
    await settle(fixture);
    expect(draftText(scroller)).toBe('V');
    fixture.componentInstance.shown.set(false);
    await settle(fixture);
    release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');
  });

  it('the arrival leaves focus where the user moved it', async () => {
    const { fixture, scroller, release } = await openWindow();
    type(['M']);
    // Focus moving to an overlay surface keeps the session open (an
    // editor's own popups live there), so the draft is still open on arrival.
    const overlayInput = byId<HTMLInputElement>(fixture, 'overlay-input');
    overlayInput.focus();
    release();
    await stable(fixture);
    expect(editorInput(scroller)?.value).toBe('M');
    expect(document.activeElement).toBe(overlayInput);
  });

  it('a stalled load times out: the draft commits, the error is reported, and the next open asks again', async () => {
    const errors: unknown[] = [];
    let calls = 0;
    const load = (): Promise<ɵTmGridEditingCode> => {
      calls += 1;
      return calls === 1 ? new Promise<never>(() => undefined) : realCode();
    };
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
    const fixture = TestBed.createComponent(EditHost);
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    type(['T']);
    // The pending task ends with the timeout, so the app becomes stable.
    await stable(fixture);
    expect(errors.some((error) => String(error).includes('did not load'))).toBe(true);
    expect(fixture.componentInstance.model()[0].name).toBe('T');
    expect(anyEditing(scroller)).toBe(false);

    keydown(document.activeElement!, 'F2');
    await stable(fixture);
    expect(calls).toBe(2);
    expect(editorInput(scroller)?.value).toBe('T');
  });

  it('a failed load reports the error, and a paste asks again', async () => {
    const errors: unknown[] = [];
    let calls = 0;
    const load = (): Promise<ɵTmGridEditingCode> => {
      calls += 1;
      return calls === 1 ? Promise.reject(new Error('chunk offline')) : realCode();
    };
    TestBed.configureTestingModule({
      providers: [
        provideTellmaUi(),
        { provide: ɵTM_GRID_EDITING_LOADER, useValue: load },
        { provide: ErrorHandler, useValue: { handleError: (error: unknown) => errors.push(error) } },
      ],
      rethrowApplicationErrors: false,
    });
    const fixture = TestBed.createComponent(EditHost);
    await stable(fixture);
    expect(calls).toBe(1);
    expect(errors.some((error) => String(error).includes('chunk offline'))).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar')).toBeNull();

    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    const data = new DataTransfer();
    data.setData('text/plain', 'Pasted');
    document.activeElement!.dispatchEvent(
      new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(calls).toBe(2);
    expect((fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar')).not.toBeNull();
  });
});
