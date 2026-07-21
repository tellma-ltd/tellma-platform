// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestKey } from '@angular/cdk/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmGridHarness, TmGridRowHarness } from '@tellma/core-ui-testing';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';
import { TmGridStateStore } from './tm-grid-state-store';
import { TmGridHeaderDef } from './tm-grid-templates';

interface Row {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
  readonly active: boolean;
  readonly note: string;
}

function makeRows(count: number): readonly Row[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i,
    name: `Item ${i}`,
    qty: 1000 + i,
    active: i % 2 === 0,
    note: `note ${i}`,
  }));
}

/** Fallback row height (no tokens CSS loads in the unit-test bundle). */
const ROW_HEIGHT = 32;

@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid
      gridId="spec-grid"
      [data]="rows()"
      [rowId]="rowId"
      [loading]="loading()"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="120" />
      <tm-grid-column key="qty" type="number" header="Qty" [flex]="1" />
      <tm-grid-column key="active" type="boolean" header="Active" [width]="80" />
      <tm-grid-column key="note" header="Note" [flex]="2" />
    </tm-grid>
  `,
})
class Host {
  readonly rows = signal<readonly Row[]>(makeRows(200));
  readonly loading = signal(false);
  readonly rowId = (row: Row): number => row.id;
}

@Component({
  imports: [TmGrid, TmGridColumn, TmGridHeaderDef],
  template: `
    <tm-grid gridId="spec-grid-header" [data]="rows()" [rowId]="rowId" style="block-size: 300px">
      <tm-grid-column key="name" header="Name" [width]="140">
        <ng-container *tmGridHeader="let header">
          <span class="hdr-label">{{ header }}</span>
          <button type="button" class="hdr-btn">filter</button>
        </ng-container>
      </tm-grid-column>
      <tm-grid-column key="qty" type="number" header="Qty" [flex]="1" />
    </tm-grid>
  `,
})
class HeaderHost {
  readonly rows = signal<readonly Row[]>(makeRows(20));
  readonly rowId = (row: Row): number => row.id;
}

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

async function setup(): Promise<{
  fixture: ComponentFixture<Host>;
  grid: HTMLElement;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  await stable(fixture);
  const grid = (fixture.nativeElement as HTMLElement).querySelector('tm-grid') as HTMLElement;
  const scroller = grid.querySelector('.tm-grid__scroller') as HTMLElement;
  return { fixture, grid, scroller };
}

function keydown(target: HTMLElement, key: string, init: KeyboardEventInit = {}): void {
  target.dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }),
  );
}

function pointerPress(target: HTMLElement, init: PointerEventInit = {}): void {
  target.dispatchEvent(
    new PointerEvent('pointerdown', { bubbles: true, cancelable: true, button: 0, ...init }),
  );
}

function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
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

describe('tm-grid (readonly core)', () => {
  it('renders only the window (plus overscan), never the whole model', async () => {
    const { scroller } = await setup();
    const rows = scroller.querySelectorAll('.tm-grid__row');
    // 300px viewport at 32px rows ⇒ ~9 visible + 4 overscan below; never 200.
    expect(rows.length).toBeGreaterThan(8);
    expect(rows.length).toBeLessThan(25);
    const spacer = scroller.querySelector('.tm-grid__spacer') as HTMLElement;
    expect(spacer.style.blockSize).toBe(`${200 * ROW_HEIGHT}px`);
  });

  it('ArrowDown moves the active cell with roving focus and ARIA state', async () => {
    const { fixture, scroller } = await setup();
    scroller.focus();
    keydown(scroller, 'ArrowDown'); // first arrow activates the origin
    await stable(fixture);
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));

    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    const cell = cellAt(scroller, 1, 0) as HTMLElement;
    expect(document.activeElement).toBe(cell);
    expect(cell.getAttribute('tabindex')).toBe('0');
    expect(cell.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 0, 0)?.getAttribute('tabindex')).toBe('-1');
    // Container is no longer the tab stop while a cell is active.
    expect(scroller.getAttribute('tabindex')).toBe('-1');
  });

  it('Shift+ArrowDown extends the selection from the anchor', async () => {
    const { fixture, scroller } = await setup();
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    keydown(scroller, 'ArrowDown', { shiftKey: true });
    await stable(fixture);
    const selected = scroller.querySelectorAll('[data-tm-cell][aria-selected="true"]');
    expect(selected.length).toBe(2);
    // Extension leaves the active cell in place.
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));
  });

  it('Mod+A + copy writes TSV and marked HTML onto the clipboard event', async () => {
    const { fixture, scroller } = await setup();
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    keydown(scroller, 'a', { ctrlKey: true });
    await stable(fixture);

    const data = new DataTransfer();
    const copy = new ClipboardEvent('copy', { clipboardData: data, cancelable: true, bubbles: true });
    scroller.dispatchEvent(copy);
    expect(copy.defaultPrevented).toBe(true);

    const tsv = data.getData('text/plain');
    const lines = tsv.split('\r\n').filter((line) => line !== '');
    expect(lines.length).toBe(200);
    expect(lines[0].split('\t')).toEqual(['Item 0', '1,000', 'TRUE', 'note 0']);
    expect(lines[1].split('\t')[2]).toBe('FALSE');

    const html = data.getData('text/html');
    expect(html).toContain('data-tm-grid');
    expect(html).toContain('data-tm-v="&quot;Item 0&quot;"'); // typed raw values present
  });

  it('scrolling re-windows and keeps the active row rendered as an outlier with focus', async () => {
    const { fixture, scroller } = await setup();
    scroller.focus();
    keydown(scroller, 'ArrowDown');
    await stable(fixture);
    const activeCell = cellAt(scroller, 0, 0) as HTMLElement;
    expect(document.activeElement).toBe(activeCell);

    await scrollTo(fixture, scroller, 100 * ROW_HEIGHT);
    // The window moved…
    expect(cellAt(scroller, 100, 0)).not.toBeNull();
    expect(cellAt(scroller, 10, 0)).toBeNull();
    // …but the active row is still rendered, outside the window, same element.
    const outlierCell = cellAt(scroller, 0, 0) as HTMLElement;
    expect(outlierCell).toBe(activeCell);
    expect(outlierCell.closest('.tm-grid__row')?.classList).toContain('tm-grid__row--outlier');
    expect(document.activeElement).toBe(activeCell);
  });

  it('renders number cells localized and boolean cells as glyph + hidden text', async () => {
    const { scroller } = await setup();
    expect(cellAt(scroller, 0, 1)?.textContent?.trim()).toBe('1,000');

    const onCell = cellAt(scroller, 0, 2) as HTMLElement;
    expect(onCell.querySelector('.tm-grid-bool.tm-grid-bool--on')).not.toBeNull();
    expect(onCell.querySelector('.tm-visually-hidden')?.textContent?.trim()).toBe('TRUE');

    const offCell = cellAt(scroller, 1, 2) as HTMLElement;
    expect(offCell.querySelector('.tm-grid-bool')).not.toBeNull();
    expect(offCell.querySelector('.tm-grid-bool--on')).toBeNull();
    expect(offCell.querySelector('.tm-visually-hidden')?.textContent?.trim()).toBe('FALSE');
  });

  it('shows the localized empty message for a bound, loaded, zero-row grid', async () => {
    const { fixture, scroller } = await setup();
    fixture.componentInstance.rows.set([]);
    await stable(fixture);
    // The overlay is a sibling of the scroller (it covers the viewport), so
    // query it from the grid-view host, not inside the scroller.
    const overlay = scroller.parentElement!.querySelector('[data-tm-empty]');
    expect(overlay?.textContent).toContain('No records to display');
  });

  it('loading sets aria-busy, keeps headers, and shows the spinner overlay', async () => {
    const { fixture, scroller } = await setup();
    fixture.componentInstance.loading.set(true);
    await stable(fixture);
    expect(scroller.getAttribute('aria-busy')).toBe('true');
    expect(scroller.parentElement!.querySelector('[data-tm-loading] tm-spinner')).not.toBeNull();
    expect(scroller.querySelectorAll('.tm-grid__colhdr').length).toBe(4);
  });

  it('exposes coherent ARIA counts over the virtualized model', async () => {
    const { scroller } = await setup();
    expect(scroller.getAttribute('role')).toBe('grid');
    expect(scroller.getAttribute('aria-rowcount')).toBe('201'); // 200 rows + header row
    expect(scroller.getAttribute('aria-colcount')).toBe('5'); // 4 data columns + row header
    const header = scroller.querySelector('.tm-grid__colhdr[data-col="0"]');
    expect(header?.getAttribute('aria-colindex')).toBe('2');
  });

  it('restores scroll and active cell across destroy/recreate for the same gridId + content', async () => {
    const first = await setup();
    first.scroller.focus();
    keydown(first.scroller, 'ArrowDown');
    keydown(first.scroller, 'ArrowDown');
    keydown(first.scroller, 'ArrowDown'); // activates (0,0) then moves to (2,0)
    await stable(first.fixture);
    await scrollTo(first.fixture, first.scroller, 50 * ROW_HEIGHT);
    first.fixture.destroy();

    const second = TestBed.createComponent(Host);
    await stable(second);
    const scroller = (second.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    expect(scroller.scrollTop).toBe(50 * ROW_HEIGHT);
    // The active cell restored by row identity (rendered as the outlier).
    expect(cellAt(scroller, 2, 0)?.getAttribute('tabindex')).toBe('0');
  });

  it('throws in dev mode when a second live grid registers the same gridId', async () => {
    const { fixture } = await setup();
    await stable(fixture);
    const duplicate = TestBed.createComponent(Host);
    expect(() => duplicate.detectChanges()).toThrowError(/already live/);
  });

  it('emits px tracks for fixed columns and minmax(fr) tracks for proportional ones', async () => {
    const { grid } = await setup();
    const template = grid.style.getPropertyValue('--grid-template');
    expect(template).toBe(
      'var(--grid-row-header-width) 120px minmax(var(--grid-min-col-width), 1fr) 80px ' +
        'minmax(var(--grid-min-col-width), 2fr)',
    );
  });

  it('is drivable through TmGridHarness: structure, pointer gestures, and keys', async () => {
    const { fixture } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const grid = await loader.getHarness(TmGridHarness);

    expect(await grid.getRowCount()).toBe(201);
    expect(await grid.getColCount()).toBe(5);
    expect(await grid.getHeaderTexts()).toEqual(['Name', 'Qty', 'Active', 'Note']);
    expect(await grid.getCellText(1, 1)).toBe('1,001');
    expect(await grid.getActiveCell()).toBeNull();

    await grid.clickCell(0, 0);
    expect(await grid.getActiveCell()).toEqual({ row: 0, col: 0 });
    expect(await (await grid.getCell(0, 0)).isActive()).toBe(true);

    await grid.shiftClickCell(2, 1);
    // The range grew from the click anchor (3×2); the active cell stayed put.
    expect(await grid.getActiveCell()).toEqual({ row: 0, col: 0 });
    expect(await (await grid.getCell(2, 1)).isSelected()).toBe(true);
    expect(await (await grid.getCell(2, 2)).isSelected()).toBe(false);

    await grid.modClickCell(4, 2);
    // Mod-click added a second range without dropping the first.
    expect(await grid.getActiveCell()).toEqual({ row: 4, col: 2 });
    expect(await (await grid.getCell(0, 0)).isSelected()).toBe(true);

    await grid.pressKeys(TestKey.DOWN_ARROW);
    expect(await grid.getActiveCell()).toEqual({ row: 5, col: 2 });
  });

  it('row and cell harnesses read the rendered structure and the overlays', async () => {
    const { fixture } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const grid = await loader.getHarness(TmGridHarness);

    const rendered = await grid.getRenderedRowCount();
    expect(rendered).toBeGreaterThan(8);
    expect(rendered).toBeLessThan(25);

    // aria-rowindex 3 = second data row (the header row is 1).
    const row = await loader.getHarness(TmGridRowHarness.with({ ariaRowIndex: 3 }));
    expect(await row.getRowHeaderText()).toBe('2');
    expect(await row.isPlaceholder()).toBe(false);
    const cells = await row.getCells();
    expect(cells.length).toBe(4);
    expect(await cells[1].getText()).toBe('1,001');
    expect(await cells[1].getAlign()).toBe('right');
    expect(await grid.hasPlaceholderRow()).toBe(false);

    expect(await grid.isLoading()).toBe(false);
    fixture.componentInstance.loading.set(true);
    await stable(fixture);
    expect(await grid.isLoading()).toBe(true);

    fixture.componentInstance.loading.set(false);
    fixture.componentInstance.rows.set([]);
    await stable(fixture);
    expect(await grid.getEmptyText()).toContain('No records to display');
    expect(await grid.getRenderedRowCount()).toBe(0);
  });

  it('selectRange, header presses, and the corner drive selection via the harness', async () => {
    const { fixture } = await setup();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const grid = await loader.getHarness(TmGridHarness);

    await grid.selectRange({ row: 1, col: 0 }, { row: 3, col: 1 });
    expect(await (await grid.getCell(3, 1)).isSelected()).toBe(true);
    expect(await (await grid.getCell(3, 2)).isSelected()).toBe(false);

    await grid.clickRowHeader(2);
    expect(await (await grid.getCell(2, 3)).isSelected()).toBe(true);
    expect(await (await grid.getCell(1, 0)).isSelected()).toBe(false);

    await grid.clickColumnHeader(1);
    expect(await (await grid.getCell(0, 1)).isSelected()).toBe(true);
    expect(await grid.getActiveCell()).toEqual({ row: 0, col: 1 });

    await grid.clickCorner();
    expect(await (await grid.getCell(0, 3)).isSelected()).toBe(true);
  });
});

describe('tm-grid (custom header template §6.2)', () => {
  it('renders *tmGridHeader; interactive header content never selects the column', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(HeaderHost);
    await stable(fixture);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    const header = scroller.querySelector('[data-tm-colhdr][data-col="0"]') as HTMLElement;
    expect(header.querySelector('.hdr-label')?.textContent).toBe('Name');

    // A press on the projected BUTTON keeps its own affordance: no column
    // selection results (column headers select on pointerdown, like rows).
    pointerPress(header.querySelector('.hdr-btn') as HTMLElement);
    await stable(fixture);
    expect(scroller.querySelectorAll('[data-tm-cell][aria-selected="true"]').length).toBe(0);

    // A press on the header background selects the column.
    pointerPress(header);
    await stable(fixture);
    expect(cellAt(scroller, 0, 0)?.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 0, 1)?.getAttribute('aria-selected')).toBeNull();
  });

  it('column headers shift-extend a column span, like row headers', async () => {
    const { scroller, fixture } = await setup();
    const colHdr = (c: number): HTMLElement =>
      scroller.querySelector(`[data-tm-colhdr][data-col="${c}"]`) as HTMLElement;
    pointerPress(colHdr(0));
    await stable(fixture);
    pointerPress(colHdr(2), { shiftKey: true });
    document.dispatchEvent(new PointerEvent('pointerup', { pointerId: 0 })); // end the drag
    await stable(fixture);
    expect(cellAt(scroller, 0, 0)?.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 0, 1)?.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 0, 2)?.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 0, 3)?.getAttribute('aria-selected')).toBeNull();
  });

  it('right-click on a column header selects that column first', async () => {
    const { scroller, fixture } = await setup();
    const colHdr = scroller.querySelector('[data-tm-colhdr][data-col="1"]') as HTMLElement;
    colHdr.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
    await stable(fixture);
    expect(cellAt(scroller, 0, 1)?.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 0, 0)?.getAttribute('aria-selected')).toBeNull();
  });
});

describe('TmGridStateStore width blobs (§12)', () => {
  it('round-trips widths through serializeWidths → restoreWidths', () => {
    const source = new TmGridStateStore();
    source.register('grid-a', undefined).setWidths({ name: 120, qty: 90.5 });
    const blob = source.serializeWidths('grid-a');
    expect(blob).not.toBeNull();

    // A fresh store (a later session) restores the identical widths.
    const target = new TmGridStateStore();
    target.restoreWidths('grid-a', blob as string);
    expect(target.register('grid-a', undefined).getWidths()).toEqual({ name: 120, qty: 90.5 });
  });

  it('serializes null when the gridId has no persisted widths', () => {
    expect(new TmGridStateStore().serializeWidths('unknown-grid')).toBeNull();
  });

  it('ignores malformed blobs — defaults apply on the next mount', () => {
    const store = new TmGridStateStore();
    store.restoreWidths('grid-a', '{not json');
    store.restoreWidths('grid-a', '[120, 90]'); // an array is not a widths map
    store.restoreWidths('grid-a', '"120"');
    store.restoreWidths('grid-a', 'null');
    expect(store.register('grid-a', undefined).getWidths()).toBeUndefined();
  });

  it('filters non-numeric and non-finite entries from a restored blob', () => {
    const store = new TmGridStateStore();
    // 1e999 parses to Infinity; the string and null entries are not widths.
    store.restoreWidths('grid-a', '{"name":120,"qty":"wide","note":null,"extra":1e999}');
    expect(store.register('grid-a', undefined).getWidths()).toEqual({ name: 120 });
  });
});
