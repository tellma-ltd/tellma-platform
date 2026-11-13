// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { applyEach, form, required } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
import type { TmRowId } from '@tellma/core-ui/contracts';
import { TmGridColumn } from '@tellma/core-ui/grid';
import { TmTreeGridHarness } from '@tellma/core-ui-testing';

import { TmTreeGrid } from './tm-tree-grid';

interface Node {
  readonly id: number;
  readonly parentId: number | null;
  readonly name: string | null;
  readonly qty: number | null;
}

function node(id: number, parentId: number | null, name: string): Node {
  return { id, parentId, name, qty: id * 10 };
}

/**
 * The readonly fixture, authored in DFS order:
 *
 *     1 ── 2 ── 4
 *       └─ 3
 *     5
 *     6  (lazy: `hasChildren` marks it; children load on demand)
 */
function makeNodes(): readonly Node[] {
  return [
    node(1, null, 'Root A'),
    node(2, 1, 'Child A1'),
    node(4, 2, 'Grand A1a'),
    node(3, 1, 'Child A2'),
    node(5, null, 'Root B'),
    node(6, null, 'Lazy root'),
  ];
}

@Component({
  imports: [TmTreeGrid, TmGridColumn],
  template: `
    <tm-tree-grid
      [gridId]="gridId()"
      [data]="rows()"
      [rowId]="rowId"
      [parentId]="parentId"
      [hasChildren]="hasChildren"
      [loadChildren]="loadChildren"
      [defaultExpandedDepth]="depth()"
      [expansionSource]="source()"
      [expandedIds]="expanded()"
      (expandedIdsChange)="onExpandedIds($event)"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="200" />
      <tm-grid-column key="qty" type="number" header="Qty" [flex]="1" />
    </tm-tree-grid>
  `,
})
class DataHost {
  readonly gridId = signal('spec-tree');
  readonly rows = signal<readonly Node[]>(makeNodes());
  readonly depth = signal<number | undefined>(undefined);
  readonly source = signal<'store' | 'model'>('store');
  readonly expanded = signal<ReadonlySet<TmRowId>>(new Set());
  /** Every set the grid emitted through `expandedIdsChange`. */
  readonly emitted: Array<ReadonlySet<TmRowId>> = [];
  /** The rows whose children load on demand. */
  readonly lazyIds = signal<ReadonlySet<number>>(new Set([6]));
  readonly rowId = (row: Node): number => row.id;
  readonly parentId = (row: Node): number | null => row.parentId;
  readonly hasChildren = (row: Node): boolean => this.lazyIds().has(row.id);

  /** The row ids of every `loadChildren` call, in call order. */
  readonly batches: number[][] = [];
  /** The settle hooks of the calls still in flight, oldest first. */
  private readonly pending: Array<{
    readonly resolve: () => void;
    readonly reject: (reason: Error) => void;
  }> = [];
  readonly loadChildren = (rows: readonly Node[]): Promise<void> => {
    this.batches.push(rows.map((row) => row.id));
    return new Promise<void>((resolve, reject) => this.pending.push({ resolve, reject }));
  };

  /** Records an emitted set and keeps it, as a two-way binding would. */
  onExpandedIds(ids: ReadonlySet<TmRowId>): void {
    this.emitted.push(ids);
    this.expanded.set(ids);
  }

  /** Settles the oldest call in flight: appends `children` and resolves, or rejects. */
  settle(children: readonly Node[], error?: Error): void {
    const call = this.pending.shift()!;
    if (error !== undefined) {
      call.reject(error);
      return;
    }
    this.rows.update((rows) => [...rows, ...children]);
    call.resolve();
  }
}

@Component({
  imports: [TmTreeGrid, TmGridColumn],
  template: `
    <tm-tree-grid
      gridId="spec-tree-edit"
      [field]="f.nodes"
      [rowId]="rowId"
      [parentId]="parentId"
      [parentIdKey]="'parentId'"
      [newRow]="newNode"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="200" />
      <tm-grid-column key="qty" type="number" header="Qty" [flex]="1" />
    </tm-tree-grid>
  `,
})
class EditHost {
  readonly model = signal({ nodes: [...makeNodes()] });
  readonly f = form(this.model, (p) => {
    applyEach(p.nodes, (n) => {
      required(n.name);
    });
  });
  readonly rowId = (row: Node): number => row.id;
  readonly parentId = (row: Node): number | null => row.parentId;
  private nextTempId = -1;
  readonly newNode = (parent?: Node): Node => ({
    id: this.nextTempId--,
    parentId: parent?.id ?? null,
    name: null,
    qty: null,
  });
}

async function stable(fixture: ComponentFixture<unknown>): Promise<void> {
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await fixture.whenStable();
}

/** What a `DataHost` starts with, applied before its first render. */
interface HostOptions {
  readonly gridId?: string;
  readonly depth?: number;
  readonly source?: 'store' | 'model';
  readonly expanded?: ReadonlySet<TmRowId>;
  readonly rows?: readonly Node[];
  readonly lazyIds?: ReadonlySet<number>;
}

async function setup(options: HostOptions = {}): Promise<{
  fixture: ComponentFixture<DataHost>;
  grid: TmTreeGridHarness;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(DataHost);
  const host = fixture.componentInstance;
  if (options.gridId !== undefined) {
    host.gridId.set(options.gridId);
  }
  host.depth.set(options.depth);
  host.source.set(options.source ?? 'store');
  host.expanded.set(options.expanded ?? new Set());
  if (options.rows !== undefined) {
    host.rows.set(options.rows);
  }
  if (options.lazyIds !== undefined) {
    host.lazyIds.set(options.lazyIds);
  }
  await stable(fixture);
  const grid = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTreeGridHarness);
  const scroller = (fixture.nativeElement as HTMLElement).querySelector(
    '.tm-grid__scroller',
  ) as HTMLElement;
  return { fixture, grid, scroller };
}

async function setupEditable(): Promise<{
  fixture: ComponentFixture<EditHost>;
  grid: TmTreeGridHarness;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(EditHost);
  await stable(fixture);
  const grid = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTreeGridHarness);
  return { fixture, grid };
}

describe('tm-tree-grid', () => {
  it('renders role="treegrid" and the DFS flattening with tree ARIA attributes', async () => {
    const { grid, scroller } = await setup();
    expect(scroller.getAttribute('role')).toBe('treegrid');

    // DFS over the fixture: 1, 2, 4, 3, 5, 6 — fully expanded by default.
    expect(await grid.getVisibleRowCount()).toBe(6);
    expect(await grid.getCellText(0, 0)).toBe('Root A');
    expect(await grid.getCellText(1, 0)).toBe('Child A1');
    expect(await grid.getCellText(2, 0)).toBe('Grand A1a');
    expect(await grid.getCellText(3, 0)).toBe('Child A2');
    expect(await grid.getCellText(4, 0)).toBe('Root B');
    expect(await grid.getCellText(5, 0)).toBe('Lazy root');

    expect(await grid.getLevel(0)).toBe(1);
    expect(await grid.getLevel(1)).toBe(2);
    expect(await grid.getLevel(2)).toBe(3);
    expect(await grid.getLevel(4)).toBe(1);

    // posinset/setsize count SIBLINGS: three roots, two children of 1.
    const rowOf = (index: number): HTMLElement =>
      scroller.querySelector(`[role="row"][aria-rowindex="${index + 2}"]`) as HTMLElement;
    expect(rowOf(0).getAttribute('aria-posinset')).toBe('1');
    expect(rowOf(0).getAttribute('aria-setsize')).toBe('3');
    expect(rowOf(3).getAttribute('aria-posinset')).toBe('2');
    expect(rowOf(3).getAttribute('aria-setsize')).toBe('2');
    expect(rowOf(2).getAttribute('aria-posinset')).toBe('1');
    expect(rowOf(2).getAttribute('aria-setsize')).toBe('1');

    // aria-expanded only on expandable rows; leaves carry none.
    expect(rowOf(0).getAttribute('aria-expanded')).toBe('true');
    expect(rowOf(3).getAttribute('aria-expanded')).toBeNull();
    expect(await grid.isExpanded(0)).toBe(true);
    expect(await grid.isExpanded(3)).toBe(false);
  });

  it('Alt+Arrow keys collapse and expand the active row (harness expand/collapse)', async () => {
    const { grid } = await setup();
    await grid.collapse(0);
    // Root A collapsed: its whole subtree left the visible sequence.
    expect(await grid.getVisibleRowCount()).toBe(3);
    expect(await grid.isExpanded(0)).toBe(false);
    expect(await grid.getCellText(1, 0)).toBe('Root B');

    await grid.expand(0);
    expect(await grid.getVisibleRowCount()).toBe(6);
    expect(await grid.isExpanded(0)).toBe(true);
  });

  it('the expander is pointer-only (tabindex -1, aria-hidden wrapper) and clicking it toggles', async () => {
    const { grid, scroller } = await setup();
    const expander = scroller.querySelector('[data-tm-expander]') as HTMLElement;
    expect(expander.getAttribute('tabindex')).toBe('-1');
    expect(expander.closest('[aria-hidden="true"]')).not.toBeNull();

    await grid.clickExpander(0);
    expect(await grid.getVisibleRowCount()).toBe(3);
    await grid.clickExpander(0);
    expect(await grid.getVisibleRowCount()).toBe(6);
  });

  it('collapsing an ancestor of the active cell moves activation to that ancestor', async () => {
    const { grid } = await setup();
    await grid.clickCell(2, 1); // Grand A1a, Qty column
    expect(await grid.getActiveCell()).toEqual({ row: 2, col: 1 });

    await grid.clickExpander(0); // collapse Root A — the active row vanishes
    expect(await grid.getActiveCell()).toEqual({ row: 0, col: 0 });
  });

  it('lazy loading: the row expands at once with a spinner, and its children show once the load resolves', async () => {
    // Collapsed to the roots (1, 5, 6), so nothing loads at the bind.
    const { fixture, grid } = await setup({ gridId: 'spec-tree-lazy', depth: 0 });
    const host = fixture.componentInstance;
    expect(host.batches).toEqual([]);

    // The lazy root is expandable BEFORE any child exists (hasChildren).
    expect(await grid.isExpanded(2)).toBe(false);
    await grid.expand(2);
    expect(host.batches).toEqual([[6]]);
    expect(await grid.isLoadingChildren(2)).toBe(true);
    expect(await grid.isExpanded(2)).toBe(true); // expanded while loading
    expect(await grid.getVisibleRowCount()).toBe(3);

    // The consumer appends the children, then the promise resolves.
    host.settle([node(7, 6, 'Lazy child'), node(8, 6, 'Lazy child 2')]);
    await stable(fixture);

    expect(await grid.isLoadingChildren(2)).toBe(false);
    expect(await grid.isExpanded(2)).toBe(true);
    expect(await grid.getVisibleRowCount()).toBe(5);
    expect(await grid.getCellText(3, 0)).toBe('Lazy child');
    expect(await grid.getLevel(3)).toBe(2);
  });

  it('a rejected lazy load collapses the row and announces the failure', async () => {
    const { fixture, grid } = await setup({ gridId: 'spec-tree-lazy-fail', depth: 0 });
    const host = fixture.componentInstance;
    const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');

    await grid.expand(2);
    expect(await grid.isLoadingChildren(2)).toBe(true);
    host.settle([], new Error('nope'));
    await stable(fixture);

    expect(await grid.isLoadingChildren(2)).toBe(false);
    expect(await grid.isExpanded(2)).toBe(false);
    expect(await grid.getVisibleRowCount()).toBe(3);
    expect(announce.mock.calls.map((call) => String(call[0]))).toContain(
      'Could not load child rows',
    );
  });

  it('re-collapsing during a lazy load wins: no expansion on resolve, instant expand after', async () => {
    const { fixture, grid } = await setup({ gridId: 'spec-tree-lazy-recollapse', depth: 0 });
    const host = fixture.componentInstance;

    await grid.expand(2);
    expect(await grid.isLoadingChildren(2)).toBe(true);
    await grid.collapse(2); // while the load is in flight
    expect(await grid.isExpanded(2)).toBe(false);

    host.settle([node(7, 6, 'Lazy child')]);
    await stable(fixture);

    // The load landed, but the node honors the user's collapse.
    expect(await grid.isExpanded(2)).toBe(false);
    expect(await grid.getVisibleRowCount()).toBe(3);

    // Children are loaded now — expanding again is synchronous, no new call.
    await grid.expand(2);
    expect(host.batches).toEqual([[6]]);
    expect(await grid.isExpanded(2)).toBe(true);
    expect(await grid.getCellText(3, 0)).toBe('Lazy child');
  });

  it('defaultExpandedDepth seeds the expansion: 0 = collapsed, 1 = roots expanded', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });

    // Distinct gridIds: each grid seeds fresh (no shared remembered state).
    const collapsed = TestBed.createComponent(DataHost);
    collapsed.componentInstance.gridId.set('spec-tree-depth0');
    collapsed.componentInstance.depth.set(0);
    await stable(collapsed);
    const collapsedGrid =
      await TestbedHarnessEnvironment.loader(collapsed).getHarness(TmTreeGridHarness);
    expect(await collapsedGrid.getVisibleRowCount()).toBe(3); // roots only
    expect(await collapsedGrid.isExpanded(0)).toBe(false);
    collapsed.destroy();

    const shallow = TestBed.createComponent(DataHost);
    shallow.componentInstance.gridId.set('spec-tree-depth1');
    shallow.componentInstance.depth.set(1);
    await stable(shallow);
    const shallowGrid =
      await TestbedHarnessEnvironment.loader(shallow).getHarness(TmTreeGridHarness);
    // Roots expanded, deeper levels collapsed: 1, 2, 3, 5, 6 (4 hidden).
    expect(await shallowGrid.getVisibleRowCount()).toBe(5);
    expect(await shallowGrid.isExpanded(0)).toBe(true);
    expect(await shallowGrid.isExpanded(1)).toBe(false);
  });

  it('restores the expansion set across destroy/recreate (state store)', async () => {
    const first = await setup();
    await first.grid.collapse(0);
    expect(await first.grid.getVisibleRowCount()).toBe(3);
    first.fixture.destroy();

    const second = TestBed.createComponent(DataHost);
    await stable(second);
    const grid = await TestbedHarnessEnvironment.loader(second).getHarness(TmTreeGridHarness);
    expect(await grid.getVisibleRowCount()).toBe(3);
    expect(await grid.isExpanded(0)).toBe(false);
    await grid.expand(0);
    expect(await grid.getVisibleRowCount()).toBe(6);
  });

  it('menu "Insert child row" appends a child, expands the parent, and activates its first editable cell', async () => {
    const { fixture, grid } = await setupEditable();
    const menu = await grid.openContextMenu(0, 0); // Root A
    await menu.clickItem('Insert child row');
    await stable(fixture);

    const nodes = fixture.componentInstance.model().nodes;
    const created = nodes[nodes.length - 1];
    expect(created.id).toBeLessThan(0); // minted temp id
    expect(created.parentId).toBe(1); // the factory stamped the parent

    // Inserted as the LAST child of Root A: after Child A2 in view order.
    expect(await grid.getCellText(4, 0)).toBe('');
    expect(await grid.getLevel(4)).toBe(2);
    expect(await grid.getActiveCell()).toEqual({ row: 4, col: 0 });
  });

  it('editable write-through: a child cell commit writes the model through the field', async () => {
    const { fixture, grid } = await setupEditable();
    await grid.openEditor(1, 0, 'type', 'Renamed');
    await grid.commitEditor();
    await stable(fixture);

    const nodes = fixture.componentInstance.model().nodes;
    expect(nodes.find((n) => n.id === 2)?.name).toBe('Renamed');
  });

  it('throws in dev mode when a second live tree grid registers the same gridId', async () => {
    const { fixture } = await setup();
    await stable(fixture);
    const duplicate = TestBed.createComponent(DataHost);
    expect(() => duplicate.detectChanges()).toThrowError(/already live/);
  });
});
describe('tm-tree-grid remembered expansion', () => {
  it('a content bind loads every expanded row with unloaded children in one call', async () => {
    // Fully expanded by default: the lazy roots 5 and 6 load at the bind,
    // together, rather than starting collapsed.
    const { fixture, grid } = await setup({ gridId: 'spec-tree-bind', lazyIds: new Set([5, 6]) });
    const host = fixture.componentInstance;
    expect(host.batches).toEqual([[5, 6]]);
    expect(await grid.isExpanded(4)).toBe(true);
    expect(await grid.isLoadingChildren(4)).toBe(true);
    expect(await grid.isLoadingChildren(5)).toBe(true);
  });

  it('user expands in one task load in one call, in data order', async () => {
    const { fixture, scroller } = await setup({
      gridId: 'spec-tree-coalesce',
      depth: 0,
      lazyIds: new Set([5, 6]),
    });
    const host = fixture.componentInstance;
    const expanderOf = (index: number): HTMLElement =>
      scroller.querySelector(
        `[role="row"][aria-rowindex="${index + 2}"] [data-tm-expander]`,
      ) as HTMLElement;
    // Lazy root 6 first, then Root B (5): one batch, in the data's order.
    expanderOf(2).click();
    expanderOf(1).click();
    await stable(fixture);
    expect(host.batches).toEqual([[5, 6]]);
  });

  it('a restore loads one batch per level and keeps ids it cannot place yet', async () => {
    const { fixture, grid } = await setup({
      gridId: 'spec-tree-restore',
      source: 'model',
      rows: [node(10, null, 'Ledger'), node(20, null, 'Assets')],
      lazyIds: new Set([10, 12, 20]),
      expanded: new Set([10, 12, 20, 999]),
    });
    const host = fixture.componentInstance;
    expect(host.batches).toEqual([[10, 20]]);

    host.settle([node(11, 10, 'Cash'), node(12, 10, 'Banks'), node(21, 20, 'Land')]);
    await stable(fixture);
    // 12 arrived and was remembered: the next level loads on its own.
    expect(host.batches).toEqual([[10, 20], [12]]);

    host.settle([node(13, 12, 'Main bank')]);
    await stable(fixture);
    expect(host.batches).toEqual([[10, 20], [12]]);
    expect(await grid.getVisibleRowCount()).toBe(6);
    expect(await grid.getCellText(3, 0)).toBe('Main bank');
    // The bound set stays whole across the batches, the unknown id included,
    // and restoring it is not a change the grid emits.
    expect(host.emitted).toEqual([]);
    expect([...host.expanded()]).toEqual([10, 12, 20, 999]);
  });

  it('a 500-id restore issues one batch per level', async () => {
    const roots = Array.from({ length: 100 }, (_, i) => node(i + 1, null, `Root ${i + 1}`));
    const children = roots.flatMap((root) => [
      node(1000 + root.id * 2, root.id, 'Child'),
      node(1001 + root.id * 2, root.id, 'Child'),
    ]);
    const grandchildren = children.map((child) => node(10_000 + child.id, child.id, 'Grandchild'));
    const leaves = grandchildren.map((grand) => node(100_000 + grand.id, grand.id, 'Leaf'));
    const expandable = [...roots, ...children, ...grandchildren].map((row) => row.id);
    expect(expandable.length).toBe(500);

    const { fixture } = await setup({
      gridId: 'spec-tree-restore-500',
      source: 'model',
      rows: roots,
      lazyIds: new Set(expandable),
      expanded: new Set(expandable),
    });
    const host = fixture.componentInstance;
    host.settle(children);
    await stable(fixture);
    host.settle(grandchildren);
    await stable(fixture);
    host.settle(leaves);
    await stable(fixture);
    expect(host.batches.map((batch) => batch.length)).toEqual([100, 200, 200]);
    expect(host.batches[0]).toEqual(roots.map((row) => row.id));
  });

  it("the 'store' source ignores expandedIds and never writes it", async () => {
    const { fixture, grid } = await setup({
      gridId: 'spec-tree-store-source',
      depth: 0,
      expanded: new Set([1]),
    });
    expect(await grid.getVisibleRowCount()).toBe(3); // the depth, not the model
    await grid.expand(0);
    expect(await grid.getVisibleRowCount()).toBe(5); // 1, 2, 3, 5, 6
    expect(fixture.componentInstance.emitted).toEqual([]);
  });

  it("the 'model' source ignores defaultExpandedDepth and leaves the store slice alone", async () => {
    const first = await setup({
      gridId: 'spec-tree-model-source',
      depth: 3,
      source: 'model',
      expanded: new Set([1]),
    });
    // Only Root A expanded: 1, 2, 3, 5, 6 (4 sits under the collapsed 2).
    expect(await first.grid.getVisibleRowCount()).toBe(5);
    await first.grid.collapse(0);
    expect(await first.grid.getVisibleRowCount()).toBe(3);
    first.fixture.destroy();

    // The same grid and content in the 'store' source: no slice was
    // written, so the default depth (fully expanded) seeds.
    const second = TestBed.createComponent(DataHost);
    second.componentInstance.gridId.set('spec-tree-model-source');
    await stable(second);
    const grid = await TestbedHarnessEnvironment.loader(second).getHarness(TmTreeGridHarness);
    expect(await grid.getVisibleRowCount()).toBe(6);
  });

  it('a collapse removes that row only; an expand brings back what it remembered', async () => {
    const { fixture, grid } = await setup({
      gridId: 'spec-tree-model-gestures',
      source: 'model',
      expanded: new Set([1, 2]),
    });
    const host = fixture.componentInstance;
    expect(await grid.getVisibleRowCount()).toBe(6);

    await grid.collapse(0);
    expect([...host.emitted.at(-1)!]).toEqual([2]);
    expect(await grid.getVisibleRowCount()).toBe(3);

    await grid.expand(0);
    expect([...host.emitted.at(-1)!].sort()).toEqual([1, 2]);
    expect(await grid.getVisibleRowCount()).toBe(6);
    // Every emission is a fresh set.
    expect(host.emitted[0]).not.toBe(host.emitted[1]);
  });

  it('a consumer write after the bind applies like a restore and is not echoed', async () => {
    const { fixture, grid } = await setup({ gridId: 'spec-tree-model-write', source: 'model' });
    const host = fixture.componentInstance;
    expect(await grid.getVisibleRowCount()).toBe(3);

    // Rows it adds expand, and the lazy one among them loads.
    host.expanded.set(new Set([1, 6]));
    await stable(fixture);
    expect(await grid.getVisibleRowCount()).toBe(5);
    expect(host.batches).toEqual([[6]]);

    // Rows it leaves out collapse; 2 is kept under its collapsed parent.
    host.expanded.set(new Set([2]));
    await stable(fixture);
    expect(await grid.getVisibleRowCount()).toBe(3);
    expect(host.emitted).toEqual([]);

    await grid.expand(0);
    expect(await grid.getVisibleRowCount()).toBe(6); // 2 was remembered
  });

  it('a failed batch drops its rows from the bound set and announces once', async () => {
    const { fixture } = await setup({
      gridId: 'spec-tree-model-fail',
      source: 'model',
      lazyIds: new Set([5, 6]),
      expanded: new Set([1, 5, 6]),
    });
    const host = fixture.componentInstance;
    const announce = vi.spyOn(TestBed.inject(LiveAnnouncer), 'announce');
    expect(host.batches).toEqual([[5, 6]]);

    host.settle([], new Error('offline'));
    await stable(fixture);
    expect([...host.expanded()]).toEqual([1]);
    const failures = announce.mock.calls.filter(
      (call) => String(call[0]) === 'Could not load child rows',
    );
    expect(failures.length).toBe(1);
  });

  it('scrollToRow expands the ancestors into the bound set and activates the row', async () => {
    const { fixture, grid } = await setup({ gridId: 'spec-tree-scroll', source: 'model' });
    const host = fixture.componentInstance;
    const tree = fixture.debugElement.query(By.directive(TmTreeGrid))
      .componentInstance as TmTreeGrid<Node>;

    expect(tree.scrollToRow(4)).toBe(true);
    await stable(fixture);
    expect([...host.expanded()].sort()).toEqual([1, 2]);
    expect(host.emitted.length).toBe(1);
    expect(await grid.getActiveRowIndex()).toBe(2);
    expect(await grid.getCellText(2, 0)).toBe('Grand A1a');

    // A lazy child that has not loaded is not in the data yet.
    expect(tree.scrollToRow(7)).toBe(false);
  });
});

/** A collapsed tree list with a two-way active row, row classes and no row numbers. */
@Component({
  imports: [TmTreeGrid, TmGridColumn],
  template: `
    <tm-tree-grid
      gridId="spec-tree-list"
      [data]="rows"
      [rowId]="rowId"
      [parentId]="parentId"
      [defaultExpandedDepth]="0"
      rowActivation="row"
      [rowHeaders]="false"
      [rowClass]="rowClass"
      [(activeRowId)]="active"
      style="block-size: 300px"
    >
      <tm-grid-column key="name" header="Name" [width]="200" />
    </tm-tree-grid>
  `,
})
class ListTreeHost {
  readonly rows = makeNodes();
  readonly active = signal<number | string | null>(null);
  readonly rowId = (row: Node): number => row.id;
  readonly parentId = (row: Node): number | null => row.parentId;
  readonly rowClass = (row: Node): string | null =>
    row.parentId === null ? 'tm-grid__row--muted' : null;
}

describe('tm-tree-grid list-page members', () => {
  it('expands the ancestors of a consumer-written active row and activates it', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(ListTreeHost);
    await stable(fixture);
    const grid = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmTreeGridHarness);
    const scroller = (fixture.nativeElement as HTMLElement).querySelector(
      '.tm-grid__scroller',
    ) as HTMLElement;
    // Collapsed to the roots; no row numbers, and the roots carry the class.
    expect(await grid.getVisibleRowCount()).toBe(3);
    expect(scroller.querySelector('[data-tm-rowhdr]')).toBeNull();
    expect(scroller.querySelector('.tm-grid__row')!.classList).toContain('tm-grid__row--muted');

    // A grandchild under two collapsed ancestors: both expand, and it is active.
    fixture.componentInstance.active.set(4);
    await stable(fixture);
    expect(await grid.getVisibleRowCount()).toBe(6);
    expect(await grid.getActiveRowIndex()).toBe(2);
    expect(await grid.getCellText(2, 0)).toBe('Grand A1a');
    expect(fixture.componentInstance.active()).toBe(4);
    expect(scroller.querySelector('.tm-grid__row--active')!.getAttribute('aria-rowindex')).toBe(
      '4',
    );
  });
});
