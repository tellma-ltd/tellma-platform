// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';
import { TmGridDisplayDef, TmGridRowEndDef } from './tm-grid-templates';

/**
 * Row-element reuse under scroll: the grid re-binds the elements of the rows
 * a scroll takes out to the rows it brings in, and every rendered row must
 * stay true to its own row, its focus semantics and its consumer views.
 */

interface Row {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
  readonly active: boolean;
}

function makeRows(count: number): readonly Row[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i,
    name: `Item ${i}`,
    qty: 1000 + i,
    active: i % 3 === 0,
  }));
}

/** Fallback row height (no tokens CSS loads in the unit-test bundle). */
const ROW_HEIGHT = 32;

@Component({
  imports: [TmGrid, TmGridColumn, TmGridDisplayDef, TmGridRowEndDef],
  template: `
    <tm-grid gridId="spec-grid-reuse" [data]="rows()" [rowId]="rowId" style="block-size: 300px">
      <tm-grid-column key="name" header="Name" [width]="120" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="100" />
      <tm-grid-column key="active" type="boolean" header="Active" [width]="80" />
      <tm-grid-column key="id" header="Link" [width]="100">
        <span *tmGridDisplay="let value; rowId as id" class="spec-link" [attr.data-id]="id">{{
          value
        }}</span>
      </tm-grid-column>
      <ng-template tmGridRowEnd let-id="rowId">
        <span class="spec-end" [attr.data-id]="id">{{ id }}</span>
      </ng-template>
    </tm-grid>
  `,
})
class ReuseHost {
  readonly rows = signal<readonly Row[]>(makeRows(500));
  readonly rowId = (row: Row): number => row.id;
}

/** A readonly selectable grid of the same rows, for the row checkboxes. */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid
      gridId="spec-grid-reuse-select"
      [data]="rows()"
      [rowId]="rowId"
      selectable
      [(selectedIds)]="selection"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="120" />
    </tm-grid>
  `,
})
class SelectReuseHost {
  readonly rows = signal<readonly Row[]>(makeRows(500));
  readonly selection = signal<ReadonlySet<string | number>>(new Set());
  readonly rowId = (row: Row): number => row.id;
}

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

async function setup(): Promise<{ fixture: ComponentFixture<ReuseHost>; scroller: HTMLElement }> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(ReuseHost);
  await stable(fixture);
  const scroller = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-grid__scroller',
  ) as HTMLElement;
  return { fixture, scroller };
}

async function setupSelect(): Promise<{
  fixture: ComponentFixture<SelectReuseHost>;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(SelectReuseHost);
  await stable(fixture);
  const scroller = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-grid__scroller',
  ) as HTMLElement;
  return { fixture, scroller };
}

/** A mouse press on an element, as the browser dispatches it. */
function press(target: HTMLElement): void {
  target.dispatchEvent(
    new PointerEvent('pointerdown', {
      bubbles: true,
      cancelable: true,
      button: 0,
      pointerType: 'mouse',
      pointerId: 1,
    }),
  );
}

/** The click that ends a mouse press on an element. */
function release(target: HTMLElement, init: MouseEventInit = {}): void {
  target.dispatchEvent(
    new MouseEvent('click', { bubbles: true, cancelable: true, detail: 1, ...init }),
  );
}

async function scrollTo(
  fixture: ComponentFixture<unknown>,
  scroller: HTMLElement,
  top: number,
): Promise<void> {
  scroller.scrollTop = top;
  scroller.dispatchEvent(new Event('scroll'));
  await stable(fixture);
}

function keydown(target: HTMLElement, key: string, init: KeyboardEventInit = {}): void {
  target.dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }),
  );
}

function rowElements(scroller: HTMLElement): HTMLElement[] {
  return [...scroller.querySelectorAll<HTMLElement>('.tm-grid__window > .tm-grid__row')];
}

function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
  );
}

/** The view index each rendered row element shows, read from its row header. */
function viewIndexOf(row: HTMLElement): number {
  return Number(row.querySelector('[data-tm-rowhdr]')!.getAttribute('data-row'));
}

/** Asserts every rendered row element shows its own row, attribute by attribute. */
function expectRowsTrue(scroller: HTMLElement): void {
  for (const row of rowElements(scroller)) {
    const index = viewIndexOf(row);
    expect(row.getAttribute('data-tm-key')).toBe(`#${index}`);
    expect(row.getAttribute('aria-rowindex')).toBe(String(index + 2));
    expect(row.id.endsWith(`-r${index}`)).toBe(true);
    expect(row.querySelector('[data-tm-rowhdr]')!.textContent!.trim()).toBe(String(index + 1));
    const cells = [...row.querySelectorAll<HTMLElement>('[data-tm-cell]')];
    expect(cells.map((cell) => cell.getAttribute('data-row'))).toEqual(
      cells.map(() => String(index)),
    );
    expect(cells[0].textContent!.trim()).toBe(`Item ${index}`);
    expect(cells[1].textContent!.trim()).toBe((1000 + index).toLocaleString('en'));
    expect(cells[2].querySelector('.tm-grid-bool--on') !== null).toBe(index % 3 === 0);
    expect(cells[3].querySelector('.spec-link')!.getAttribute('data-id')).toBe(String(index));
    expect(row.querySelector('.spec-end')!.getAttribute('data-id')).toBe(String(index));
  }
}

describe('tm-grid (reused rows)', () => {
  it('re-binds the elements of the rows a scroll takes out, creating no new ones', async () => {
    const { fixture, scroller } = await setup();
    // Away from the origin, where the window has its overscan on both sides.
    await scrollTo(fixture, scroller, 20 * ROW_HEIGHT);
    const before = new Set(rowElements(scroller));
    for (const top of [22, 31, 150, 151, 23]) {
      await scrollTo(fixture, scroller, top * ROW_HEIGHT);
      const after = rowElements(scroller);
      expect(after).toHaveLength(before.size);
      expect(after.every((row) => before.has(row))).toBe(true);
    }
  });

  it('keeps every rendered row true to its own row through a sweep of scrolls and jumps', async () => {
    const { fixture, scroller } = await setup();
    for (const top of [3, 9, 10, 40, 37, 300, 491, 0, 250]) {
      await scrollTo(fixture, scroller, top * ROW_HEIGHT);
      expect(viewIndexOf(rowElements(scroller)[4])).toBe(Math.max(0, top - 4) + 4);
      expectRowsTrue(scroller);
    }
  });

  it('moves only the row elements a scroll re-binds, scrolling down or back up', async () => {
    const { fixture, scroller } = await setup();
    await scrollTo(fixture, scroller, 100 * ROW_HEIGHT);
    // Counts the row elements taken out of the window to be put back elsewhere.
    let moved = 0;
    const count = (records: MutationRecord[]): void => {
      for (const record of records) {
        for (const node of record.removedNodes) {
          moved += node instanceof Element && node.classList.contains('tm-grid__row') ? 1 : 0;
        }
      }
    };
    const observer = new MutationObserver(count);
    observer.observe(scroller.querySelector('.tm-grid__window')!, { childList: true });
    // Each scroll of three rows re-binds three: the rows that stay never move.
    for (const top of [103, 106, 103, 100, 97, 100]) {
      moved = 0;
      await scrollTo(fixture, scroller, top * ROW_HEIGHT);
      count(observer.takeRecords());
      expect(moved).toBeLessThanOrEqual(3);
      expect(rowElements(scroller).map(viewIndexOf)[0]).toBe(top - 4);
    }
    observer.disconnect();
  });

  it('renders a scroll’s own frame from the rows it has, and the overscan after it', async () => {
    const { fixture, scroller } = await setup();
    await scrollTo(fixture, scroller, 20 * ROW_HEIGHT);
    const before = new Set(rowElements(scroller));
    expect(rowElements(scroller).map(viewIndexOf)[0]).toBe(16);
    // The scroll's own frame: the window moves only as far as rows 40-48
    // (the visible ones) need, keeping its length.
    scroller.scrollTop = 40 * ROW_HEIGHT;
    scroller.dispatchEvent(new Event('scroll'));
    fixture.detectChanges();
    const sliced = rowElements(scroller);
    expect(sliced).toHaveLength(before.size);
    expect(sliced.map(viewIndexOf)[0]).toBe(49 - before.size);
    expect(sliced.every((row) => before.has(row))).toBe(true);
    expectRowsTrue(scroller);
    // The next task brings the overscan on both sides.
    await stable(fixture);
    const settled = rowElements(scroller);
    expect(settled).toHaveLength(before.size);
    expect(settled.map(viewIndexOf)[0]).toBe(36);
    expect(settled.every((row) => before.has(row))).toBe(true);
    expectRowsTrue(scroller);
  });

  it('keeps the active row on its element as it scrolls out of the window and back', async () => {
    const { fixture, scroller } = await setup();
    scroller.focus();
    keydown(scroller, 'ArrowDown'); // the first arrow activates the origin
    await stable(fixture);
    const active = cellAt(scroller, 0, 0)!;
    expect(document.activeElement).toBe(active);
    for (const top of [50, 300, 0]) {
      await scrollTo(fixture, scroller, top * ROW_HEIGHT);
      expect(cellAt(scroller, 0, 0)).toBe(active);
      expect(document.activeElement).toBe(active);
    }
  });

  it('lands focus on a fresh element when the active cell moves onto a row the move brings in', async () => {
    const { fixture, scroller } = await setup();
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    keydown(document.activeElement as HTMLElement, 'ArrowDown', { ctrlKey: true });
    await stable(fixture);
    const last = cellAt(scroller, 499, 0)!;
    expect(document.activeElement).toBe(last);
    // The jump back to the first row replaces the whole window: the last
    // row leaves as the first enters, and focus must land on the first
    // row's own element with a focusin, never stay on a re-bound one.
    const focusins: EventTarget[] = [];
    scroller.addEventListener('focusin', (event) => focusins.push(event.target!));
    keydown(last, 'ArrowUp', { ctrlKey: true });
    await stable(fixture);
    const origin = cellAt(scroller, 0, 0)!;
    expect(document.activeElement).toBe(origin);
    expect(origin).not.toBe(last);
    expect(focusins).toContain(origin);
  });

  it('gives a re-bound row fresh consumer views, never another row’s', async () => {
    const { fixture, scroller } = await setup();
    await scrollTo(fixture, scroller, 20 * ROW_HEIGHT);
    const rows = new Set(rowElements(scroller));
    const views = new Set(scroller.querySelectorAll('.spec-link, .spec-end'));
    await scrollTo(fixture, scroller, 200 * ROW_HEIGHT);
    // Every row element is reused…
    expect(rowElements(scroller).every((row) => rows.has(row))).toBe(true);
    // …but none of the consumer views, which belong to the rows that left.
    const after = [...scroller.querySelectorAll('.spec-link, .spec-end')];
    expect(after.length).toBe(views.size);
    expect(after.some((view) => views.has(view))).toBe(false);
    expectRowsTrue(scroller);
    // A row that stays keeps its views.
    const staying = [...scroller.querySelectorAll('.spec-link')].at(-1)!;
    await scrollTo(fixture, scroller, 201 * ROW_HEIGHT);
    expect(scroller.contains(staying)).toBe(true);
  });
  it('keeps a replaced active row off its element, so focus moves with a focusin', async () => {
    const { fixture, scroller } = await setup();
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    const origin = cellAt(scroller, 0, 0)!;
    expect(document.activeElement).toBe(origin);
    const focusins: EventTarget[] = [];
    scroller.addEventListener('focusin', (event) => focusins.push(event.target!));
    // The active row's temporary id gives way to its saved id: only that
    // row leaves and only its replacement enters, which would take the
    // focused element in place were it not kept out of reuse.
    fixture.componentInstance.rows.update((rows) => [
      { ...rows[0], id: 100_000 },
      ...rows.slice(1),
    ]);
    await stable(fixture);
    expect(cellAt(scroller, 0, 0)!.closest('.tm-grid__row')!.getAttribute('data-tm-key')).toBe(
      '#100000',
    );
    expect(origin.isConnected).toBe(false);
    // Focus follows the active cell onto an element of its own.
    const active = scroller.querySelector<HTMLElement>('.tm-grid__cell--active')!;
    expect(document.activeElement).toBe(active);
    expect(active).not.toBe(origin);
    expect(focusins).toContain(active);
  });

  it('ignores a checkbox click whose press was on the row its element showed before a scroll', async () => {
    const { fixture, scroller } = await setupSelect();
    await scrollTo(fixture, scroller, 20 * ROW_HEIGHT);
    const checkCell = (row: number): HTMLElement =>
      scroller.querySelector<HTMLElement>(`[data-tm-checkcell][data-row="${row}"]`)!;
    // The window's first row leaves on a scroll of three rows, and its
    // element re-binds to a row that enters; a plain click and a Shift
    // click (a range from the anchor) alike.
    for (const [top, init] of [
      [23, {}],
      [26, { shiftKey: true }],
    ] as const) {
      const first = viewIndexOf(rowElements(scroller)[0]);
      const pressed = checkCell(first);
      press(pressed);
      await scrollTo(fixture, scroller, top * ROW_HEIGHT);
      expect(pressed.isConnected).toBe(true);
      expect(pressed.getAttribute('data-row')).not.toBe(String(first));
      release(pressed, init);
      await stable(fixture);
      expect([...fixture.componentInstance.selection()]).toEqual([]);
    }
    // A press and click on one row still toggles it.
    const row = viewIndexOf(rowElements(scroller)[6]);
    press(checkCell(row));
    release(checkCell(row));
    await stable(fixture);
    expect([...fixture.componentInstance.selection()]).toEqual([row]);
  });
});
