// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Dir } from '@angular/cdk/bidi';
import { Component, effect, input, signal, untracked, viewChild } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmGridHarness } from '@tellma/core-ui-testing';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';

interface Row {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
}

function makeRows(count: number): readonly Row[] {
  return Array.from({ length: count }, (_, i) => ({ id: i, name: `Item ${i}`, qty: 1000 + i }));
}

/** Fallback row height (no tokens CSS loads in the unit-test bundle). */
const ROW_HEIGHT = 32;

/** A 200-row grid beside a button that holds focus outside it. */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <button type="button" class="outside">Outside</button>
    <tm-grid
      gridId="spec-grid-scroll-to-row"
      [data]="rows()"
      [rowId]="rowId"
      style="block-size: 300px; --grid-flash-duration: 400ms"
    >
      <tm-grid-column key="name" header="Name" [width]="120" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="120" />
    </tm-grid>
  `,
})
class Host {
  readonly grid = viewChild.required(TmGrid<Row>);
  readonly rows = signal<readonly Row[]>(makeRows(200));
  readonly rowId = (row: Row): number => row.id;
}

/**
 * Reveals row 20 from an effect on the grid's rows, as a deep link does,
 * once they hold any. It sits after the grid, so its effect runs once the
 * grid's `data` holds the rows (an effect in the grid's host runs before
 * the host's bindings update, and finds no row).
 */
@Component({ selector: 'tm-spec-deep-link', template: '' })
class DeepLink {
  readonly grid = input.required<TmGrid<Row>>();
  readonly rows = input.required<readonly Row[]>();
  /** What `scrollToRow` returned once the rows arrived. */
  result: boolean | null = null;

  constructor() {
    effect(() => {
      if (this.rows().length > 0 && this.result === null) {
        this.result = untracked(() => this.grid().scrollToRow(20));
      }
    });
  }
}

/** The same grid remounted with its rows fetched later, and a deep link to row 20. */
@Component({
  imports: [DeepLink, TmGrid, TmGridColumn],
  template: `
    <tm-grid
      #grid
      gridId="spec-grid-scroll-to-row"
      [data]="rows()"
      [rowId]="rowId"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="120" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="120" />
    </tm-grid>
    <tm-spec-deep-link [grid]="grid" [rows]="rows()" />
  `,
})
class DeepLinkHost {
  readonly link = viewChild.required(DeepLink);
  readonly rows = signal<readonly Row[]>([]);
  readonly rowId = (row: Row): number => row.id;
}

/**
 * Twenty 150px columns in a 400px-wide grid, in either direction, with
 * the row-header track's token, which a unit test's page does not load.
 */
@Component({
  imports: [Dir, TmGrid, TmGridColumn],
  template: `
    <div [dir]="dir()" style="inline-size: 400px; --grid-row-header-width: 40px">
      <tm-grid
        gridId="spec-grid-scroll-wide"
        [data]="rows"
        [rowId]="rowId"
        style="block-size: 300px"
      >
        @for (key of keys; track key) {
          <tm-grid-column [key]="key" [header]="key" [width]="150" />
        }
      </tm-grid>
    </div>
  `,
})
class WideHost {
  readonly grid = viewChild.required(TmGrid<Row>);
  readonly dir = signal<'ltr' | 'rtl'>('ltr');
  readonly keys = Array.from({ length: 20 }, (_, i) => `c${i}`);
  readonly rows = makeRows(200);
  readonly rowId = (row: Row): number => row.id;
}

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

async function setup(): Promise<{
  fixture: ComponentFixture<Host>;
  harness: TmGridHarness;
  grid: TmGrid<Row>;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await stable(fixture);
  const harness = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmGridHarness);
  const scroller = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-grid__scroller',
  ) as HTMLElement;
  return { fixture, harness, grid: fixture.componentInstance.grid(), scroller };
}

/** The view row whose top edge sits at the body viewport's top. */
function firstVisibleRow(scroller: HTMLElement): number {
  return Math.round(scroller.scrollTop / ROW_HEIGHT);
}

/** Whether an element's box lies inside the scroller's box. */
function insideScroller(scroller: HTMLElement, element: Element): boolean {
  const outer = scroller.getBoundingClientRect();
  const inner = element.getBoundingClientRect();
  return (
    inner.left >= outer.left - 1 &&
    inner.right <= outer.right + 1 &&
    inner.top >= outer.top - 1 &&
    inner.bottom <= outer.bottom + 1
  );
}

describe('tm-grid scrollToRow', () => {
  it('returns false for a row that is not in the data, and changes nothing', async () => {
    const { harness, grid, scroller } = await setup();
    expect(grid.scrollToRow(999)).toBe(false);
    expect(await harness.getActiveCell()).toBeNull();
    expect(scroller.scrollTop).toBe(0);
  });

  it('centres the row by default and activates it in the current column', async () => {
    const { fixture, harness, grid, scroller } = await setup();
    await harness.clickCell(0, 1);
    expect(grid.scrollToRow(120)).toBe(true);
    await stable(fixture);

    expect(await harness.getActiveCell()).toEqual({ row: 120, col: 1 });
    // The body viewport is the scroller minus the sticky header row.
    const body = scroller.clientHeight - ROW_HEIGHT;
    const expected = Math.round(120 * ROW_HEIGHT - (body - ROW_HEIGHT) / 2);
    expect(Math.abs(scroller.scrollTop - expected)).toBeLessThanOrEqual(1);
  });

  it("align 'nearest' scrolls just far enough", async () => {
    const { fixture, grid, scroller } = await setup();
    grid.scrollToRow(120, { align: 'nearest' });
    await stable(fixture);
    const body = scroller.clientHeight - ROW_HEIGHT;
    expect(scroller.scrollTop).toBe(121 * ROW_HEIGHT - body);
  });

  it('lands in the named column, and keeps the current one for an unknown key', async () => {
    const { fixture, harness, grid } = await setup();
    grid.scrollToRow(10, { column: 'qty' });
    await stable(fixture);
    expect(await harness.getActiveCell()).toEqual({ row: 10, col: 1 });

    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    grid.scrollToRow(12, { column: 'nope' });
    await stable(fixture);
    expect(await harness.getActiveCell()).toEqual({ row: 12, col: 1 });
    expect(warn).toHaveBeenCalledTimes(1);
    expect(String(warn.mock.calls[0][0])).toContain("'nope'");
  });

  it('activate false scrolls without moving the active cell', async () => {
    const { fixture, harness, grid, scroller } = await setup();
    await harness.clickCell(1, 0);
    grid.scrollToRow(150, { activate: false });
    await stable(fixture);
    // The active row stays rendered (as an outlier) while out of view.
    expect(await harness.getActiveCell()).toEqual({ row: 1, col: 0 });
    expect(firstVisibleRow(scroller)).toBeGreaterThan(100);
  });

  it('never moves focus into the grid', async () => {
    const { fixture, grid } = await setup();
    const outside = (fixture.nativeElement as HTMLElement).querySelector(
      '.outside',
    ) as HTMLButtonElement;
    outside.focus();
    grid.scrollToRow(80, { flash: true });
    await stable(fixture);
    expect(document.activeElement).toBe(outside);
  });

  it('flashes the row by id for the token duration, following it through a reorder', async () => {
    const { fixture, harness, grid } = await setup();
    const started = Date.now();
    grid.scrollToRow(3, { flash: true });
    await stable(fixture);
    expect(await harness.isRowFlashing(3)).toBe(true);
    expect(await harness.isRowFlashing(4)).toBe(false);

    // Reversed data: row id 3 now renders at view index 196, and the row
    // element at index 3 (id 196) does not carry the flash.
    fixture.componentInstance.rows.update((rows) => [...rows].reverse());
    await stable(fixture);
    expect(await harness.isRowFlashing(3)).toBe(false);
    grid.scrollToRow(3, { activate: false, flash: false });
    await stable(fixture);
    expect(await harness.isRowFlashing(196)).toBe(true);

    // The 400ms duration from the host's token, then the flash clears.
    let polls = 0;
    while ((await harness.isRowFlashing(196)) && polls < 60) {
      await new Promise((resolve) => setTimeout(resolve, 50));
      polls += 1;
    }
    expect(await harness.isRowFlashing(196)).toBe(false);
    expect(Date.now() - started).toBeGreaterThanOrEqual(350);
  });

  it('a flash of a row still flashing restarts its animation', async () => {
    const { fixture, grid, scroller } = await setup();
    // A long flash, so the first one is still running at the second.
    (fixture.nativeElement as HTMLElement)
      .querySelector('tm-grid')!
      .setAttribute('style', 'block-size: 300px; --grid-flash-duration: 5s');
    const cellAnimation = (): CSSAnimation => {
      const cell = scroller.querySelector('[role="row"][aria-rowindex="5"] [data-tm-cell]')!;
      const animations = cell.getAnimations().filter((a) => a instanceof CSSAnimation);
      expect(animations.length).toBe(1);
      return animations[0] as CSSAnimation;
    };
    grid.scrollToRow(3, { flash: true });
    await stable(fixture);
    const first = cellAnimation();

    grid.scrollToRow(3, { flash: true });
    await stable(fixture);
    const second = cellAnimation();
    expect(second).not.toBe(first);
    expect(second.animationName).not.toBe(first.animationName);
  });

  it('wins over a remembered scroll position restored in the same render', async () => {
    // Visit the content once and leave it scrolled far down.
    const { fixture, scroller } = await setup();
    scroller.scrollTop = 150 * ROW_HEIGHT;
    scroller.dispatchEvent(new Event('scroll'));
    await stable(fixture);
    expect(firstVisibleRow(scroller)).toBeGreaterThan(100);
    fixture.destroy();

    // Remount: the rows arrive later, and a deep link reveals row 20 then.
    const linked = TestBed.createComponent(DeepLinkHost);
    await stable(linked);
    linked.componentInstance.rows.set(makeRows(200));
    await stable(linked);
    expect(linked.componentInstance.link().result).toBe(true);
    const view = (linked.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    // Row 20 centred, not the remembered position.
    const body = view.clientHeight - ROW_HEIGHT;
    const expected = Math.round(20 * ROW_HEIGHT - (body - ROW_HEIGHT) / 2);
    expect(Math.abs(view.scrollTop - expected)).toBeLessThanOrEqual(1);
  });

  for (const dir of ['ltr', 'rtl'] as const) {
    it(`brings a far row's last column into view in a wide grid (${dir})`, async () => {
      TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
      const fixture = TestBed.createComponent(WideHost);
      fixture.componentInstance.dir.set(dir);
      await stable(fixture);
      const harness = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmGridHarness);
      const scroller = (fixture.nativeElement as HTMLElement).querySelector(
        '.tm-grid__scroller',
      ) as HTMLElement;
      expect(scroller.scrollWidth).toBeGreaterThan(scroller.clientWidth);

      // The row is not rendered when the reveal first runs, so its cell
      // comes into view sideways on the next pass.
      expect(fixture.componentInstance.grid().scrollToRow(180, { column: 'c19' })).toBe(true);
      await stable(fixture);
      expect(await harness.getActiveCell()).toEqual({ row: 180, col: 19 });
      const cell = scroller.querySelector(
        '[role="row"][aria-rowindex="182"] [data-tm-cell][aria-colindex="21"]',
      );
      expect(cell).not.toBeNull();
      expect(Math.abs(scroller.scrollLeft)).toBeGreaterThan(0);
      expect(insideScroller(scroller, cell!)).toBe(true);
    });
  }
});
