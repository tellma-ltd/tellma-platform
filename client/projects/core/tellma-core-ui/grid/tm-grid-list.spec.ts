// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, viewChild, ViewEncapsulation } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { form } from '@angular/forms/signals';

import type { TmRowId } from '@tellma/core-ui/contracts';
import { provideTellmaUi } from '@tellma/core-ui';
import { TmGridHarness } from '@tellma/core-ui-testing';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';
import { TmGridDriver } from './tm-grid-driver';
import { TmGridDisplayDef, TmGridHeaderDef } from './tm-grid-templates';
import type { TmGridRowActivation, TmGridSort } from './tm-grid-types';

// The list-page features of a readonly grid: row activation, the external
// active row and its keyboard driver, sortable headers, select-all across
// pages, row classes, and the row-number column switch.

interface Row {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
}

function makeRows(count: number, from = 0): readonly Row[] {
  return Array.from({ length: count }, (_, i) => ({
    id: from + i,
    name: `Item ${from + i}`,
    qty: 100 + from + i,
  }));
}

/** A list page: search field driving a readonly, selectable, sortable grid. */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridDisplayDef, TmGridDriver],
  template: `
    <input class="search" type="search" [tmGridDriver]="grid" />
    <tm-grid
      #grid
      class="paint-probe"
      gridId="list-grid"
      [data]="rows()"
      [rowId]="rowId"
      [rowActivation]="mode()"
      [rowHeaders]="rowHeaders()"
      [rowClass]="rowClass()"
      [selectable]="selectable()"
      [(selectedIds)]="selection"
      [(activeRowId)]="active"
      [(orderBy)]="order"
      (rowActivate)="activations.push($event)"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="140" sortable>
        <a *tmGridDisplay="let value; rowId as id" class="link" [href]="'#/item/' + id">{{
          value
        }}</a>
      </tm-grid-column>
      <tm-grid-column key="qty" type="number" header="Qty" [width]="100" sortable />
      <tm-grid-column key="note" header="Note" [value]="noteOf" [flex]="1" />
    </tm-grid>
  `,
  // A consumer row class, styled from outside the grid's view.
  styles: `
    .paint-probe {
      --grid-active-row-bg: rgb(7, 8, 9);
      --surface-selected: rgb(10, 11, 12);
    }
    .test-tint {
      --tm-grid-row-bg: rgb(1, 2, 3);
      --tm-grid-row-ink: rgb(4, 5, 6);
    }
  `,
  encapsulation: ViewEncapsulation.None,
})
class ListHost {
  readonly grid = viewChild.required<TmGrid<Row>>('grid');
  readonly rows = signal<readonly Row[]>(makeRows(30));
  readonly mode = signal<'none' | 'row'>('row');
  readonly rowHeaders = signal(true);
  readonly selectable = signal(false);
  readonly rowClass = signal<((row: Row) => string | readonly string[] | null) | undefined>(
    undefined,
  );
  readonly selection = signal<ReadonlySet<string | number>>(new Set());
  readonly active = signal<TmRowId | null>(null);
  readonly order = signal<TmGridSort | null>(null);
  readonly activations: TmGridRowActivation<Row>[] = [];
  readonly rowId = (row: Row): number => row.id;
  readonly noteOf = (row: Row): string => `note ${row.id}`;
}

/** An editable grid with row activation on — the dev-mode misconfiguration. */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid gridId="list-editable" [field]="f" [rowId]="rowId" rowActivation="row">
      <tm-grid-column key="name" header="Name" />
    </tm-grid>
  `,
})
class EditableRowsHost {
  readonly model = signal<Row[]>([{ id: 1, name: 'Alpha', qty: 1 }]);
  readonly f = form(this.model);
  readonly rowId = (row: Row): number => row.id;
}

/** A sortable column without a key — the dev-mode misconfiguration. */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid gridId="list-unkeyed" [data]="rows" [rowId]="rowId">
      <tm-grid-column header="Computed" [value]="nameOf" sortable />
    </tm-grid>
  `,
})
class UnkeyedSortHost {
  readonly rows = makeRows(2);
  readonly rowId = (row: Row): number => row.id;
  readonly nameOf = (row: Row): string => row.name;
}

/** An editable grid whose readonly column renders links. */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridDisplayDef],
  template: `
    <tm-grid gridId="list-editable-link" [field]="f" [rowId]="rowId" style="block-size: 300px">
      <tm-grid-column key="name" header="Name" [width]="140" />
      <tm-grid-column key="qty" header="Item" [width]="140" readonly>
        <a *tmGridDisplay="let value; rowId as id" class="link" [href]="'#/item/' + id">{{
          value
        }}</a>
      </tm-grid-column>
    </tm-grid>
  `,
})
class EditableLinkHost {
  readonly model = signal<Row[]>([...makeRows(5)]);
  readonly f = form(this.model);
  readonly rowId = (row: Row): number => row.id;
}

/** Sortable columns whose headers are templates, with no header text. */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridHeaderDef],
  template: `
    <tm-grid gridId="list-template-header" [data]="rows" [rowId]="rowId" style="block-size: 200px">
      <tm-grid-column key="qty" type="number" [width]="100" sortable>
        <span *tmGridHeader>#</span>
      </tm-grid-column>
    </tm-grid>
  `,
})
class TemplateHeaderHost {
  readonly rows = makeRows(3);
  readonly rowId = (row: Row): number => row.id;
}

/** A template-headed sortable column without a key. */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridHeaderDef],
  template: `
    <tm-grid gridId="list-unkeyed-template" [data]="rows" [rowId]="rowId">
      <tm-grid-column [value]="nameOf" sortable><span *tmGridHeader>N</span></tm-grid-column>
    </tm-grid>
  `,
})
class UnkeyedTemplateSortHost {
  readonly rows = makeRows(2);
  readonly rowId = (row: Row): number => row.id;
  readonly nameOf = (row: Row): string => row.name;
}

// Section: helpers.

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

async function setup(configure?: (host: ListHost) => void): Promise<{
  fixture: ComponentFixture<ListHost>;
  host: ListHost;
  scroller: HTMLElement;
  input: HTMLInputElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(ListHost);
  configure?.(fixture.componentInstance);
  await stable(fixture);
  const root = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    host: fixture.componentInstance,
    scroller: root.querySelector('.tm-grid__scroller') as HTMLElement,
    input: root.querySelector('input.search') as HTMLInputElement,
  };
}

function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
  )!;
}

function headerAt(scroller: HTMLElement, col: number): HTMLElement {
  return scroller.querySelector<HTMLElement>(`[data-tm-colhdr][data-col="${col}"]`)!;
}

function rowAt(scroller: HTMLElement, viewIndex: number): HTMLElement {
  return scroller.querySelector<HTMLElement>(`.tm-grid__row[aria-rowindex="${viewIndex + 2}"]`)!;
}

function pointer(target: Element, type: string, init: PointerEventInit = {}): PointerEvent {
  const event = new PointerEvent(type, {
    bubbles: true,
    cancelable: true,
    button: 0,
    isPrimary: true,
    pointerType: 'mouse',
    ...init,
  });
  target.dispatchEvent(event);
  return event;
}

/** A mouse press and release on one element, the release `travel` px away. */
function press(target: Element, init: PointerEventInit = {}, travel = 0): PointerEvent {
  const rect = target.getBoundingClientRect();
  const at = { clientX: rect.left + 8, clientY: rect.top + 8 };
  const down = pointer(target, 'pointerdown', { ...at, ...init });
  if (travel > 0) {
    pointer(document.body, 'pointermove', {
      ...init,
      clientX: at.clientX + travel,
      clientY: at.clientY,
    });
  }
  pointer(target, 'pointerup', { ...init, clientX: at.clientX + travel, clientY: at.clientY });
  return down;
}

function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

/** The platform's Mod key as event modifiers. */
const mod: KeyboardEventInit & PointerEventInit = tmIsApplePlatform()
  ? { metaKey: true }
  : { ctrlKey: true };

// Section: row activation.

describe('tm-grid row activation', () => {
  it('opens a row on a press and release, moving the active cell on the press', async () => {
    const { fixture, host, scroller } = await setup();
    const cell = cellAt(scroller, 3, 1);
    pointer(cell, 'pointerdown');
    await stable(fixture);
    // The highlight moves on the press; the activation waits for the release.
    expect(cellAt(scroller, 3, 1).getAttribute('tabindex')).toBe('0');
    expect(rowAt(scroller, 3).classList).toContain('tm-grid__row--active');
    expect(host.activations).toEqual([]);
    pointer(cell, 'pointerup');
    await stable(fixture);
    expect(host.activations).toEqual([
      { row: host.rows()[3], rowId: 3, via: 'pointer', newTab: false },
    ]);
  });

  it('asks for a new tab with Mod held or the middle button', async () => {
    const { fixture, host, scroller } = await setup();
    press(cellAt(scroller, 1, 1), mod);
    await stable(fixture);
    expect(host.activations.at(-1)).toMatchObject({ rowId: 1, newTab: true, via: 'pointer' });

    const middle = pointer(cellAt(scroller, 2, 1), 'pointerdown', { button: 1, buttons: 4 });
    expect(middle.defaultPrevented).toBe(true); // no autoscroll
    await stable(fixture);
    expect(cellAt(scroller, 2, 1).getAttribute('tabindex')).toBe('0');
    cellAt(scroller, 2, 1).dispatchEvent(
      new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 }),
    );
    await stable(fixture);
    expect(host.activations.at(-1)).toMatchObject({ rowId: 2, newTab: true, via: 'pointer' });
  });

  it('never opens a row from a drag, Shift+click, a link, or the chrome cells', async () => {
    const { fixture, host, scroller } = await setup((h) => h.selectable.set(true));
    // A drag past the click slop is a range drag.
    press(cellAt(scroller, 1, 1), {}, 30);
    await stable(fixture);
    // Shift+click extends the range.
    press(cellAt(scroller, 4, 1), { shiftKey: true });
    await stable(fixture);
    expect(cellAt(scroller, 4, 1).getAttribute('aria-selected')).toBe('true');
    // A link in the cell keeps its native click.
    press(cellAt(scroller, 2, 0).querySelector('a.link')!);
    await stable(fixture);
    // The checkbox cell and the row header have their own behavior.
    press(scroller.querySelector('[data-tm-checkcell][data-row="5"]')!);
    press(scroller.querySelector('[data-tm-rowhdr][data-row="6"]')!);
    await stable(fixture);
    expect(host.activations).toEqual([]);
  });

  it('opens a row on a touch tap, but not on a tap on a link', async () => {
    const { fixture, host, scroller } = await setup();
    const tap = (target: Element): void => {
      target.dispatchEvent(
        new PointerEvent('click', { pointerType: 'touch', bubbles: true, cancelable: true }),
      );
    };
    tap(cellAt(scroller, 2, 0).querySelector('a.link')!);
    await stable(fixture);
    expect(host.activations).toEqual([]);
    tap(cellAt(scroller, 4, 1));
    await stable(fixture);
    expect(host.activations).toEqual([
      { row: host.rows()[4], rowId: 4, via: 'pointer', newTab: false },
    ]);
  });

  it('opens the active row on Enter, in a new tab on Mod+Enter', async () => {
    const { fixture, host, scroller } = await setup();
    press(cellAt(scroller, 2, 1));
    await stable(fixture);
    host.activations.length = 0;
    await new Promise((resolve) => setTimeout(resolve, 450)); // past the guard
    const enter = keydown(cellAt(scroller, 2, 1), 'Enter');
    expect(enter.defaultPrevented).toBe(true);
    await new Promise((resolve) => setTimeout(resolve, 450));
    keydown(cellAt(scroller, 2, 1), 'Enter', mod);
    await stable(fixture);
    expect(host.activations).toEqual([
      { row: host.rows()[2], rowId: 2, via: 'keyboard', newTab: false },
      { row: host.rows()[2], rowId: 2, via: 'keyboard', newTab: true },
    ]);
    // Enter opens the row instead of moving down.
    expect(cellAt(scroller, 2, 1).getAttribute('tabindex')).toBe('0');
  });

  it('ignores a second activation of the same row within 400 ms', async () => {
    const { fixture, host, scroller } = await setup();
    const now = vi.spyOn(Date, 'now').mockReturnValue(10_000);
    try {
      press(cellAt(scroller, 1, 1));
      now.mockReturnValue(10_300);
      press(cellAt(scroller, 1, 1)); // the double-click's second click
      now.mockReturnValue(10_350);
      press(cellAt(scroller, 2, 1)); // another row is not guarded
      now.mockReturnValue(10_500);
      press(cellAt(scroller, 1, 1)); // past the window again
      await stable(fixture);
    } finally {
      now.mockRestore();
    }
    expect(host.activations.map((a) => a.rowId)).toEqual([1, 2, 1]);
  });

  it("reaches a cell's link with F2, and leaves typed characters to the page", async () => {
    const { fixture, scroller } = await setup();
    press(cellAt(scroller, 3, 0));
    await stable(fixture);
    const link = cellAt(scroller, 3, 0).querySelector('a.link') as HTMLAnchorElement;
    const followed = vi.fn((event: Event) => event.preventDefault());
    link.addEventListener('click', followed);
    const f2 = keydown(cellAt(scroller, 3, 0), 'F2');
    expect(f2.defaultPrevented).toBe(true);
    expect(followed).toHaveBeenCalledTimes(1);
    for (const key of ['a', 'Z', '7']) {
      expect(keydown(cellAt(scroller, 3, 0), key).defaultPrevented, key).toBe(false);
    }
  });

  it('marks the active row, describes the scroller and shows pointer rows only in row mode', async () => {
    const { fixture, host, scroller } = await setup();
    press(cellAt(scroller, 1, 1));
    await stable(fixture);
    const hintId = scroller.getAttribute('aria-describedby')!;
    expect(document.getElementById(hintId)?.textContent).toBe(
      'Press Enter to open a row. Press F2 to reach a link in a cell.',
    );
    expect(rowAt(scroller, 1).classList).toContain('tm-grid__row--active');
    expect(getComputedStyle(cellAt(scroller, 1, 1)).cursor).toBe('pointer');

    host.mode.set('none');
    await stable(fixture);
    expect(scroller.hasAttribute('aria-describedby')).toBe(false);
    expect(rowAt(scroller, 1).classList).not.toContain('tm-grid__row--active');
    expect(getComputedStyle(cellAt(scroller, 1, 1)).cursor).toBe('default');
  });

  it("keeps every behavior of today with rowActivation 'none'", async () => {
    const { fixture, host, scroller } = await setup((h) => h.mode.set('none'));
    press(cellAt(scroller, 1, 1));
    await stable(fixture);
    // Enter moves down, as before; nothing opens.
    keydown(cellAt(scroller, 1, 1), 'Enter');
    await stable(fixture);
    expect(cellAt(scroller, 2, 1).getAttribute('tabindex')).toBe('0');
    expect(keydown(cellAt(scroller, 2, 1), 'Enter', mod).defaultPrevented).toBe(false);
    expect(keydown(cellAt(scroller, 2, 1), 'F2').defaultPrevented).toBe(false);
    expect(host.activations).toEqual([]);
  });

  it('opens no row when a pen press is held into the long-press menu', async () => {
    const { fixture, host, scroller } = await setup();
    const cell = cellAt(scroller, 3, 1);
    pointer(cell, 'pointerdown', { pointerType: 'pen', pointerId: 9 });
    await new Promise((resolve) => setTimeout(resolve, 650));
    await stable(fixture);
    expect(document.querySelector('.tm-menu__panel')).not.toBeNull();
    // The release ends a press that asked for the menu: nothing opens.
    pointer(cell, 'pointerup', { pointerType: 'pen', pointerId: 9 });
    await stable(fixture);
    expect(host.activations).toEqual([]);
  });

  it('opens the row once while Enter or Mod+Enter is held', async () => {
    const { fixture, host, scroller, input } = await setup();
    const now = vi.spyOn(Date, 'now').mockReturnValue(10_000);
    try {
      press(cellAt(scroller, 2, 1));
      await stable(fixture);
      host.activations.length = 0;
      const cell = cellAt(scroller, 2, 1);
      now.mockReturnValue(11_000);
      keydown(cell, 'Enter');
      // Repeats arrive past the 400 ms guard: each is consumed, none opens.
      now.mockReturnValue(11_600);
      expect(keydown(cell, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
      now.mockReturnValue(12_200);
      expect(keydown(cell, 'Enter', { ...mod, repeat: true }).defaultPrevented).toBe(true);
      // The same through the driver.
      now.mockReturnValue(13_000);
      expect(keydown(input, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
      now.mockReturnValue(14_000);
      expect(keydown(input, 'Enter').defaultPrevented).toBe(true);
      await stable(fixture);
    } finally {
      now.mockRestore();
    }
    expect(host.activations.map((a) => [a.rowId, a.via, a.newTab])).toEqual([
      [2, 'keyboard', false],
      [2, 'keyboard', false],
    ]);
  });

  it('adds a range on Mod+click while opening the row in a new tab', async () => {
    const { fixture, host, scroller } = await setup();
    press(cellAt(scroller, 1, 1));
    await stable(fixture);
    press(cellAt(scroller, 4, 2), mod);
    await stable(fixture);
    expect(cellAt(scroller, 1, 1).getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 4, 2).getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 2, 1).getAttribute('aria-selected')).not.toBe('true');
    expect(host.activations.at(-1)).toMatchObject({ rowId: 4, newTab: true, via: 'pointer' });
  });

  it('throws in dev mode when row activation meets an editable grid', () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(EditableRowsHost);
    expect(() => fixture.detectChanges()).toThrowError(/requires a readonly grid/);
  });
});

// Section: the browser's menu on links.

describe('tm-grid links keep the browser menu', () => {
  it.each(['none', 'row'] as const)(
    "leaves a right-click on a cell link alone, moving the active cell there (rowActivation '%s')",
    async (mode) => {
      const { fixture, scroller } = await setup((h) => h.mode.set(mode));
      const link = cellAt(scroller, 4, 0).querySelector('a.link')!;
      const rect = link.getBoundingClientRect();
      const menu = new MouseEvent('contextmenu', {
        bubbles: true,
        cancelable: true,
        clientX: rect.left + 2,
        clientY: rect.top + 2,
      });
      link.dispatchEvent(menu);
      await stable(fixture);
      expect(menu.defaultPrevented, mode).toBe(false);
      expect(document.querySelector('.tm-menu__panel'), mode).toBeNull();
      expect(cellAt(scroller, 4, 0).getAttribute('tabindex'), mode).toBe('0');

      // Elsewhere in a cell, the grid's menu opens as before.
      const plain = cellAt(scroller, 4, 1);
      const other = new MouseEvent('contextmenu', {
        bubbles: true,
        cancelable: true,
        clientX: plain.getBoundingClientRect().left + 2,
        clientY: plain.getBoundingClientRect().top + 2,
      });
      plain.dispatchEvent(other);
      await stable(fixture);
      expect(other.defaultPrevented, mode).toBe(true);
      expect(document.querySelector('.tm-menu__panel'), mode).not.toBeNull();
    },
  );

  it('settles an open editor before a right-click on a link moves the active cell', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(EditableLinkHost);
    await stable(fixture);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    press(cellAt(scroller, 0, 0));
    await stable(fixture);
    keydown(cellAt(scroller, 0, 0), 'F2');
    await stable(fixture);
    expect(scroller.querySelector('[data-tm-editor]')).not.toBeNull();

    const link = cellAt(scroller, 3, 1).querySelector('a.link')!;
    const rect = link.getBoundingClientRect();
    const menu = new MouseEvent('contextmenu', {
      bubbles: true,
      cancelable: true,
      clientX: rect.left + 2,
      clientY: rect.top + 2,
    });
    link.dispatchEvent(menu);
    await stable(fixture);
    expect(menu.defaultPrevented).toBe(false);
    expect(scroller.querySelector('[data-tm-editor]')).toBeNull();
    expect(cellAt(scroller, 3, 1).getAttribute('tabindex')).toBe('0');
  });

  it('starts no long-press timer on a cell link', async () => {
    const { fixture, scroller } = await setup();
    const link = cellAt(scroller, 4, 0).querySelector('a.link')!;
    const down = pointer(link, 'pointerdown', { pointerType: 'touch', pointerId: 7 });
    expect(down.defaultPrevented).toBe(false);
    await new Promise((resolve) => setTimeout(resolve, 650));
    await stable(fixture);
    expect(document.querySelector('.tm-menu__panel')).toBeNull();
    pointer(link, 'pointerup', { pointerType: 'touch', pointerId: 7 });

    // The same press elsewhere in the cell opens the grid's menu.
    const cell = cellAt(scroller, 4, 1);
    pointer(cell, 'pointerdown', { pointerType: 'touch', pointerId: 8 });
    await new Promise((resolve) => setTimeout(resolve, 650));
    await stable(fixture);
    expect(document.querySelector('.tm-menu__panel')).not.toBeNull();
    pointer(cell, 'pointerup', { pointerType: 'touch', pointerId: 8 });
  });
});

// Section: the external active row and the driver.

describe('tm-grid active row model and driver', () => {
  it('writes every change of the active row to activeRowId', async () => {
    const { fixture, host, scroller } = await setup();
    expect(host.active()).toBeNull();
    press(cellAt(scroller, 5, 1));
    await stable(fixture);
    expect(host.active()).toBe(5);
    keydown(cellAt(scroller, 5, 1), 'ArrowDown');
    await stable(fixture);
    expect(host.active()).toBe(6);
    // A reconcile: the rows above the active one disappear.
    host.rows.set(host.rows().slice(2));
    await stable(fixture);
    expect(host.active()).toBe(6);
    expect(cellAt(scroller, 4, 1).getAttribute('tabindex')).toBe('0');
  });

  it('moves the active cell on a consumer write without taking focus', async () => {
    const { fixture, host, scroller, input } = await setup();
    press(cellAt(scroller, 1, 1));
    await stable(fixture);
    input.focus();
    host.active.set(20);
    await stable(fixture);
    expect(cellAt(scroller, 20, 1).getAttribute('tabindex')).toBe('0'); // same column
    expect(document.activeElement).toBe(input);

    // An unknown id is ignored and written back with the actual row.
    host.active.set(999);
    await stable(fixture);
    expect(host.active()).toBe(20);
    expect(cellAt(scroller, 20, 1).getAttribute('tabindex')).toBe('0');
  });

  it('starts a consumer write with no active cell in the first column', async () => {
    const { fixture, host, scroller } = await setup((h) => h.active.set(7));
    expect(host.active()).toBe(7);
    expect(cellAt(scroller, 7, 0).getAttribute('tabindex')).toBe('0');
    // Emptied data: the model reads back null.
    host.rows.set([]);
    await stable(fixture);
    expect(host.active()).toBeNull();
  });

  it('drives the active row from a search field, which keeps focus', async () => {
    const { fixture, host, scroller, input } = await setup();
    input.focus();
    expect(input.getAttribute('aria-controls')).toBe(scroller.id);
    expect(input.hasAttribute('aria-activedescendant')).toBe(false);

    // Enter with no active row stays the input's own.
    expect(keydown(input, 'Enter').defaultPrevented).toBe(false);

    expect(keydown(input, 'ArrowDown').defaultPrevented).toBe(true);
    await stable(fixture);
    expect(host.active()).toBe(0);
    const descendant = input.getAttribute('aria-activedescendant')!;
    expect(document.getElementById(descendant)).toBe(rowAt(scroller, 0));

    keydown(input, 'ArrowDown');
    keydown(input, 'ArrowDown');
    keydown(input, 'ArrowUp');
    await stable(fixture);
    expect(host.active()).toBe(1);

    const page = host.grid().pageStep();
    expect(page).toBeGreaterThan(1);
    keydown(input, 'PageDown');
    await stable(fixture);
    expect(host.active()).toBe(1 + page);
    keydown(input, 'PageUp');
    keydown(input, 'PageUp');
    await stable(fixture);
    expect(host.active()).toBe(0); // clamped, never wraps
    keydown(input, 'ArrowUp');
    await stable(fixture);
    expect(host.active()).toBe(0);

    expect(keydown(input, 'Enter').defaultPrevented).toBe(true);
    await new Promise((resolve) => setTimeout(resolve, 450));
    keydown(input, 'Enter', mod);
    await stable(fixture);
    expect(host.activations).toEqual([
      { row: host.rows()[0], rowId: 0, via: 'keyboard', newTab: false },
      { row: host.rows()[0], rowId: 0, via: 'keyboard', newTab: true },
    ]);

    // Every other key stays with the input.
    for (const key of ['Home', 'End', 'ArrowLeft', 'ArrowRight', ' ', 'a']) {
      expect(keydown(input, key).defaultPrevented, key).toBe(false);
    }
    expect(keydown(input, 'ArrowDown', { shiftKey: true }).defaultPrevented).toBe(false);
    expect(keydown(input, 'ArrowDown', { altKey: true }).defaultPrevented).toBe(false);
    expect(keydown(input, 'ArrowDown', { isComposing: true }).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(input);
  });

  it('opens the row an arrow just reached, before the page renders again', async () => {
    const { fixture, host, input } = await setup();
    input.focus();
    // Keys queued behind a busy page arrive in one task, before the next
    // render writes the active-row model back: Enter still opens the row the
    // arrows reached.
    keydown(input, 'ArrowDown');
    keydown(input, 'ArrowDown');
    expect(keydown(input, 'Enter').defaultPrevented).toBe(true);
    keydown(input, 'ArrowDown');
    expect(keydown(input, 'Enter', mod).defaultPrevented).toBe(true);
    // A repeat on a row just reached is consumed too, and opens nothing more.
    expect(keydown(input, 'Enter', { repeat: true }).defaultPrevented).toBe(true);
    await stable(fixture);
    expect(host.activations.map((a) => [a.rowId, a.via, a.newTab])).toEqual([
      [1, 'keyboard', false],
      [2, 'keyboard', true],
    ]);
    expect(host.active()).toBe(2);
  });

  it('moves to the first and last rows and reports a missing row', async () => {
    const { fixture, host } = await setup();
    host.grid().moveActiveRow('last');
    await stable(fixture);
    expect(host.active()).toBe(29);
    host.grid().moveActiveRow('first');
    await stable(fixture);
    expect(host.active()).toBe(0);
    expect(host.grid().activateRow(12)).toBe(true);
    expect(host.grid().activateRow(999)).toBe(false);
    expect(host.activations.map((a) => [a.rowId, a.via])).toEqual([[12, 'keyboard']]);
  });
});

// Section: sort.

describe('tm-grid sortable headers', () => {
  it('cycles asc, desc and unsorted on clicks, with aria-sort and announcements', async () => {
    const { fixture, host, scroller } = await setup();
    const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
    const name = headerAt(scroller, 0);
    press(name);
    await stable(fixture);
    expect(host.order()).toEqual({ key: 'name', direction: 'asc' });
    expect(name.getAttribute('aria-sort')).toBe('ascending');
    expect(headerAt(scroller, 1).hasAttribute('aria-sort')).toBe(false);
    press(name);
    await stable(fixture);
    expect(host.order()).toEqual({ key: 'name', direction: 'desc' });
    expect(name.getAttribute('aria-sort')).toBe('descending');
    press(name);
    await stable(fixture);
    expect(host.order()).toBeNull();
    expect(name.hasAttribute('aria-sort')).toBe(false);
    expect(announce.mock.calls.map((call) => String(call[0]))).toEqual([
      'Sorted by Name, ascending',
      'Sorted by Name, descending',
      'Sort cleared',
    ]);

    // Another column starts ascending: one sorted column.
    press(name);
    press(headerAt(scroller, 1));
    await stable(fixture);
    expect(host.order()).toEqual({ key: 'qty', direction: 'asc' });
    expect(name.hasAttribute('aria-sort')).toBe(false);
  });

  it('leaves the rows, the selection and focus alone on a sort click', async () => {
    const { fixture, host, scroller } = await setup();
    press(cellAt(scroller, 2, 1));
    await stable(fixture);
    const order = host.rows();
    press(headerAt(scroller, 1));
    await stable(fixture);
    expect(host.rows()).toBe(order);
    expect(cellAt(scroller, 2, 1).getAttribute('tabindex')).toBe('0');
    expect(scroller.querySelectorAll('[data-tm-cell][aria-selected="true"]').length).toBe(1);
  });

  it('keeps column selection on drags, Shift+click and non-sortable headers', async () => {
    const { fixture, host, scroller } = await setup();
    // A drag from a sortable header selects columns and does not sort.
    const name = headerAt(scroller, 0);
    const from = { clientX: name.getBoundingClientRect().left + 8, clientY: 8 };
    pointer(name, 'pointerdown', from);
    pointer(document.body, 'pointermove', { clientX: from.clientX + 40, clientY: 8 });
    pointer(document.body, 'pointerup', { clientX: from.clientX + 40, clientY: 8 });
    await stable(fixture);
    expect(host.order()).toBeNull();
    expect(cellAt(scroller, 0, 0).getAttribute('aria-selected')).toBe('true');

    // Shift+click on a sortable header extends the column span.
    press(headerAt(scroller, 2));
    await stable(fixture);
    press(name, { shiftKey: true });
    await stable(fixture);
    expect(host.order()).toBeNull();
    expect(cellAt(scroller, 0, 0).getAttribute('aria-selected')).toBe('true');

    // A non-sortable header selects on the press, as before.
    pointer(headerAt(scroller, 2), 'pointerdown');
    await stable(fixture);
    expect(cellAt(scroller, 0, 2).getAttribute('aria-selected')).toBe('true');
    expect(headerAt(scroller, 2).querySelector('.tm-grid__sort')).toBeNull();
  });

  it('renders the decorative indicator slot whether or not the column is sorted', async () => {
    const { fixture, host, scroller } = await setup();
    const slot = headerAt(scroller, 0).querySelector('.tm-grid__sort') as HTMLElement;
    expect(slot.getAttribute('aria-hidden')).toBe('true');
    // Unsorted: only the hover preview of ascending.
    expect(slot.querySelectorAll('tm-icon').length).toBe(1);
    expect(slot.querySelector('.tm-grid__sort-glyph--next')).not.toBeNull();
    const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
    host.order.set({ key: 'name', direction: 'desc' });
    await stable(fixture);
    // A consumer write shows without a word: only grid gestures announce.
    expect(announce).not.toHaveBeenCalled();
    expect(headerAt(scroller, 0).querySelector('.tm-grid__sort')).toBe(slot);
    expect(headerAt(scroller, 0).getAttribute('aria-sort')).toBe('descending');
    // Sorted descending: the current arrow; a click clears, so no preview.
    expect(slot.querySelectorAll('tm-icon').length).toBe(1);
    expect(slot.querySelector('.tm-grid__sort-glyph--next')).toBeNull();
  });

  it('names a template-headed column by its key in the announcement', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(TemplateHeaderHost);
    await stable(fixture);
    const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    press(headerAt(scroller, 0));
    await stable(fixture);
    expect(announce.mock.calls.map((call) => String(call[0]))).toEqual([
      'Sorted by qty, ascending',
    ]);
  });

  it('names a template-headed unkeyed sortable column by position in the dev error', () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(UnkeyedTemplateSortHost);
    expect(() => fixture.detectChanges()).toThrowError(/sortable column "#1" has no key/);
  });

  it('throws in dev mode when a sortable column has no key', () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(UnkeyedSortHost);
    expect(() => fixture.detectChanges()).toThrowError(/has no key/);
  });
});

// Section: select-all across pages.

describe('tm-grid select-all across pages', () => {
  it('adds and removes only the bound rows, keeping other pages checked', async () => {
    const { fixture, host, scroller } = await setup((h) => {
      h.selectable.set(true);
      h.rows.set(makeRows(5));
      h.selection.set(new Set([100, 101]));
    });
    const selectAll = scroller.querySelector('[data-tm-checkall]') as HTMLElement;
    // The tri-state counts the bound rows only.
    expect(selectAll.getAttribute('aria-checked')).toBe('false');
    selectAll.click();
    await stable(fixture);
    expect([...host.selection()].sort((a, b) => Number(a) - Number(b))).toEqual([
      0, 1, 2, 3, 4, 100, 101,
    ]);
    expect(selectAll.getAttribute('aria-checked')).toBe('true');
    // From all, the bound rows are unchecked; the other pages stay.
    selectAll.click();
    await stable(fixture);
    expect([...host.selection()].sort((a, b) => Number(a) - Number(b))).toEqual([100, 101]);
    expect(selectAll.getAttribute('aria-checked')).toBe('false');
  });

  it('announces the count here and in all when other pages are checked', async () => {
    const { fixture, host, scroller } = await setup((h) => {
      h.selectable.set(true);
      h.rows.set(makeRows(5));
    });
    const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
    (scroller.querySelector('[data-tm-checkcell][data-row="1"]') as HTMLElement).click();
    await new Promise((resolve) => setTimeout(resolve, 200));
    host.rows.set(makeRows(5, 5)); // the next page
    await stable(fixture);
    (scroller.querySelector('[data-tm-checkcell][data-row="0"]') as HTMLElement).click();
    await new Promise((resolve) => setTimeout(resolve, 200));
    expect(announce.mock.calls.map((call) => String(call[0]))).toEqual([
      '1 of 5 selected',
      '1 of 5 selected here, 2 in all',
    ]);
  });
});

// Section: row classes and the row-number column.

describe('tm-grid rowClass and rowHeaders', () => {
  it('puts the classes on the row element and paints through the custom properties', async () => {
    const { fixture, host, scroller } = await setup((h) => {
      h.mode.set('none');
      h.rowClass.set((row) =>
        row.id === 1
          ? 'tm-grid__row--muted'
          : row.id === 2
            ? ['tm-grid__row--emphasis', 'extra']
            : row.id === 4 || row.id === 5
              ? 'test-tint'
              : null,
      );
    });
    expect(rowAt(scroller, 1).classList).toContain('tm-grid__row--muted');
    expect(rowAt(scroller, 2).classList).toContain('tm-grid__row--emphasis');
    expect(rowAt(scroller, 2).classList).toContain('extra');
    expect(rowAt(scroller, 2).classList).toContain('tm-grid__row'); // the grid's own class stays
    const style = (row: number): CSSStyleDeclaration => getComputedStyle(cellAt(scroller, row, 1));
    // A consumer class paints through the custom properties — over the
    // zebra stripe on the odd row too.
    for (const row of [4, 5]) {
      expect(style(row).backgroundColor, String(row)).toBe('rgb(1, 2, 3)');
      expect(style(row).color, String(row)).toBe('rgb(4, 5, 6)');
    }
    host.rowClass.set(undefined);
    await stable(fixture);
    expect(rowAt(scroller, 1).classList).not.toContain('tm-grid__row--muted');
  });

  it('paints the active and checked tints over a row class', async () => {
    const { fixture, host, scroller } = await setup((h) => {
      h.selectable.set(true);
      h.rowClass.set((row) => (row.id === 4 || row.id === 5 ? 'test-tint' : null));
    });
    press(cellAt(scroller, 4, 1));
    host.selection.set(new Set([5]));
    await stable(fixture);
    const background = (row: number): string =>
      getComputedStyle(cellAt(scroller, row, 2)).backgroundColor;
    expect(background(4)).toBe('rgb(7, 8, 9)'); // active over the class
    expect(background(5)).toBe('rgb(10, 11, 12)'); // checked over the class
  });

  it('drops the row-number column and the corner, shifting the column indexes', async () => {
    const { scroller } = await setup((h) => {
      h.rowHeaders.set(false);
      h.selectable.set(true);
    });
    expect(scroller.querySelector('[data-tm-corner]')).toBeNull();
    expect(scroller.querySelector('[data-tm-rowhdr]')).toBeNull();
    expect(scroller.getAttribute('aria-colcount')).toBe('4'); // 3 data + checkbox
    expect(scroller.querySelector('[data-tm-checkhdr]')?.getAttribute('aria-colindex')).toBe('1');
    expect(scroller.querySelector('[data-tm-checkcell]')?.getAttribute('aria-colindex')).toBe('1');
    expect(headerAt(scroller, 0).getAttribute('aria-colindex')).toBe('2');
    expect(cellAt(scroller, 0, 0).getAttribute('aria-colindex')).toBe('2');
    // No row-header track: the checkbox column's is the first.
    expect(
      (scroller.closest('tm-grid') as HTMLElement).style.getPropertyValue('--grid-template'),
    ).toMatch(/^var\(--grid-check-col-width\) /);
  });
});

// Section: the harness.

describe('TmGridHarness list-page members', () => {
  it('activates rows, reads the active row, and sorts by key', async () => {
    const { fixture, host } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    expect(await harness.getActiveRowIndex()).toBeNull();
    expect(await harness.getSort()).toBeNull();

    await harness.activateRow(4);
    expect(host.activations.map((a) => a.rowId)).toEqual([4]);
    expect(await harness.getActiveRowIndex()).toBe(4);

    await harness.sortBy('qty', 'desc');
    expect(await harness.getSort()).toEqual({ key: 'qty', direction: 'desc' });
    expect(host.order()).toEqual({ key: 'qty', direction: 'desc' });
    await harness.sortBy('name');
    expect(await harness.getSort()).toEqual({ key: 'name', direction: 'asc' });
  });
});
