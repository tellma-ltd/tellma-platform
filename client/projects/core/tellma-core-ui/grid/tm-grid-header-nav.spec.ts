// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Dir } from '@angular/cdk/bidi';
import { Component, signal, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import type { TmRowId } from '@tellma/core-ui/contracts';
import { provideTellmaUi } from '@tellma/core-ui';
import { TmGridHarness } from '@tellma/core-ui-testing';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';
import { TmGridHeaderAction } from './tm-grid-header-action';
import {
  TmGridHeaderDef,
  TmGridHeaderEndDef,
  TmGridRowEndDef,
  TmGridRowEndHeaderDef,
} from './tm-grid-templates';
import type { TmGridRowActivation, TmGridSort } from './tm-grid-types';

// Header navigation with header actions, and the row-end column: the keys
// that reach the header row and leave it, the single tab stop across both,
// and a sticky inline-end column that stays outside selection, copy and
// find.

interface Row {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
}

function makeRows(count: number): readonly Row[] {
  return Array.from({ length: count }, (_, i) => ({ id: i, name: `Item ${i}`, qty: 100 + i }));
}

/**
 * A list with every header kind: Name sortable, Qty sortable with an action,
 * Note action-only, and (by switch) a row-end header and row-end cells.
 */
@Component({
  imports: [
    Dir,
    TmGrid,
    TmGridColumn,
    TmGridHeaderAction,
    TmGridHeaderEndDef,
    TmGridRowEndDef,
    TmGridRowEndHeaderDef,
  ],
  // The tokens the header action and the row-end cell paint with, and the
  // chrome tracks' widths, which a unit test's page does not load.
  template: `
    <div
      [dir]="dir()"
      style="
        --grid-header-action-size: 24px;
        --surface-page: rgb(255, 255, 255);
        --grid-active-row-bg: rgb(7, 8, 9);
        --grid-selection-bg: rgba(1, 2, 3, 0.5);
        --grid-row-header-width: 40px;
        --grid-check-col-width: 32px;
        --grid-row-end-width: 36px;
      "
    >
      <tm-grid
        #grid
        gridId="header-nav-grid"
        [data]="rows()"
        [rowId]="rowId"
        [rowActivation]="mode()"
        [selectable]="selectable()"
        [(selectedIds)]="selection"
        [(orderBy)]="order"
        searchable
        (rowActivate)="activations.push($event)"
        style="block-size: 300px; inline-size: 600px"
      >
        <tm-grid-column key="name" header="Name" [width]="140" sortable />
        <tm-grid-column key="qty" type="number" header="Qty" [width]="120" sortable>
          <ng-template tmGridHeaderEnd let-label let-key="key" let-sort="sort">
            <button
              tmGridHeaderAction
              class="qty-action"
              [attr.aria-label]="'Filter ' + label"
              [attr.data-context]="key + ' ' + sort"
              [active]="qtyActive()"
              (click)="qtyClicks.set(qtyClicks() + 1)"
            >
              F
            </button>
          </ng-template>
        </tm-grid-column>
        <tm-grid-column key="note" header="Note" [value]="noteOf" [width]="140">
          <ng-template tmGridHeaderEnd>
            <button
              tmGridHeaderAction
              class="note-action"
              aria-label="Filter Note"
              (click)="noteClicks.set(noteClicks() + 1)"
            >
              F
            </button>
          </ng-template>
        </tm-grid-column>
        @if (endHeader()) {
          <ng-template tmGridRowEndHeader>
            <button
              tmGridHeaderAction
              class="end-action"
              aria-label="Choose columns"
              (click)="endClicks.set(endClicks() + 1)"
            >
              C
            </button>
          </ng-template>
        }
        @if (endCell()) {
          <ng-template tmGridRowEnd let-id="rowId">
            <button type="button" class="end-button" (click)="opened.push(id)">
              Open {{ id }}
            </button>
          </ng-template>
        }
      </tm-grid>
    </div>
  `,
})
class NavHost {
  readonly grid = viewChild.required<TmGrid<Row>>('grid');
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly rows = signal<readonly Row[]>(makeRows(20));
  readonly mode = signal<'none' | 'row'>('none');
  readonly selectable = signal(true);
  readonly endHeader = signal(true);
  readonly endCell = signal(true);
  readonly qtyActive = signal(false);
  readonly selection = signal<ReadonlySet<string | number>>(new Set());
  readonly order = signal<TmGridSort | null>(null);
  readonly qtyClicks = signal(0);
  readonly noteClicks = signal(0);
  readonly endClicks = signal(0);
  readonly opened: TmRowId[] = [];
  readonly activations: TmGridRowActivation<Row>[] = [];
  readonly rowId = (row: Row): number => row.id;
  readonly noteOf = (row: Row): string => `note ${row.id}`;
}

/** A grid with neither sortable columns nor header actions: today's headers. */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridHeaderDef],
  template: `
    <tm-grid gridId="header-plain-grid" [data]="rows" [rowId]="rowId" style="block-size: 200px">
      <tm-grid-column key="name" [width]="140">
        <button *tmGridHeader type="button" class="header-button">Name</button>
      </tm-grid-column>
      <tm-grid-column key="qty" header="Qty" [width]="100" />
    </tm-grid>
  `,
})
class PlainHost {
  readonly rows = makeRows(3);
  readonly rowId = (row: Row): number => row.id;
}

/**
 * A navigating header row (one sortable column) whose other headers carry
 * interactive content of their own rather than a header action.
 */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridHeaderDef],
  template: `
    <tm-grid
      gridId="header-content-grid"
      [data]="rows"
      [rowId]="rowId"
      style="block-size: 200px; inline-size: 600px"
    >
      <tm-grid-column key="name" header="Name" [width]="140" sortable />
      <tm-grid-column key="qty" [width]="140">
        <button
          *tmGridHeader
          type="button"
          class="header-button"
          (click)="clicks.set(clicks() + 1)"
        >
          Qty
        </button>
      </tm-grid-column>
      <tm-grid-column key="id" [width]="140">
        <input *tmGridHeader class="header-input" aria-label="Filter id" />
      </tm-grid-column>
      <tm-grid-column key="unit" [value]="blank" [width]="100">
        <div
          *tmGridHeader
          class="header-widget"
          role="combobox"
          tabindex="0"
          aria-label="Unit"
          (click)="widgetClicks.set(widgetClicks() + 1)"
        >
          kg
        </div>
      </tm-grid-column>
      <tm-grid-column key="label" [value]="blank" [width]="100">
        <span *tmGridHeader class="header-text" contenteditable="true">Label</span>
      </tm-grid-column>
    </tm-grid>
  `,
})
class ContentHost {
  readonly rows = makeRows(3);
  readonly clicks = signal(0);
  readonly widgetClicks = signal(0);
  readonly blank = (): string => '';
  readonly rowId = (row: Row): number => row.id;
}

// Section: helpers.

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

async function setup(configure?: (host: NavHost) => void): Promise<{
  fixture: ComponentFixture<NavHost>;
  host: NavHost;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(NavHost);
  configure?.(fixture.componentInstance);
  await stable(fixture);
  const root = fixture.nativeElement as HTMLElement;
  return {
    fixture,
    host: fixture.componentInstance,
    scroller: root.querySelector('.tm-grid__scroller') as HTMLElement,
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

function endCellAt(scroller: HTMLElement, row: number): HTMLElement {
  return scroller.querySelector<HTMLElement>(`[data-tm-endcell][data-row="${row}"]`)!;
}

function endHeader(scroller: HTMLElement): HTMLElement {
  return scroller.querySelector<HTMLElement>('[data-tm-endhdr]')!;
}

function selectAll(scroller: HTMLElement): HTMLElement {
  return scroller.querySelector<HTMLElement>('[data-tm-checkall]')!;
}

function pointer(target: Element, type: string, init: PointerEventInit = {}): PointerEvent {
  const rect = target.getBoundingClientRect();
  const event = new PointerEvent(type, {
    bubbles: true,
    cancelable: true,
    button: 0,
    isPrimary: true,
    pointerType: 'mouse',
    clientX: rect.left + 4,
    clientY: rect.top + 4,
    ...init,
  });
  target.dispatchEvent(event);
  return event;
}

/** A mouse press and release on one element. */
function press(target: Element, init: PointerEventInit = {}): void {
  pointer(target, 'pointerdown', init);
  pointer(target, 'pointerup', init);
}

function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

/** Sends a key to whatever holds focus, then lets the grid settle. */
async function key(
  fixture: ComponentFixture<unknown>,
  name: string,
  init: KeyboardEventInit = {},
): Promise<KeyboardEvent> {
  const event = keydown(document.activeElement!, name, init);
  await stable(fixture);
  return event;
}

/** Focuses a data cell the way a press does, and settles. */
async function activate(
  fixture: ComponentFixture<unknown>,
  scroller: HTMLElement,
  row: number,
  col: number,
): Promise<void> {
  press(cellAt(scroller, row, col));
  await stable(fixture);
  cellAt(scroller, row, col).focus();
  await stable(fixture);
}

/** A right-click on an element, at its top inline-start corner. */
function contextMenu(target: Element): MouseEvent {
  const rect = target.getBoundingClientRect();
  const event = new MouseEvent('contextmenu', {
    bubbles: true,
    cancelable: true,
    clientX: rect.left + 2,
    clientY: rect.top + 2,
  });
  target.dispatchEvent(event);
  return event;
}

/** A touch press held on an element until the long-press menu opens, then released. */
async function longPress(fixture: ComponentFixture<unknown>, target: Element): Promise<void> {
  pointer(target, 'pointerdown', { pointerType: 'touch', pointerId: 7 });
  await new Promise((resolve) => setTimeout(resolve, 650));
  await stable(fixture);
  pointer(target, 'pointerup', { pointerType: 'touch', pointerId: 7 });
  await stable(fixture);
}

/** Whether the grid's context menu is open. */
function menuOpen(): boolean {
  return document.querySelector('.tm-menu__panel') !== null;
}

/** Whether the open context menu's item with this label is disabled. */
function menuItemDisabled(label: string): boolean {
  const labels = document.querySelectorAll<HTMLElement>('.tm-menu__panel .tm-menu__label');
  const match = [...labels].find((element) => element.textContent?.trim() === label);
  return match?.closest('.tm-menu__item')?.getAttribute('aria-disabled') === 'true';
}

function dispatchClipboard(scroller: HTMLElement, kind: 'copy' | 'cut'): DataTransfer {
  const data = new DataTransfer();
  scroller.dispatchEvent(
    new ClipboardEvent(kind, { clipboardData: data, bubbles: true, cancelable: true }),
  );
  return data;
}

/** The platform's Mod key as event modifiers. */
const mod: KeyboardEventInit = tmIsApplePlatform() ? { metaKey: true } : { ctrlKey: true };

// Section: header navigation.

describe('tm-grid header navigation', () => {
  it('keeps headers as they are without sortable columns or header actions', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(PlainHost);
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;
    const headers = root.querySelectorAll('[data-tm-colhdr]');
    for (const header of headers) {
      expect(header.hasAttribute('tabindex')).toBe(false);
      expect(header.hasAttribute('aria-describedby')).toBe(false);
    }
    // Projected header content stays in the tab order; there is no row-end column.
    expect(root.querySelector('.header-button')?.hasAttribute('tabindex')).toBe(false);
    expect(root.querySelector('[data-tm-endhdr]')).toBeNull();
    expect(root.querySelector('.tm-grid__scroller')?.getAttribute('aria-colcount')).toBe('3');
    // ArrowUp on the first row stays in the rows.
    const scroller = root.querySelector('.tm-grid__scroller') as HTMLElement;
    await activate(fixture, scroller, 0, 1);
    await key(fixture, 'ArrowUp');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 1));
  });

  it('gives navigable headers a roving tabindex and the hint naming their keys', async () => {
    const { scroller } = await setup();
    const hint = (header: HTMLElement): string =>
      document.getElementById(header.getAttribute('aria-describedby')!)?.textContent?.trim() ?? '';
    expect(headerAt(scroller, 0).getAttribute('tabindex')).toBe('-1');
    expect(hint(headerAt(scroller, 0))).toBe('Enter to sort.');
    expect(hint(headerAt(scroller, 1))).toMatch(/^Enter to sort\. .+ to filter\.$/);
    expect(hint(headerAt(scroller, 2))).toBe('Press Enter to open.');
    expect(hint(endHeader(scroller))).toBe('Press Enter to open.');
    // Header actions leave the tab order; the header slots carry the stops.
    for (const action of scroller.querySelectorAll('.tm-grid-header-action')) {
      expect(action.getAttribute('tabindex')).toBe('-1');
      expect(action.getAttribute('type')).toBe('button');
    }
    // The header-end template sees the column's label, key and sort.
    expect(scroller.querySelector('.qty-action')?.getAttribute('data-context')).toBe('qty null');
  });

  it('enters the header from the first row on ArrowUp and returns on ArrowDown', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 1, 1);
    await key(fixture, 'ArrowUp');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 1));

    await key(fixture, 'ArrowUp');
    expect(document.activeElement).toBe(headerAt(scroller, 1));
    expect(headerAt(scroller, 1).getAttribute('tabindex')).toBe('0');
    // One tab stop: the rows give theirs up while a header holds it.
    expect(scroller.querySelectorAll('[tabindex="0"]').length).toBe(1);
    expect(scroller.getAttribute('tabindex')).toBe('-1');

    // ArrowUp has nowhere to go; the key is consumed.
    expect((await key(fixture, 'ArrowUp')).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(headerAt(scroller, 1));

    await key(fixture, 'ArrowDown');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 1));
    expect(cellAt(scroller, 0, 1).getAttribute('tabindex')).toBe('0');
    expect(host.order()).toBeNull();
  });

  it('moves between header cells with the arrows, Home and End, clamped and direction-mapped', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 0);
    await key(fixture, 'ArrowUp');
    expect(document.activeElement).toBe(headerAt(scroller, 0));

    await key(fixture, 'ArrowRight');
    expect(document.activeElement).toBe(headerAt(scroller, 1));
    await key(fixture, 'End');
    expect(document.activeElement).toBe(endHeader(scroller));
    await key(fixture, 'ArrowRight');
    expect(document.activeElement).toBe(endHeader(scroller));
    await key(fixture, 'Home');
    expect(document.activeElement).toBe(selectAll(scroller));
    await key(fixture, 'ArrowLeft');
    expect(document.activeElement).toBe(selectAll(scroller));

    // Right-to-left: ArrowLeft moves toward the inline end.
    host.dir.set('rtl');
    await stable(fixture);
    await key(fixture, 'ArrowLeft');
    expect(document.activeElement).toBe(headerAt(scroller, 0));
    await key(fixture, 'ArrowRight');
    expect(document.activeElement).toBe(selectAll(scroller));
  });

  it('sorts on Enter and Space, toggles select-all, and opens an action-only header', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 0);
    await key(fixture, 'ArrowUp');

    expect((await key(fixture, 'Enter')).defaultPrevented).toBe(true);
    expect(host.order()).toEqual({ key: 'name', direction: 'asc' });
    await key(fixture, ' ');
    expect(host.order()).toEqual({ key: 'name', direction: 'desc' });
    expect(document.activeElement).toBe(headerAt(scroller, 0));

    // Enter on the action-only header clicks its action.
    await key(fixture, 'ArrowRight');
    await key(fixture, 'ArrowRight');
    await key(fixture, 'Enter');
    expect(host.noteClicks()).toBe(1);
    expect(host.order()).toEqual({ key: 'name', direction: 'desc' });

    // And on the row-end header.
    await key(fixture, 'End');
    await key(fixture, 'Enter');
    expect(host.endClicks()).toBe(1);

    // Space and Enter toggle select-all on its header.
    await key(fixture, 'Home');
    await key(fixture, ' ');
    expect(host.selection().size).toBe(20);
    await key(fixture, 'Enter');
    expect(host.selection().size).toBe(0);
  });

  it('clicks the header action on Alt+ArrowDown, while Enter on that header still sorts', async () => {
    const { fixture, host, scroller } = await setup();
    await activate(fixture, scroller, 0, 1);
    await key(fixture, 'ArrowUp');
    expect((await key(fixture, 'ArrowDown', { altKey: true })).defaultPrevented).toBe(true);
    expect(host.qtyClicks()).toBe(1);
    expect(document.activeElement).toBe(headerAt(scroller, 1));
    await key(fixture, 'Enter');
    expect(host.qtyClicks()).toBe(1);
    expect(host.order()).toEqual({ key: 'qty', direction: 'asc' });
    expect(scroller.querySelector('.qty-action')?.getAttribute('data-context')).toBe('qty asc');

    // Alt+ArrowDown on a header without an action does nothing.
    await key(fixture, 'ArrowLeft');
    expect((await key(fixture, 'ArrowDown', { altKey: true })).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(headerAt(scroller, 0));
  });

  it('reaches the first content of a header with neither a sort nor an action', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(ContentHost);
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;
    const scroller = root.querySelector<HTMLElement>('.tm-grid__scroller')!;
    const button = root.querySelector<HTMLElement>('.header-button')!;
    // Out of the tab order: the header row is the keyboard route to it.
    expect(button.getAttribute('tabindex')).toBe('-1');

    await activate(fixture, scroller, 0, 1);
    await key(fixture, 'ArrowUp');
    expect(document.activeElement).toBe(headerAt(scroller, 1));
    expect((await key(fixture, 'Enter')).defaultPrevented).toBe(true);
    expect(fixture.componentInstance.clicks()).toBe(1);
    expect(document.activeElement).toBe(headerAt(scroller, 1));

    // A form field takes focus, and keeps its keys but Escape, which returns to its header.
    await key(fixture, 'ArrowRight');
    await key(fixture, 'Enter');
    const input = root.querySelector<HTMLElement>('.header-input')!;
    expect(document.activeElement).toBe(input);
    expect((await key(fixture, 'ArrowLeft')).defaultPrevented).toBe(false);
    expect((await key(fixture, 'Enter')).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(input);
    expect((await key(fixture, 'Escape')).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(headerAt(scroller, 2));

    // Other focusable content (a select's trigger) takes focus without a click.
    await key(fixture, 'ArrowRight');
    await key(fixture, 'Enter');
    expect(document.activeElement).toBe(root.querySelector('.header-widget'));
    expect(fixture.componentInstance.widgetClicks()).toBe(0);

    // The arrows go on from it, and editable text takes focus too, keeping
    // its keys like a form field.
    await key(fixture, 'ArrowRight');
    expect(document.activeElement).toBe(headerAt(scroller, 4));
    await key(fixture, 'Enter');
    const text = root.querySelector<HTMLElement>('.header-text')!;
    expect(document.activeElement).toBe(text);
    expect((await key(fixture, 'ArrowLeft')).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(text);
  });

  it('leaves an Escape that ends an IME composition in a header field', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(ContentHost);
    await stable(fixture);
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector<HTMLElement>('.header-input')!;
    input.focus();
    await stable(fixture);

    expect((await key(fixture, 'Escape', { isComposing: true })).defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(input);
    // The first keydown of a composition carries only the 229 sentinel.
    const first = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    Object.defineProperty(first, 'keyCode', { value: 229 });
    input.dispatchEvent(first);
    await stable(fixture);
    expect(first.defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(input);

    // Once the composition ends, Escape returns to the header.
    const scroller = root.querySelector<HTMLElement>('.tm-grid__scroller')!;
    expect((await key(fixture, 'Escape')).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(headerAt(scroller, 2));
  });

  it("counts focus on a header's action as that header's focus", async () => {
    const { fixture, host, scroller } = await setup();
    const action = scroller.querySelector<HTMLElement>('.note-action')!;
    action.focus();
    await stable(fixture);
    expect(document.activeElement).toBe(action);
    expect(headerAt(scroller, 2).getAttribute('tabindex')).toBe('0');

    // Enter on the action is the action's own: the grid leaves it alone.
    expect((await key(fixture, 'Enter')).defaultPrevented).toBe(false);
    expect(host.noteClicks()).toBe(0);

    // The arrows keep navigating the header row from the action.
    await key(fixture, 'ArrowLeft');
    expect(document.activeElement).toBe(headerAt(scroller, 1));
  });

  it('reaches the first header from the container of a grid without rows', async () => {
    const { fixture, scroller } = await setup((host) => {
      host.rows.set([]);
      host.selectable.set(false);
    });
    expect(headerAt(scroller, 0).getAttribute('tabindex')).toBe('0');
    expect(scroller.getAttribute('tabindex')).toBe('-1');
    headerAt(scroller, 0).focus();
    await stable(fixture);
    await key(fixture, 'ArrowDown');
    expect(document.activeElement).toBe(headerAt(scroller, 0));
  });

  it('leaves the header row on Escape, parking focus on the container', async () => {
    const { fixture, scroller } = await setup();
    await activate(fixture, scroller, 0, 0);
    await key(fixture, 'ArrowUp');
    await key(fixture, 'Escape');
    expect(document.activeElement).toBe(scroller);
    expect(scroller.querySelectorAll('[data-tm-hslot][tabindex="0"]').length).toBe(0);
  });
});

// Section: header actions.

describe('TmGridHeaderAction', () => {
  it('marks its active state with a class and a hidden description', async () => {
    const { fixture, host, scroller } = await setup();
    const action = scroller.querySelector<HTMLElement>('.qty-action')!;
    expect(action.classList).not.toContain('tm-grid-header-action--active');
    expect(action.hasAttribute('aria-describedby')).toBe(false);
    const before = action.getBoundingClientRect();

    host.qtyActive.set(true);
    await stable(fixture);
    expect(action.classList).toContain('tm-grid-header-action--active');
    const description = document.getElementById(action.getAttribute('aria-describedby')!);
    expect(description?.textContent).toBe('Active');
    // The slot is reserved: the active state moves nothing.
    const after = action.getBoundingClientRect();
    expect([after.left, after.width, after.height]).toEqual([
      before.left,
      before.width,
      before.height,
    ]);
    expect(before.width).toBe(24);
    expect(before.height).toBe(24);

    host.qtyActive.set(false);
    await stable(fixture);
    expect(action.hasAttribute('aria-describedby')).toBe(false);
  });

  it('describes the header holding it as active, where header navigation puts focus', async () => {
    const { fixture, host, scroller } = await setup();
    const texts = (element: HTMLElement): string[] =>
      (element.getAttribute('aria-describedby') ?? '')
        .split(' ')
        .map((id) => document.getElementById(id)?.textContent?.trim() ?? '');
    expect(texts(headerAt(scroller, 1))).not.toContain('Active');

    host.qtyActive.set(true);
    await stable(fixture);
    const header = texts(headerAt(scroller, 1));
    expect(header.length).toBe(2);
    expect(header[0]).not.toBe('');
    expect(header[1]).toBe('Active');
    // Only the header holding the active action.
    expect(texts(headerAt(scroller, 2))).not.toContain('Active');
    expect(texts(endHeader(scroller))).not.toContain('Active');

    host.qtyActive.set(false);
    await stable(fixture);
    expect(texts(headerAt(scroller, 1))).toEqual([header[0]]);
  });

  it('opens on a press without sorting or selecting the column', async () => {
    const { fixture, host, scroller } = await setup();
    const action = scroller.querySelector<HTMLElement>('.qty-action')!;
    press(action);
    action.click();
    await stable(fixture);
    expect(host.qtyClicks()).toBe(1);
    expect(host.order()).toBeNull();
    expect(scroller.querySelector('[data-tm-cell][aria-selected="true"]')).toBeNull();
  });
});

// Section: the row-end column.

describe('tm-grid row-end column', () => {
  it('renders after the data columns as the last ARIA column', async () => {
    const { fixture, host, scroller } = await setup();
    // The checkbox, row-number and three data columns, then the row-end column.
    expect(scroller.getAttribute('aria-colcount')).toBe('6');
    expect(endHeader(scroller).getAttribute('aria-colindex')).toBe('6');
    expect(endCellAt(scroller, 0).getAttribute('aria-colindex')).toBe('6');
    expect(endCellAt(scroller, 0).getAttribute('role')).toBe('gridcell');
    expect(endCellAt(scroller, 0).textContent?.trim()).toBe('Open 0');
    // No resize handle, label or sort slot.
    expect(endHeader(scroller).querySelector('[data-tm-resize], .tm-grid__sort')).toBeNull();

    // Only an end-cell template: an empty, hidden header that no key reaches.
    host.endHeader.set(false);
    await stable(fixture);
    expect(endHeader(scroller).getAttribute('aria-hidden')).toBe('true');
    expect(endHeader(scroller).hasAttribute('data-tm-hslot')).toBe(false);
    expect(endHeader(scroller).hasAttribute('tabindex')).toBe(false);

    // Neither template: no column.
    host.endCell.set(false);
    await stable(fixture);
    expect(scroller.querySelector('[data-tm-endhdr], [data-tm-endcell]')).toBeNull();
    expect(scroller.getAttribute('aria-colcount')).toBe('5');
  });

  it('reaches the row-end cell with End and the inline-end arrow, and leaves it', async () => {
    const { fixture, scroller, host } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    await activate(fixture, scroller, 0, 0);
    await key(fixture, 'End');
    expect(document.activeElement).toBe(endCellAt(scroller, 0));
    expect(endCellAt(scroller, 0).getAttribute('tabindex')).toBe('0');
    expect(endCellAt(scroller, 0).classList).toContain('tm-grid__endcell--active');
    expect(cellAt(scroller, 0, 2).getAttribute('tabindex')).toBe('-1');
    expect(await harness.isRowEndActive()).toBe(true);
    expect(await harness.getActiveCell()).toBeNull();
    expect(await harness.getActiveRowIndex()).toBe(0);

    // Vertical moves keep the column.
    await key(fixture, 'ArrowDown');
    expect(document.activeElement).toBe(endCellAt(scroller, 1));

    // The inline-start arrow returns to the last data column; the end arrow comes back.
    await key(fixture, 'ArrowLeft');
    expect(document.activeElement).toBe(cellAt(scroller, 1, 2));
    await key(fixture, 'ArrowRight');
    expect(document.activeElement).toBe(endCellAt(scroller, 1));

    // Enter clicks the cell's button.
    await key(fixture, 'Enter');
    expect(host.opened).toEqual([1]);

    // ArrowUp on the first row enters the row-end header.
    await key(fixture, 'ArrowUp');
    await key(fixture, 'ArrowUp');
    expect(document.activeElement).toBe(endHeader(scroller));
    await key(fixture, 'ArrowDown');
    expect(document.activeElement).toBe(endCellAt(scroller, 0));

    // Home leaves for the first data column.
    await key(fixture, 'Home');
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));
  });

  it('reaches the row-end cell with the inline-end arrow in a right-to-left grid', async () => {
    const { fixture, host, scroller } = await setup();
    host.dir.set('rtl');
    await stable(fixture);
    await activate(fixture, scroller, 1, 2);
    await key(fixture, 'ArrowRight');
    expect(document.activeElement).toBe(cellAt(scroller, 1, 1));
    await key(fixture, 'ArrowLeft');
    expect(document.activeElement).toBe(cellAt(scroller, 1, 2));
    await key(fixture, 'ArrowLeft');
    expect(document.activeElement).toBe(endCellAt(scroller, 1));
    // The inline-end arrow stays; the inline-start arrow returns.
    await key(fixture, 'ArrowLeft');
    expect(document.activeElement).toBe(endCellAt(scroller, 1));
    await key(fixture, 'ArrowRight');
    expect(document.activeElement).toBe(cellAt(scroller, 1, 2));
  });

  it('takes a right-click or a long-press as its row, holding the ring with a template', async () => {
    const { fixture, scroller } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    await activate(fixture, scroller, 0, 0);

    expect(contextMenu(endCellAt(scroller, 3)).defaultPrevented).toBe(true);
    await stable(fixture);
    expect(menuOpen()).toBe(true);
    expect(await harness.isRowEndActive()).toBe(true);
    expect(await harness.getActiveRowIndex()).toBe(3);
    keydown(document.activeElement!, 'Escape');
    await stable(fixture);
    expect(menuOpen()).toBe(false);

    // A right-click on the active position's data cell moves the ring there.
    contextMenu(cellAt(scroller, 3, 2));
    await stable(fixture);
    expect(await harness.isRowEndActive()).toBe(false);
    expect(await harness.getActiveCell()).toEqual({ row: 3, col: 2 });
    keydown(document.activeElement!, 'Escape');
    await stable(fixture);

    await longPress(fixture, endCellAt(scroller, 5));
    expect(menuOpen()).toBe(true);
    expect(await harness.isRowEndActive()).toBe(true);
    expect(await harness.getActiveRowIndex()).toBe(5);
  });

  it("takes a right-click or a long-press as its row's last data cell without a template", async () => {
    const { fixture, scroller } = await setup((h) => h.endCell.set(false));
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    await activate(fixture, scroller, 0, 0);

    contextMenu(endCellAt(scroller, 3));
    await stable(fixture);
    expect(menuOpen()).toBe(true);
    expect(await harness.isRowEndActive()).toBe(false);
    expect(await harness.getActiveCell()).toEqual({ row: 3, col: 2 });
    keydown(document.activeElement!, 'Escape');
    await stable(fixture);

    await longPress(fixture, endCellAt(scroller, 5));
    expect(menuOpen()).toBe(true);
    expect(await harness.isRowEndActive()).toBe(false);
    expect(await harness.getActiveCell()).toEqual({ row: 5, col: 2 });
  });

  it('paints the range fill while its whole row is selected', async () => {
    const { fixture, scroller } = await setup();
    const fill = 'rgba(1, 2, 3, 0.5)';
    await activate(fixture, scroller, 2, 0);
    press(cellAt(scroller, 2, 1), { shiftKey: true });
    await stable(fixture);
    // Part of the row: no fill.
    expect(endCellAt(scroller, 2).classList).not.toContain('tm-grid__endcell--selected');
    expect(getComputedStyle(endCellAt(scroller, 2)).backgroundImage).not.toContain(fill);

    press(cellAt(scroller, 2, 2), { shiftKey: true });
    await stable(fixture);
    expect(endCellAt(scroller, 2).classList).toContain('tm-grid__endcell--selected');
    expect(getComputedStyle(endCellAt(scroller, 2)).backgroundImage).toContain(fill);
    expect(endCellAt(scroller, 4).classList).not.toContain('tm-grid__endcell--selected');
    // Paint only: the cell stays outside the selection.
    expect(endCellAt(scroller, 2).hasAttribute('aria-selected')).toBe(false);
  });

  it('stays outside selection and column selection', async () => {
    const { fixture, scroller } = await setup();
    await activate(fixture, scroller, 0, 2);
    await key(fixture, 'a', mod);
    expect(cellAt(scroller, 3, 0).getAttribute('aria-selected')).toBe('true');
    expect(scroller.querySelector('[data-tm-endcell][aria-selected]')).toBeNull();

    // A press on the row-end header selects no column and leaves the active cell.
    await activate(fixture, scroller, 2, 1);
    const down = pointer(endHeader(scroller), 'pointerdown');
    pointer(endHeader(scroller), 'pointerup');
    await stable(fixture);
    expect(down.defaultPrevented).toBe(true);
    expect(cellAt(scroller, 0, 1).getAttribute('aria-selected')).toBeNull();
    expect(cellAt(scroller, 2, 1).getAttribute('tabindex')).toBe('0');

    // A column selection from the row-end cell takes nothing.
    await activate(fixture, scroller, 2, 2);
    await key(fixture, 'End');
    await key(fixture, ' ', { ctrlKey: true });
    expect(cellAt(scroller, 0, 2).getAttribute('aria-selected')).toBeNull();
  });

  it('is never copied or cut', async () => {
    const { fixture, scroller } = await setup();
    await activate(fixture, scroller, 0, 0);
    expect(dispatchClipboard(scroller, 'copy').getData('text/plain')).toBe('Item 0\r\n');

    await key(fixture, 'End');
    expect(dispatchClipboard(scroller, 'copy').getData('text/plain')).toBe('');
    expect(dispatchClipboard(scroller, 'cut').getData('text/plain')).toBe('');
  });

  it('never takes a paste', async () => {
    const { fixture, scroller } = await setup();
    const paste = (): ClipboardEvent => {
      const data = new DataTransfer();
      data.setData('text/plain', 'Pasted');
      const event = new ClipboardEvent('paste', {
        clipboardData: data,
        bubbles: true,
        cancelable: true,
      });
      scroller.dispatchEvent(event);
      return event;
    };
    await activate(fixture, scroller, 0, 2);
    expect(paste().defaultPrevented).toBe(true);

    // On the row-end cell the paste is left to the page.
    await key(fixture, 'End');
    expect(paste().defaultPrevented).toBe(false);
  });

  it('hands copy and cut to a selection grown from the row-end cell by Mod+A or Shift+Space', async () => {
    const { fixture, scroller } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    await activate(fixture, scroller, 0, 0);
    await key(fixture, 'End');
    await key(fixture, 'a', mod);
    expect(await harness.isRowEndActive()).toBe(false);
    expect(document.activeElement).toBe(cellAt(scroller, 0, 2));
    const all = dispatchClipboard(scroller, 'copy').getData('text/plain');
    expect(all.split('\r\n').filter((line) => line !== '').length).toBe(20);
    // The Note column's value is computed, so it copies empty.
    expect(all.startsWith('Item 0\t100\t\r\n')).toBe(true);

    await activate(fixture, scroller, 1, 0);
    await key(fixture, 'End');
    await key(fixture, ' ', { shiftKey: true });
    expect(await harness.isRowEndActive()).toBe(false);
    expect(dispatchClipboard(scroller, 'copy').getData('text/plain')).toBe('Item 1\t101\t\r\n');
    expect(dispatchClipboard(scroller, 'cut').getData('text/plain')).toBe('Item 1\t101\t\r\n');
  });

  it('hands copy to the range a drag from the row-end cell selects', async () => {
    const { fixture, scroller } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    await activate(fixture, scroller, 4, 0);
    pointer(endCellAt(scroller, 0), 'pointerdown');
    // Within the row-end cell the press keeps the ring.
    pointer(document.body, 'pointermove', {
      clientX: endCellAt(scroller, 0).getBoundingClientRect().left + 6,
      clientY: endCellAt(scroller, 0).getBoundingClientRect().top + 4,
    });
    await stable(fixture);
    expect(await harness.isRowEndActive()).toBe(true);

    const target = cellAt(scroller, 2, 1).getBoundingClientRect();
    pointer(document.body, 'pointermove', {
      clientX: target.left + 4,
      clientY: target.top + 4,
    });
    pointer(cellAt(scroller, 2, 1), 'pointerup');
    await stable(fixture);
    expect(await harness.isRowEndActive()).toBe(false);
    expect(dispatchClipboard(scroller, 'copy').getData('text/plain')).toBe(
      '100\t\r\n101\t\r\n102\t\r\n',
    );
  });

  it("disables the context menu's clipboard items exactly where the keys do nothing", async () => {
    const { fixture, scroller } = await setup();
    await activate(fixture, scroller, 0, 0);
    contextMenu(endCellAt(scroller, 2));
    await stable(fixture);
    expect(menuOpen()).toBe(true);
    expect(menuItemDisabled('Copy')).toBe(true);
    expect(menuItemDisabled('Copy with headers')).toBe(true);
    keydown(document.activeElement!, 'Escape');
    await stable(fixture);

    // A grown selection returns the ring to the data cell, and the items act on it.
    await key(fixture, ' ', { shiftKey: true });
    contextMenu(cellAt(scroller, 2, 1));
    await stable(fixture);
    expect(menuOpen()).toBe(true);
    expect(menuItemDisabled('Copy')).toBe(false);
    expect(menuItemDisabled('Copy with headers')).toBe(false);
  });

  it('is never a find match, and a match leaves the row-end cell', async () => {
    const { fixture, scroller } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    await activate(fixture, scroller, 0, 0);
    await key(fixture, 'End');
    await key(fixture, 'f', mod);
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector<HTMLInputElement>('[data-tm-find-input]')!;
    input.value = 'Open 3';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 400));
    await stable(fixture);
    expect(root.querySelector('[data-tm-find-counter]')?.textContent?.trim()).toBe('No matches');

    input.value = 'Item 3';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 400));
    await stable(fixture);
    keydown(input, 'Enter');
    await stable(fixture);
    expect(scroller.querySelector('[data-tm-endcell].tm-grid__cell--find')).toBeNull();
    expect(await harness.isRowEndActive()).toBe(false);
    expect(await harness.getActiveCell()).toEqual({ row: 3, col: 0 });
  });

  it('activates its row from an empty row-end cell', async () => {
    const { fixture, host, scroller } = await setup((h) => {
      h.mode.set('row');
      h.endCell.set(false);
    });
    const end = endCellAt(scroller, 4);
    expect(end.getAttribute('aria-hidden')).toBe('true');
    expect(end.hasAttribute('role')).toBe(false);
    press(end);
    await stable(fixture);
    expect(host.activations).toEqual([
      { row: host.rows()[4], rowId: 4, via: 'pointer', newTab: false },
    ]);
  });

  it('paints the active row band on its cell', async () => {
    const { fixture, host, scroller } = await setup((h) => h.mode.set('row'));
    expect(getComputedStyle(endCellAt(scroller, 2)).backgroundImage).not.toContain('rgb(7, 8, 9)');
    host.grid().activeRowId.set(2);
    await stable(fixture);
    expect(getComputedStyle(endCellAt(scroller, 2)).backgroundImage).toContain('rgb(7, 8, 9)');
    // Over the opaque page surface, so nothing scrolled under it shows through.
    expect(getComputedStyle(endCellAt(scroller, 2)).backgroundColor).toBe('rgb(255, 255, 255)');
  });
});

// Section: harness.

describe('TmGridHarness header and row-end members', () => {
  it('opens header actions and reads the focused header', async () => {
    const { fixture, host, scroller } = await setup();
    const harness = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmGridHarness);
    await harness.openHeaderAction('qty');
    expect(host.qtyClicks()).toBe(1);
    await harness.openRowEndAction();
    expect(host.endClicks()).toBe(1);

    expect(await harness.getFocusedHeader()).toBeNull();
    await activate(fixture, scroller, 0, 1);
    await key(fixture, 'ArrowUp');
    expect(await harness.getFocusedHeader()).toBe('qty');
    await harness.pressKeys(TestKey.END);
    expect(await harness.getFocusedHeader()).toBe('rowEnd');
    await key(fixture, 'Home');
    expect(await harness.getFocusedHeader()).toBe('selectAll');
    expect(await harness.getColCount()).toBe(6);
  });
});
