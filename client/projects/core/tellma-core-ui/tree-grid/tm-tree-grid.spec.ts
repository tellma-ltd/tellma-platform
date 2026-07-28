// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { applyEach, form, required } from '@angular/forms/signals';

import { provideTellmaUi } from '@tellma/core-ui';
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
  readonly rowId = (row: Node): number => row.id;
  readonly parentId = (row: Node): number | null => row.parentId;
  readonly hasChildren = (row: Node): boolean => row.id === 6;

  /** Settle hooks of the CURRENT in-flight lazy load (one at a time). */
  resolveLoad: (() => void) | null = null;
  rejectLoad: ((reason: Error) => void) | null = null;
  loadCalls = 0;
  readonly loadChildren = (): Promise<void> =>
    new Promise<void>((resolve, reject) => {
      this.loadCalls += 1;
      this.resolveLoad = resolve;
      this.rejectLoad = reject;
    });
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

async function setup(): Promise<{
  fixture: ComponentFixture<DataHost>;
  grid: TmTreeGridHarness;
  scroller: HTMLElement;
}> {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(DataHost);
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

  it('lazy loading: spinner in the reserved slot, expansion once the load resolves', async () => {
    const { fixture, grid } = await setup();
    const host = fixture.componentInstance;

    // The lazy root is expandable BEFORE any child exists (hasChildren).
    expect(await grid.isExpanded(5)).toBe(false);
    await grid.expand(5);
    expect(host.loadCalls).toBe(1);
    expect(await grid.isLoadingChildren(5)).toBe(true);
    expect(await grid.isExpanded(5)).toBe(false); // not expanded while loading
    expect(await grid.getVisibleRowCount()).toBe(6);

    // The consumer appends the children, then the promise resolves.
    host.rows.update((rows) => [...rows, node(7, 6, 'Lazy child'), node(8, 6, 'Lazy child 2')]);
    await stable(fixture);
    host.resolveLoad!();
    await stable(fixture);

    expect(await grid.isLoadingChildren(5)).toBe(false);
    expect(await grid.isExpanded(5)).toBe(true);
    expect(await grid.getVisibleRowCount()).toBe(8);
    expect(await grid.getCellText(6, 0)).toBe('Lazy child');
    expect(await grid.getLevel(6)).toBe(2);
  });

  it('a rejected lazy load restores the collapsed state', async () => {
    const { fixture, grid } = await setup();
    const host = fixture.componentInstance;

    await grid.expand(5);
    expect(await grid.isLoadingChildren(5)).toBe(true);
    host.rejectLoad!(new Error('nope'));
    await stable(fixture);

    expect(await grid.isLoadingChildren(5)).toBe(false);
    expect(await grid.isExpanded(5)).toBe(false);
    expect(await grid.getVisibleRowCount()).toBe(6);
  });

  it('re-collapsing during a lazy load wins: no expansion on resolve, instant expand after', async () => {
    const { fixture, grid } = await setup();
    const host = fixture.componentInstance;

    await grid.expand(5);
    expect(await grid.isLoadingChildren(5)).toBe(true);
    await grid.collapse(5); // while the load is in flight

    host.rows.update((rows) => [...rows, node(7, 6, 'Lazy child')]);
    await stable(fixture);
    host.resolveLoad!();
    await stable(fixture);

    // The load landed, but the node honors the user's collapse.
    expect(await grid.isExpanded(5)).toBe(false);
    expect(await grid.getVisibleRowCount()).toBe(6);

    // Children are loaded now — expanding again is synchronous, no new call.
    await grid.expand(5);
    expect(host.loadCalls).toBe(1);
    expect(await grid.isExpanded(5)).toBe(true);
    expect(await grid.getCellText(6, 0)).toBe('Lazy child');
  });

  it('defaultExpandedDepth seeds the expansion: 0 = collapsed, 1 = roots expanded', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });

    // Distinct gridIds: each grid seeds fresh (no shared remembered state).
    const collapsed = TestBed.createComponent(DataHost);
    collapsed.componentInstance.gridId.set('spec-tree-depth0');
    collapsed.componentInstance.depth.set(0);
    await stable(collapsed);
    const collapsedGrid = await TestbedHarnessEnvironment.loader(collapsed).getHarness(
      TmTreeGridHarness,
    );
    expect(await collapsedGrid.getVisibleRowCount()).toBe(3); // roots only
    expect(await collapsedGrid.isExpanded(0)).toBe(false);
    collapsed.destroy();

    const shallow = TestBed.createComponent(DataHost);
    shallow.componentInstance.gridId.set('spec-tree-depth1');
    shallow.componentInstance.depth.set(1);
    await stable(shallow);
    const shallowGrid = await TestbedHarnessEnvironment.loader(shallow).getHarness(
      TmTreeGridHarness,
    );
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
