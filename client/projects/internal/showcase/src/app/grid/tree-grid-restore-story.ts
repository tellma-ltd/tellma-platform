// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal, viewChild } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import { TmGridColumn } from '@tellma/core-ui/grid';
import { TmTreeGrid } from '@tellma/core-ui/tree-grid';

import { HeldResponses, HeldResponsesControls } from '../held-responses';

/** A cost-centre node: a flat adjacency-list row. */
interface Unit {
  readonly id: number;
  readonly parentId: number | null;
  readonly name: string;
  /** Whether the node has children the "server" has not sent yet. */
  readonly lazy: boolean;
}

/** One unit row. */
function u(id: number, parentId: number | null, name: string, lazy = false): Unit {
  return { id, parentId, name, lazy };
}

/** The roots the screen fetches up front; every one loads its children lazily. */
const ROOTS: readonly Unit[] = [
  u(1, null, 'Operations', true),
  u(2, null, 'Finance', true),
  u(3, null, 'Archive', true),
];

/** The "server": the children each lazy unit returns. */
const CHILDREN: ReadonlyMap<number, readonly Unit[]> = new Map([
  [1, [u(11, 1, 'Plant', true), u(12, 1, 'Logistics', true)]],
  [11, [u(111, 11, 'Line A'), u(112, 11, 'Line B')]],
  [12, [u(121, 12, 'Fleet'), u(122, 12, 'Warehousing')]],
  [2, [u(21, 2, 'Treasury', true), u(22, 2, 'Payroll')]],
  [21, [u(211, 21, 'Cash pool'), u(212, 21, 'Hedging')]],
  [3, [u(31, 3, 'Archive 2019'), u(32, 3, 'Archive 2020')]],
]);

/**
 * The expansion the user left last time: two levels under Operations and
 * Finance, plus 404, a unit deleted since — the grid keeps it.
 */
const REMEMBERED: ReadonlySet<TmRowId> = new Set([1, 11, 2, 21, 404]);

/**
 * tm-tree-grid expansion-restore demo host: an all-lazy cost-centre tree
 * whose expansion the screen keeps itself (`expansionSource="model"`, as a
 * user-settings store would). Mounting restores it one level per
 * `loadChildren` call — the batch log lists each call's row ids — and the
 * Mounted toggle re-fetches the roots and restores again. `scrollToRow`
 * controls reveal a row by id, with an optional flash, without taking
 * focus. A held-response gate and a failure toggle exercise the lazy
 * loading states.
 */
@Component({
  imports: [TmTreeGrid, TmGridColumn, HeldResponsesControls],
  template: `
    <h2>Tree grid (expansion restore)</h2>

    <div class="toolbar">
      <label>
        <input
          type="checkbox"
          data-testid="toggle-mounted"
          [checked]="mounted()"
          (change)="onMountedChange($event)"
        />
        Mounted
      </label>
      <label>
        <input
          type="checkbox"
          data-testid="fail-toggle"
          [checked]="fail()"
          (change)="onFailChange($event)"
        />
        Fail loads
      </label>
      <!-- Holds child loads open, so the browser tests can assert the
           in-flight state without racing the delay. -->
      <app-held-responses [gate]="heldLoads" name="children" />
    </div>

    <!-- scrollToRow: a row id, an optional flash, and the call's result. -->
    <div class="toolbar">
      <label>
        Row id
        <input
          type="number"
          data-testid="scroll-row-id"
          [value]="scrollId()"
          (input)="onScrollIdChange($event)"
        />
      </label>
      <label>
        <input
          type="checkbox"
          data-testid="scroll-flash"
          [checked]="scrollFlash()"
          (change)="onScrollFlashChange($event)"
        />
        Flash
      </label>
      <button type="button" data-testid="scroll-go" (click)="scrollToRow()">Scroll to row</button>
      <label for="scroll-result-output">Result</label>
      <output id="scroll-result-output" data-testid="scroll-result">{{ scrollResult() }}</output>
    </div>

    @if (mounted()) {
      <tm-tree-grid
        class="demo-grid"
        gridId="tree-grid-restore"
        data-testid="tree-grid"
        [data]="units()"
        [rowId]="unitId"
        [parentId]="unitParentId"
        [hasChildren]="hasChildren"
        [loadChildren]="loadChildren"
        expansionSource="model"
        [(expandedIds)]="remembered"
      >
        <tm-grid-column key="name" header="Cost centre" [flex]="2" [minWidth]="220" />
        <tm-grid-column key="id" type="number" header="Id" [width]="90" />
      </tm-tree-grid>
    }

    <h3>Remembered expansion</h3>
    <output class="readout" data-testid="remembered">{{ rememberedText() }}</output>

    <h3>loadChildren calls</h3>
    <ol class="readout" data-testid="batch-log">
      @for (batch of batches(); track $index) {
        <li>{{ batch }}</li>
      }
    </ol>
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: center;
      margin-block-end: 12px;
    }
    /* Token colors on the native controls: WebKit's UA form styling under
       a dark color-scheme fails the axe contrast floor otherwise. */
    .toolbar button,
    .toolbar input[type='number'] {
      font: inherit;
      color: var(--text-body);
      background: var(--surface-card);
      border: 1px solid var(--border-default);
      border-radius: var(--radius-sm);
      padding-block: 2px;
      padding-inline: 6px;
    }
    .toolbar input[type='number'] {
      inline-size: 80px;
    }
    .demo-grid {
      display: block;
      block-size: 360px;
    }
    .readout {
      display: block;
      font-size: 12px;
    }
  `,
})
export class TreeGridRestoreStory {
  /** The grid, while mounted. */
  private readonly grid = viewChild<TmTreeGrid<Unit>>(TmTreeGrid);
  /** The rows the screen holds: the roots, plus every child loaded so far. */
  readonly units = signal<readonly Unit[]>(ROOTS);
  /** The kept expansion, two-way with the grid. */
  readonly remembered = signal<ReadonlySet<TmRowId>>(REMEMBERED);
  /** Each `loadChildren` call's row ids, in call order. */
  readonly batches = signal<readonly string[]>([]);
  readonly mounted = signal(true);
  readonly fail = signal(false);
  /** The latency gate of the child loader (see {@link HeldResponses}). */
  readonly heldLoads = new HeldResponses();

  readonly scrollId = signal(212);
  readonly scrollFlash = signal(true);
  /** What the last `scrollToRow` returned, or empty before the first. */
  readonly scrollResult = signal('');

  /** The remembered ids, ascending, for the readout. */
  readonly rememberedText = computed(() =>
    [...this.remembered()]
      .map(Number)
      .sort((a, b) => a - b)
      .join(', '),
  );

  readonly unitId = (unit: Unit): number => unit.id;
  readonly unitParentId = (unit: Unit): number | null => unit.parentId;
  readonly hasChildren = (unit: Unit): boolean => unit.lazy;

  /**
   * The child loader: logs the batch, waits 300 ms (or, while the gate
   * holds, for its release), rejects while the failure toggle is on, and
   * otherwise appends every batch row's children it has not sent yet.
   */
  readonly loadChildren = async (units: readonly Unit[]): Promise<void> => {
    this.batches.update((log) => [...log, units.map((unit) => unit.id).join(', ')]);
    await this.heldLoads.wait(300);
    if (this.fail()) {
      throw new Error('child load failed (story toggle)');
    }
    const present = new Set(this.units().map((unit) => unit.id));
    const fetched = units
      .flatMap((unit) => CHILDREN.get(unit.id) ?? [])
      .filter((child) => !present.has(child.id));
    this.units.update((rows) => [...rows, ...fetched]);
  };

  /** Calls `scrollToRow` with the toolbar's id and flash, and shows the result. */
  scrollToRow(): void {
    const found = this.grid()?.scrollToRow(this.scrollId(), { flash: this.scrollFlash() }) ?? false;
    this.scrollResult.set(String(found));
  }

  /**
   * Unmounting drops the loaded children, as leaving the screen would; the
   * remount fetches the roots again and restores the remembered expansion.
   */
  onMountedChange(event: Event): void {
    const mounted = (event.target as HTMLInputElement).checked;
    if (mounted) {
      this.units.set(ROOTS);
      this.batches.set([]);
    }
    this.mounted.set(mounted);
  }

  onFailChange(event: Event): void {
    this.fail.set((event.target as HTMLInputElement).checked);
  }

  onScrollIdChange(event: Event): void {
    this.scrollId.set(Number((event.target as HTMLInputElement).value));
  }

  onScrollFlashChange(event: Event): void {
    this.scrollFlash.set((event.target as HTMLInputElement).checked);
  }
}
