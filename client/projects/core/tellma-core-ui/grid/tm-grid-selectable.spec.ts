// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { LiveAnnouncer } from '@angular/cdk/a11y';
import { applyEach, form, required } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmTreeGrid } from '@tellma/core-ui/tree-grid';

import { TmGrid } from './tm-grid';
import { TmGridColumn } from './tm-grid-column';

interface Row {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
  readonly note: string;
}

function makeRows(count: number): readonly Row[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i,
    name: `Item ${i}`,
    qty: 1000 + i,
    note: `note ${i}`,
  }));
}

/** Readonly selectable + searchable list grid (the list-screen shape). */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid
      gridId="selectable-grid"
      [data]="rows()"
      [rowId]="rowId"
      [selectable]="selectable()"
      [searchable]="searchable()"
      [(selectedIds)]="selection"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="120" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="100" />
      <tm-grid-column key="note" header="Note" [flex]="1" />
    </tm-grid>
  `,
})
class SelectableHost {
  readonly rows = signal<readonly Row[]>(makeRows(30));
  readonly selectable = signal(true);
  readonly searchable = signal(false);
  readonly selection = signal<ReadonlySet<string | number>>(new Set());
  readonly rowId = (row: Row): number => row.id;
}

interface Line {
  readonly id: number;
  readonly name: string | null;
  readonly qty: number | null;
}

/** An EDITABLE grid with `selectable` on — the dev-mode misconfiguration. */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid gridId="selectable-editable-grid" [field]="f" [rowId]="rowId" selectable style="block-size: 200px">
      <tm-grid-column key="name" header="Name" [width]="120" />
    </tm-grid>
  `,
})
class SelectableEditableHost {
  readonly model = signal<Line[]>([{ id: 1, name: 'Alpha', qty: 1 }]);
  readonly f = form(this.model, (lines) => {
    applyEach(lines, (line) => required(line.name));
  });
  readonly rowId = (row: Line): number => row.id;
}

/** An editable searchable grid (invalid-input raw text must be findable). */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <tm-grid gridId="searchable-editable-grid" [field]="f" [rowId]="rowId" searchable style="block-size: 200px">
      <tm-grid-column key="name" header="Name" [width]="120" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="100" />
    </tm-grid>
  `,
})
class SearchableEditableHost {
  readonly model = signal<Line[]>([
    { id: 1, name: 'Alpha', qty: 10 },
    { id: 2, name: 'Beta', qty: 20 },
  ]);
  readonly f = form(this.model);
  readonly rowId = (row: Line): number => row.id;
}

interface TreeRow {
  readonly id: number;
  readonly parentId: number | null;
  readonly name: string;
}

/** A searchable tree whose interesting row hides in a collapsed subtree. */
@Component({
  imports: [TmTreeGrid, TmGridColumn],
  template: `
    <tm-tree-grid
      gridId="searchable-tree"
      [data]="rows()"
      [rowId]="rowId"
      [parentId]="parentId"
      [defaultExpandedDepth]="0"
      searchable
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [flex]="1" />
    </tm-tree-grid>
  `,
})
class SearchableTreeHost {
  readonly rows = signal<readonly TreeRow[]>([
    { id: 1, parentId: null, name: 'Assets' },
    { id: 2, parentId: 1, name: 'Cash' },
    { id: 3, parentId: 1, name: 'Receivables' },
    { id: 4, parentId: null, name: 'Liabilities' },
  ]);
  readonly rowId = (row: TreeRow): number => row.id;
  readonly parentId = (row: TreeRow): number | null => row.parentId;
}

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

/** Waits out the find debounce (250ms) plus the scan slices. */
async function findScanned(fixture: ComponentFixture<unknown>): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 400));
  await stable(fixture);
}

function keydown(target: Element, key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

function cellAt(scroller: HTMLElement, row: number, col: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(
    `[data-tm-cell][data-row="${row}"][data-col="${col}"]`,
  );
}

function checkCellAt(scroller: HTMLElement, row: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(`[data-tm-checkcell][data-row="${row}"]`);
}

function rowElementAt(scroller: HTMLElement, viewIndex: number): HTMLElement | null {
  return scroller.querySelector<HTMLElement>(`.tm-grid__row[aria-rowindex="${viewIndex + 2}"]`);
}

function findInput(root: HTMLElement): HTMLInputElement | null {
  return root.querySelector<HTMLInputElement>('[data-tm-find-input]');
}

function click(target: Element, init: MouseEventInit = {}): void {
  target.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ...init }));
}

async function setupSelectable(): Promise<{
  fixture: ComponentFixture<SelectableHost>;
  host: SelectableHost;
  grid: HTMLElement;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(SelectableHost);
  await stable(fixture);
  const grid = (fixture.nativeElement as HTMLElement).querySelector('tm-grid') as HTMLElement;
  const scroller = grid.querySelector('.tm-grid__scroller') as HTMLElement;
  return { fixture, host: fixture.componentInstance, grid, scroller };
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

describe('tm-grid (row checkbox selection §8.8)', () => {
  it('throws in dev mode when selectable meets an editable grid', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(SelectableEditableHost);
    expect(() => fixture.detectChanges()).toThrowError(/readonly grid/);
  });

  it('renders the checkbox chrome column with a tri-state select-all header', async () => {
    const { scroller } = await setupSelectable();
    const header = scroller.querySelector('[data-tm-checkhdr]');
    expect(header).not.toBeNull();
    expect(header?.getAttribute('aria-colindex')).toBe('2');
    const selectAll = scroller.querySelector('[data-tm-checkall]');
    expect(selectAll?.getAttribute('role')).toBe('checkbox');
    expect(selectAll?.getAttribute('aria-checked')).toBe('false');
    expect(selectAll?.getAttribute('tabindex')).toBe('-1');
    expect(selectAll?.getAttribute('aria-label')).toBe('Select all rows');
    // ARIA offsets account for the extra chrome column.
    expect(scroller.getAttribute('aria-colcount')).toBe('5'); // 3 data + rowhdr + checkbox
    expect(
      scroller.querySelector('.tm-grid__colhdr[data-col="0"]')?.getAttribute('aria-colindex'),
    ).toBe('3');
    // Row checkboxes are labelled and unchecked; none is a tab stop.
    const rowCheck = checkCellAt(scroller, 0)?.querySelector('[role="checkbox"]');
    expect(rowCheck?.getAttribute('aria-checked')).toBe('false');
    expect(rowCheck?.getAttribute('aria-label')).toBe('Select row');
    // A readonly grid never has a placeholder row (nothing without a checkbox).
    expect(scroller.querySelector('.tm-grid__row--placeholder')).toBeNull();
  });

  it('clicking a checkbox toggles selectedIds with a FRESH Set each time', async () => {
    const { fixture, host, scroller } = await setupSelectable();
    const before = host.selection();
    click(checkCellAt(scroller, 1) as HTMLElement);
    await stable(fixture);
    const after = host.selection();
    expect(after).not.toBe(before);
    expect(before.size).toBe(0); // the old instance was never mutated
    expect([...after]).toEqual([1]);
    expect(
      checkCellAt(scroller, 1)?.querySelector('[role="checkbox"]')?.getAttribute('aria-checked'),
    ).toBe('true');

    click(checkCellAt(scroller, 1) as HTMLElement);
    await stable(fixture);
    expect(host.selection().size).toBe(0);
    expect(host.selection()).not.toBe(after);
  });

  it('Shift+click checks the range from the last toggled row (the Gmail model)', async () => {
    const { fixture, host, scroller } = await setupSelectable();
    click(checkCellAt(scroller, 1) as HTMLElement);
    await stable(fixture);
    click(checkCellAt(scroller, 4) as HTMLElement, { shiftKey: true });
    await stable(fixture);
    expect([...host.selection()].sort((a, b) => Number(a) - Number(b))).toEqual([1, 2, 3, 4]);

    // The anchor moved to row 4; unchecking 4 then shift-clicking 2 clears 2..4.
    click(checkCellAt(scroller, 4) as HTMLElement);
    await stable(fixture);
    click(checkCellAt(scroller, 2) as HTMLElement, { shiftKey: true });
    await stable(fixture);
    expect([...host.selection()]).toEqual([1]);
  });

  it('Space toggles the active row; Ctrl+Shift+Space cycles all/none (mixed → all)', async () => {
    const { fixture, host, scroller } = await setupSelectable();
    await activateOrigin(fixture, scroller);
    const space = keydown(scroller, ' ');
    await stable(fixture);
    expect(space.defaultPrevented).toBe(true);
    expect([...host.selection()]).toEqual([0]);
    // One row of thirty checked = mixed.
    expect(scroller.querySelector('[data-tm-checkall]')?.getAttribute('aria-checked')).toBe(
      'mixed',
    );

    // Mixed → all (every DATA row, not just the rendered window).
    keydown(scroller, ' ', { ctrlKey: true, shiftKey: true });
    await stable(fixture);
    expect(host.selection().size).toBe(30);
    expect(scroller.querySelector('[data-tm-checkall]')?.getAttribute('aria-checked')).toBe(
      'true',
    );

    // All → none.
    keydown(scroller, ' ', { ctrlKey: true, shiftKey: true });
    await stable(fixture);
    expect(host.selection().size).toBe(0);
    expect(scroller.querySelector('[data-tm-checkall]')?.getAttribute('aria-checked')).toBe(
      'false',
    );
  });

  it('clicking the select-all header checks everything, and again clears', async () => {
    const { fixture, host, scroller } = await setupSelectable();
    click(scroller.querySelector('[data-tm-checkall]') as HTMLElement);
    await stable(fixture);
    expect(host.selection().size).toBe(30);
    click(scroller.querySelector('[data-tm-checkall]') as HTMLElement);
    await stable(fixture);
    expect(host.selection().size).toBe(0);
  });

  it('checked rows carry ROW-level aria-selected while range selection stays cell-scoped', async () => {
    const { fixture, scroller } = await setupSelectable();
    click(checkCellAt(scroller, 1) as HTMLElement);
    await stable(fixture);
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowDown', { shiftKey: true }); // range (0,0)-(1,0)
    await stable(fixture);

    // Row 1 is BOTH checked (row aria-selected) and range-covered (cell
    // aria-selected); row 0 is only range-covered.
    expect(rowElementAt(scroller, 1)?.getAttribute('aria-selected')).toBe('true');
    expect(rowElementAt(scroller, 1)?.classList).toContain('tm-grid__row--checked');
    expect(rowElementAt(scroller, 0)?.getAttribute('aria-selected')).toBeNull();
    expect(cellAt(scroller, 0, 0)?.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 1, 0)?.getAttribute('aria-selected')).toBe('true');
    expect(cellAt(scroller, 1, 1)?.getAttribute('aria-selected')).toBeNull();
  });

  it('keeps the checkbox column outside the cell coordinate space (arrows, copy)', async () => {
    const { fixture, scroller } = await setupSelectable();
    await activateOrigin(fixture, scroller);
    // ArrowLeft at column 0 stays at column 0 — arrows never land on chrome.
    keydown(scroller, 'ArrowLeft');
    await stable(fixture);
    expect(document.activeElement).toBe(cellAt(scroller, 0, 0));

    // A full-row selection copies the DATA columns only.
    keydown(scroller, ' ', { shiftKey: true }); // Shift+Space selects the row
    await stable(fixture);
    const data = new DataTransfer();
    scroller.dispatchEvent(
      new ClipboardEvent('copy', { clipboardData: data, cancelable: true, bubbles: true }),
    );
    const line = data.getData('text/plain').split('\r\n')[0];
    expect(line.split('\t')).toEqual(['Item 0', '1,000', 'note 0']);
  });

  it('announces the checked count through the live region (debounced)', async () => {
    const { fixture, scroller } = await setupSelectable();
    const announcer = TestBed.inject(LiveAnnouncer);
    const announce = vi.spyOn(announcer, 'announce');
    click(checkCellAt(scroller, 0) as HTMLElement);
    click(checkCellAt(scroller, 1) as HTMLElement);
    await stable(fixture);
    await new Promise((resolve) => setTimeout(resolve, 250));
    const texts = announce.mock.calls.map((call) => String(call[0]));
    // The burst coalesced into one final announcement.
    expect(texts.filter((text) => text.includes('selected'))).toEqual(['2 of 30 selected']);
  });

  it('does not render checkbox chrome while selectable is off', async () => {
    const { fixture, host, scroller } = await setupSelectable();
    host.selectable.set(false);
    await stable(fixture);
    expect(scroller.querySelector('[data-tm-checkhdr]')).toBeNull();
    expect(scroller.querySelector('[data-tm-checkcell]')).toBeNull();
    expect(scroller.getAttribute('aria-colcount')).toBe('4');
  });
});

describe('tm-grid (find §8.7)', () => {
  async function setupSearchable(): Promise<{
    fixture: ComponentFixture<SelectableHost>;
    host: SelectableHost;
    grid: HTMLElement;
    scroller: HTMLElement;
  }> {
    const context = await setupSelectable();
    context.host.selectable.set(false);
    context.host.searchable.set(true);
    await stable(context.fixture);
    return context;
  }

  /** Opens the bar via Mod+F and types `query` into its input. */
  async function openAndType(
    fixture: ComponentFixture<unknown>,
    grid: HTMLElement,
    scroller: HTMLElement,
    query: string,
  ): Promise<HTMLInputElement> {
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'f', { ctrlKey: true });
    await stable(fixture);
    const input = findInput(grid) as HTMLInputElement;
    input.value = query;
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await findScanned(fixture);
    return input;
  }

  it('Mod+F opens the find bar and focuses its input', async () => {
    const { fixture, grid, scroller } = await setupSearchable();
    await activateOrigin(fixture, scroller);
    const event = keydown(scroller, 'f', { ctrlKey: true });
    await stable(fixture);
    expect(event.defaultPrevented).toBe(true);
    const input = findInput(grid);
    expect(input).not.toBeNull();
    expect(document.activeElement).toBe(input);
  });

  it('non-searchable grids leave Mod+F to the browser and render no bar', async () => {
    const { fixture, grid, scroller } = await setupSelectable();
    await activateOrigin(fixture, scroller);
    const ignored = keydown(scroller, 'f', { ctrlKey: true });
    await stable(fixture);
    expect(ignored.defaultPrevented).toBe(false);
    expect(findInput(grid)).toBeNull();
  });

  it('scans all columns via display text (formatted numbers included) and counts matches', async () => {
    const { fixture, grid, scroller } = await setupSearchable();
    await openAndType(fixture, grid, scroller, '1,003'); // qty 1003 formats as '1,003'
    const counter = grid.querySelector('[data-tm-find-counter]');
    expect(counter?.textContent?.trim()).toBe('1 of 1');
    // The match highlights in the rendered window.
    expect(cellAt(scroller, 3, 1)?.classList).toContain('tm-grid__cell--find');
  });

  it('Enter cycles matches, ACTIVATING each match cell while focus stays in the input', async () => {
    const { fixture, grid, scroller } = await setupSearchable();
    const input = await openAndType(fixture, grid, scroller, 'note 2'); // rows 2, 20-29
    expect(grid.querySelector('[data-tm-find-counter]')?.textContent?.trim()).toBe('1 of 11');

    keydown(input, 'Enter');
    await stable(fixture);
    // First Enter moves past the nearest match (row 2) to row 20.
    expect(grid.querySelector('[data-tm-find-counter]')?.textContent?.trim()).toBe('2 of 11');
    const active = scroller.querySelector('.tm-grid__cell--active');
    expect(active?.getAttribute('data-row')).toBe('20');
    expect(active?.getAttribute('data-col')).toBe('2');
    expect(active?.classList).toContain('tm-grid__cell--find-active');
    expect(document.activeElement).toBe(input);

    keydown(input, 'Enter', { shiftKey: true });
    await stable(fixture);
    expect(grid.querySelector('[data-tm-find-counter]')?.textContent?.trim()).toBe('1 of 11');
  });

  it('shows the no-matches counter for a query nothing contains', async () => {
    const { fixture, grid, scroller } = await setupSearchable();
    await openAndType(fixture, grid, scroller, 'xyzzy');
    expect(grid.querySelector('[data-tm-find-counter]')?.textContent?.trim()).toBe('No matches');
    expect(grid.querySelector<HTMLButtonElement>('[data-tm-find-next]')?.disabled).toBe(true);
  });

  it('Esc clears the query, closes the bar, and returns focus to the grid at the match', async () => {
    const { fixture, grid, scroller } = await setupSearchable();
    const input = await openAndType(fixture, grid, scroller, 'note 5');
    keydown(input, 'Enter');
    await stable(fixture);
    keydown(input, 'Escape');
    await stable(fixture);
    expect(findInput(grid)).toBeNull();
    // Focus landed on the (activated) match cell: row 5, note column.
    expect(document.activeElement).toBe(cellAt(scroller, 5, 2));
    // Highlights cleared with the query.
    expect(scroller.querySelector('.tm-grid__cell--find')).toBeNull();
  });

  it('finds rows hidden inside collapsed subtrees and expands their ancestors on navigation', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(SearchableTreeHost);
    await stable(fixture);
    const grid = (fixture.nativeElement as HTMLElement).querySelector(
      'tm-tree-grid',
    ) as HTMLElement;
    const scroller = grid.querySelector('.tm-grid__scroller') as HTMLElement;
    // Depth 0: only the roots are visible; 'Cash' is hidden under 'Assets'.
    expect(scroller.textContent).not.toContain('Cash');

    await activateOrigin(fixture, scroller);
    keydown(scroller, 'f', { ctrlKey: true });
    await stable(fixture);
    const input = findInput(grid) as HTMLInputElement;
    input.value = 'Cash';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await findScanned(fixture);
    expect(grid.querySelector('[data-tm-find-counter]')?.textContent?.trim()).toBe('1 of 1');

    keydown(input, 'Enter');
    await stable(fixture);
    // The ancestor expanded and the match's cell became the active cell.
    const active = scroller.querySelector('.tm-grid__cell--active');
    expect(active?.textContent).toContain('Cash');
    expect(document.activeElement).toBe(input); // focus stayed in the bar
  });

  it('finds the invalid-input raw text of an editable grid', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(SearchableEditableHost);
    await stable(fixture);
    const grid = (fixture.nativeElement as HTMLElement).querySelector('tm-grid') as HTMLElement;
    const scroller = grid.querySelector('.tm-grid__scroller') as HTMLElement;
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowRight'); // (0,1) qty
    await stable(fixture);

    // Commit unparseable text — it stays visible as the cell's raw text.
    keydown(scroller, 'z');
    const editor = scroller.querySelector<HTMLInputElement>(
      '[data-tm-editor] input',
    ) as HTMLInputElement;
    editor.value = 'zebra';
    editor.dispatchEvent(new Event('input', { bubbles: true }));
    keydown(editor, 'Enter');
    await stable(fixture);

    keydown(scroller, 'f', { ctrlKey: true });
    await stable(fixture);
    const input = findInput(grid) as HTMLInputElement;
    input.value = 'zebra';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await findScanned(fixture);
    expect(grid.querySelector('[data-tm-find-counter]')?.textContent?.trim()).toBe('1 of 1');
    expect(cellAt(scroller, 0, 1)?.classList).toContain('tm-grid__cell--find');
  });
});

describe('tm-grid (touch §8.6)', () => {
  it('touch pointerdown neither prevents default nor starts a drag; the tap click activates', async () => {
    const { fixture, scroller } = await setupSelectable();
    const cell = cellAt(scroller, 2, 0) as HTMLElement;

    const down = new PointerEvent('pointerdown', {
      pointerType: 'touch',
      button: 0,
      bubbles: true,
      cancelable: true,
    });
    cell.dispatchEvent(down);
    expect(down.defaultPrevented).toBe(false); // native pan must stay possible

    // No drag pipeline armed: moving the pointer extends nothing.
    (cellAt(scroller, 5, 0) as HTMLElement).dispatchEvent(
      new PointerEvent('pointermove', { pointerType: 'touch', bubbles: true }),
    );
    await stable(fixture);
    expect(scroller.querySelectorAll('[data-tm-cell][aria-selected="true"]').length).toBe(0);

    // The synthesized click is the tap: it activates the cell.
    cell.dispatchEvent(
      new PointerEvent('click', { pointerType: 'touch', bubbles: true, cancelable: true }),
    );
    await stable(fixture);
    expect(cellAt(scroller, 2, 0)?.getAttribute('tabindex')).toBe('0');
    expect(cellAt(scroller, 2, 0)?.classList).toContain('tm-grid__cell--active');
  });

  it('mouse pointerdown keeps the existing press semantics (preventDefault + activate)', async () => {
    const { fixture, scroller } = await setupSelectable();
    const cell = cellAt(scroller, 2, 0) as HTMLElement;
    const down = new PointerEvent('pointerdown', {
      pointerType: 'mouse',
      button: 0,
      bubbles: true,
      cancelable: true,
    });
    cell.dispatchEvent(down);
    await stable(fixture);
    expect(down.defaultPrevented).toBe(true);
    expect(cellAt(scroller, 2, 0)?.classList).toContain('tm-grid__cell--active');
  });

  it('renders no selection handles on fine-pointer devices', async () => {
    const { fixture, scroller } = await setupSelectable();
    await activateOrigin(fixture, scroller);
    keydown(scroller, 'ArrowDown', { shiftKey: true });
    await stable(fixture);
    // Headless desktop Chromium reports (pointer: fine) — the handles
    // component must not mount at all.
    expect(scroller.querySelector('tm-grid-touch-handles')).toBeNull();
    expect(scroller.querySelector('[data-tm-handle]')).toBeNull();
  });
});
