// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal, viewChild } from '@angular/core';
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
});
