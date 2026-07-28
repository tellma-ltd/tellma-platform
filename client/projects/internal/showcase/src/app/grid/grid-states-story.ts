// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmGrid, TmGridColumn } from '@tellma/core-ui/grid';

import { mulberry32 } from './seeded-random';

interface StateRow {
  readonly id: number;
  readonly name: string;
  readonly qty: number;
  readonly active: boolean;
  readonly note: string;
}

const ROW_COUNT = 50;

function makeRows(): readonly StateRow[] {
  return Array.from({ length: ROW_COUNT }, (_, i) => ({
    id: i,
    name: `Row ${i}`,
    qty: 100 + i,
    active: i % 2 === 0,
    note: `note ${i} v0`,
  }));
}

/**
 * Grid lifecycle/identity demo host: loading and empty overlays, in-place
 * row refreshes (same ids, new object identities), row removal, a
 * deterministic shuffle, contentKey switches, and full unmount/remount —
 * the transitions the Playwright battery asserts state memory across.
 */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <h2>Grid states</h2>

    <div class="toolbar">
      <button type="button" data-testid="set-loading" (click)="toggleLoading()">Toggle loading</button>
      <button type="button" data-testid="set-empty" (click)="setEmpty()">Set empty</button>
      <button type="button" data-testid="restore-data" (click)="restoreData()">Restore data</button>
      <button type="button" data-testid="refresh-rows" (click)="refreshRows()">Refresh rows</button>
      <button type="button" data-testid="remove-rows" (click)="removeMiddleRows()">Remove middle 10</button>
      <button type="button" data-testid="shuffle-rows" (click)="shuffleRows()">Shuffle rows</button>
      <button type="button" data-testid="switch-content" (click)="switchContent()">Switch content</button>
      <button type="button" data-testid="toggle-mount" (click)="toggleMount()">Mount/unmount</button>
    </div>

    <p>Content key: <span data-testid="content-key-label">{{ contentKey() }}</span></p>

    @if (mounted()) {
      <tm-grid
        class="demo-grid"
        gridId="grid-states"
        data-testid="grid-states"
        [data]="rows()"
        [rowId]="rowId"
        [loading]="loading()"
        [contentKey]="contentKey()"
      >
        <tm-grid-column key="name" header="Name" [width]="140" />
        <tm-grid-column key="qty" type="number" header="Qty" [width]="90" />
        <tm-grid-column key="active" type="boolean" header="Active" [width]="80" />
        <tm-grid-column key="note" header="Note" [flex]="1" />
      </tm-grid>
    }
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      margin-block-end: 12px;
    }
    .demo-grid {
      display: block;
      block-size: 480px;
    }
  `,
})
export class GridStatesStory {
  private readonly baseRows = makeRows();
  private refreshGeneration = 0;

  readonly rows = signal<readonly StateRow[]>(this.baseRows);
  readonly loading = signal(false);
  readonly contentKey = signal<'A' | 'B'>('A');
  readonly mounted = signal(true);

  readonly rowId = (row: StateRow): number => row.id;

  toggleLoading(): void {
    this.loading.update((value) => !value);
  }

  setEmpty(): void {
    this.rows.set([]);
  }

  restoreData(): void {
    this.rows.set(this.baseRows);
  }

  /** Same row ids, new object identities, visibly changed values. */
  refreshRows(): void {
    this.refreshGeneration += 1;
    const generation = this.refreshGeneration;
    this.rows.update((rows) =>
      rows.map((row) => ({ ...row, qty: row.qty + 1000, note: `note ${row.id} v${generation}` })),
    );
  }

  /** Removes the middle 10 rows (the rows around the gap keep identity). */
  removeMiddleRows(): void {
    this.rows.update((rows) => {
      if (rows.length <= 10) {
        return rows;
      }
      const start = Math.floor((rows.length - 10) / 2);
      return [...rows.slice(0, start), ...rows.slice(start + 10)];
    });
  }

  /** Fisher–Yates with a fixed seed: the same reorder on every press. */
  shuffleRows(): void {
    this.rows.update((rows) => {
      const random = mulberry32(42);
      const next = [...rows];
      for (let i = next.length - 1; i > 0; i--) {
        const j = Math.floor(random() * (i + 1));
        [next[i], next[j]] = [next[j], next[i]];
      }
      return next;
    });
  }

  switchContent(): void {
    this.contentKey.update((key) => (key === 'A' ? 'B' : 'A'));
  }

  toggleMount(): void {
    this.mounted.update((value) => !value);
  }
}
