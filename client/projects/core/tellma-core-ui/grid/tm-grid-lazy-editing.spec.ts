// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, ErrorHandler, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { form } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';

import { ɵTM_GRID_EDITING_LOADER, type ɵTmGridEditingCode } from './internal/grid-core';
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
}

/** Three fresh rows. */
function makeLines(): Line[] {
  return [
    { id: 1, name: 'Alpha', unit: 'kg' },
    { id: 2, name: 'Beta', unit: null },
    { id: 3, name: 'Gamma', unit: 'pcs' },
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
    <tm-grid
      gridId="lazy-edit-grid"
      [field]="f"
      [rowId]="rowId"
      [readonly]="readonly()"
      style="block-size: 240px"
    >
      <tm-grid-column key="name" header="Name" [width]="140" />
      <tm-grid-column key="unit" type="enum" header="Unit" [options]="units" [width]="100" />
    </tm-grid>
    <input id="outside-input" />
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
  /** The enum column's options. */
  readonly units = ['kg', 'pcs'];
  /** Reads a row's identity. */
  readonly rowId = (row: Line): number => row.id;
}

// ---- helpers ----

/** The real editing code, loaded the way the grid's default loader does. */
function realCode(): Promise<ɵTmGridEditingCode> {
  return import('@tellma/core-ui/grid-editing');
}

/** A loader that counts its calls and loads the real code at once. */
function countingLoader(): { readonly calls: () => number; readonly load: () => Promise<ɵTmGridEditingCode> } {
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
function gatedLoader(): { readonly release: () => void; readonly load: () => Promise<ɵTmGridEditingCode> } {
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

/** The cell element at a view row and column. */
function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
  );
}

/** A mouse press and release on `target`. */
function pointerPress(target: Element): void {
  const init: PointerEventInit = { bubbles: true, cancelable: true, button: 0, pointerType: 'mouse' };
  target.dispatchEvent(new PointerEvent('pointerdown', init));
  target.dispatchEvent(new PointerEvent('pointerup', init));
}

/** The open editor's input, or `null`. */
function editorInput(scroller: HTMLElement): HTMLInputElement | null {
  return scroller.querySelector<HTMLInputElement>('[data-tm-editor] input');
}

/** The grid's scroller. */
function scrollerOf(fixture: ComponentFixture<unknown>): HTMLElement {
  return (fixture.nativeElement as HTMLElement).querySelector('.tm-grid__scroller') as HTMLElement;
}

/** Creates a host with the given loader in place of the grid's own. */
function create<H>(host: new () => H, load: () => Promise<ɵTmGridEditingCode>): ComponentFixture<H> {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), { provide: ɵTM_GRID_EDITING_LOADER, useValue: load }],
  });
  return TestBed.createComponent(host);
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
    cellAt(scroller, 1, 0)!.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
    await stable(fixture);

    expect(loader.calls()).toBe(0);
    expect(editorInput(scroller)).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('.tm-grid__status')).toBeNull();
  });

  it('a field-bound grid asks only once it is editable, then exactly once', async () => {
    const loader = countingLoader();
    TestBed.configureTestingModule({
      providers: [provideTellmaUi(), { provide: ɵTM_GRID_EDITING_LOADER, useValue: loader.load }],
    });
    const fixture = TestBed.createComponent(EditHost);
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

  it('the first key typed while the code loads seeds the editor it opens', async () => {
    const loader = gatedLoader();
    const fixture = create(EditHost, loader.load);
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    // The status bar's slot holds its place before the bar arrives.
    expect((fixture.nativeElement as HTMLElement).querySelector('.tm-grid__status')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('tm-grid-status-bar')).toBeNull();

    scroller.focus();
    keydown(scroller, 'ArrowDown'); // activates (0,0), Name
    await settle(fixture);
    const typed = keydown(scroller, 'R');
    await settle(fixture);
    // Consumed, and the cell is already editing; its editor is on its way.
    expect(typed.defaultPrevented).toBe(true);
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--editing')).toBe(true);
    expect(editorInput(scroller)).toBeNull();

    loader.release();
    await stable(fixture);
    const input = editorInput(scroller);
    expect(input?.value).toBe('R');
    expect(document.activeElement).toBe(input);
  });

  it('keys typed while the code loads replay in order: characters extend, Enter commits and moves', async () => {
    const loader = gatedLoader();
    const fixture = create(EditHost, loader.load);
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await settle(fixture);

    for (const key of ['R', 'e', 'x', 'Backspace', 'd', 'Enter']) {
      // While no editor exists, every key lands on the grid.
      keydown(document.activeElement ?? scroller, key);
    }
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');

    loader.release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Red');
    expect(editorInput(scroller)).toBeNull();
    expect(document.activeElement).toBe(cellAt(scroller, 1, 0));
  });

  it('a key typed after a held commit opens the next editor with it', async () => {
    const loader = gatedLoader();
    const fixture = create(EditHost, loader.load);
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await settle(fixture);

    for (const key of ['A', 'Enter', 'B', 'c']) {
      keydown(document.activeElement ?? scroller, key);
    }
    loader.release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('A');
    expect(editorInput(scroller)?.value).toBe('Bc');
  });

  it('Escape typed while the code loads cancels the held session', async () => {
    const loader = gatedLoader();
    const fixture = create(EditHost, loader.load);
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await settle(fixture);

    keydown(scroller, 'Z');
    keydown(scroller, 'Escape');
    loader.release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Alpha');
    expect(editorInput(scroller)).toBeNull();
    expect(cellAt(scroller, 0, 0)!.classList.contains('tm-grid__cell--editing')).toBe(false);
  });

  it('a press on another cell while the code loads commits the held text, then activates the press', async () => {
    const loader = gatedLoader();
    const fixture = create(EditHost, loader.load);
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await settle(fixture);

    keydown(scroller, 'Q');
    pointerPress(cellAt(scroller, 2, 0)!);
    await settle(fixture);
    loader.release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('Q');
    expect(editorInput(scroller)).toBeNull();
    expect(document.activeElement).toBe(cellAt(scroller, 2, 0));
  });

  it('focus moved out of the grid while the code loads commits and stays outside', async () => {
    const loader = gatedLoader();
    const fixture = create(EditHost, loader.load);
    await settle(fixture);
    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await settle(fixture);

    keydown(scroller, 'W');
    const outside = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#outside-input')!;
    outside.focus(); // the focusout commit is held
    loader.release();
    await stable(fixture);
    expect(fixture.componentInstance.model()[0].name).toBe('W');
    expect(document.activeElement).toBe(outside);
  });

  it('a failed load cancels the held session, reports the error, and the next open asks again', async () => {
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
      // The failure is the app's to see through its error handler, as in
      // production, not a failure of this test.
      rethrowApplicationErrors: false,
    });
    const fixture = TestBed.createComponent(EditHost);
    await stable(fixture);
    expect(calls).toBe(1);
    expect(errors.some((error) => String(error).includes('chunk offline'))).toBe(true);

    const scroller = scrollerOf(fixture);
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    keydown(scroller, 'K');
    await stable(fixture);
    expect(calls).toBe(2);
    expect(editorInput(scroller)?.value).toBe('K');
  });
});
