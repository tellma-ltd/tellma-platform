// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmGrid, TmGridColumn } from '@tellma/core-ui/grid';

import { mulberry32 } from './seeded-random';

/** One demo row of the states grid. */
interface StateRow {
  /** The row id (the grid's `rowId`); both records use ids 0 to 49. */
  readonly id: number;
  /** The display name, `Row <id>`. */
  readonly name: string;
  /** A number that a refresh visibly bumps. */
  readonly qty: number;
  /** A boolean column value (true on even ids). */
  readonly active: boolean;
  /**
   * A note naming the row, the refresh generation that produced it and its
   * record, `note <id> v<generation> (<record>)`.
   */
  readonly note: string;
}

/** The two demo records the content switch toggles between (the contentKey). */
type RecordName = 'A' | 'B';

/** How many rows a record is seeded with. */
const ROW_COUNT = 50;

/** A row's note: names the row, its refresh generation and its record. */
function noteOf(id: number, generation: number, record: RecordName): string {
  return `note ${id} v${generation} (${record})`;
}

/**
 * A fresh seed for a record: a new rows array of new row objects on every
 * call. The records share ids, names and values; only the note's record
 * marker tells their rows apart.
 */
function makeRows(record: RecordName): readonly StateRow[] {
  return Array.from({ length: ROW_COUNT }, (_, i) => ({
    id: i,
    name: `Row ${i}`,
    qty: 100 + i,
    active: i % 2 === 0,
    note: noteOf(i, 0, record),
  }));
}

/**
 * Grid lifecycle/identity demo host: loading and empty overlays, in-place
 * row refreshes (same ids, new object identities), row removal, a
 * deterministic shuffle, contentKey switches, and full unmount/remount —
 * the transitions the Playwright battery asserts state memory across.
 *
 * Every contentKey change hands the grid a new rows array, as the grid's
 * contentKey contract requires: A and B are separate records, each owning
 * its seed, rows array and row objects (every note names its record), so
 * the switch binds the other record's own rows, each record keeps its own
 * edits, and Restore data brings back the bound record's own seed.
 */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <h2>Grid states</h2>

    <!-- Toolbar: each button drives one state transition. -->
    <div class="toolbar">
      <button type="button" data-testid="set-loading" (click)="toggleLoading()">
        Toggle loading
      </button>
      <button type="button" data-testid="set-empty" (click)="setEmpty()">Set empty</button>
      <button type="button" data-testid="restore-data" (click)="restoreData()">Restore data</button>
      <button type="button" data-testid="refresh-rows" (click)="refreshRows()">Refresh rows</button>
      <button type="button" data-testid="remove-rows" (click)="removeMiddleRows()">
        Remove middle 10
      </button>
      <button type="button" data-testid="shuffle-rows" (click)="shuffleRows()">Shuffle rows</button>
      <button type="button" data-testid="switch-content" (click)="switchContent()">
        Switch content
      </button>
      <button type="button" data-testid="toggle-mount" (click)="toggleMount()">
        Mount/unmount
      </button>
    </div>

    <p>
      Content key: <span data-testid="content-key-label">{{ contentKey() }}</span>
    </p>

    <!-- The grid under test, bound to the current record's rows. -->
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
  /**
   * Each record's own seed, which Restore data brings back. The two seeds
   * are separate arrays of separate row objects, each marked with its
   * record, so no record's rows are ever another record's.
   */
  private readonly seeds: Readonly<Record<RecordName, readonly StateRow[]>> = {
    A: makeRows('A'),
    B: makeRows('B'),
  };

  /**
   * The rows of the record that is not bound, kept while the other one is
   * (the bound record's rows live in {@link rows}). Empty until the first
   * switch: a record that was never bound starts from its seed.
   */
  private readonly parked = new Map<RecordName, readonly StateRow[]>();

  /** How many refreshes have run (shared by both records); it names the notes a refresh writes. */
  private refreshGeneration = 0;

  /** The bound record's rows, the grid's `data`. */
  readonly rows = signal<readonly StateRow[]>(this.seeds.A);
  /** Whether the grid shows its loading overlay. */
  readonly loading = signal(false);
  /** The bound record, which is also the grid's contentKey. */
  readonly contentKey = signal<RecordName>('A');
  /** Whether the grid is mounted. */
  readonly mounted = signal(true);

  /** Reads a row's id. */
  readonly rowId = (row: StateRow): number => row.id;

  /** Toggles the loading overlay. */
  toggleLoading(): void {
    this.loading.update((value) => !value);
  }

  /** Empties the bound record (a new, empty array), showing the empty overlay. */
  setEmpty(): void {
    this.rows.set([]);
  }

  /** Brings the bound record back to its own seed. */
  restoreData(): void {
    this.rows.set(this.seeds[this.contentKey()]);
  }

  /** Same row ids, new object identities, visibly changed values (the record marker stays). */
  refreshRows(): void {
    this.refreshGeneration += 1;
    const generation = this.refreshGeneration;
    const record = this.contentKey();
    this.rows.update((rows) =>
      rows.map((row) => ({
        ...row,
        qty: row.qty + 1000,
        note: noteOf(row.id, generation, record),
      })),
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

  /**
   * Toggles the bound record: parks the outgoing record's rows and binds
   * the incoming record's own (its seed on its first bind), in the same
   * change as the key, so a new key never arrives over the outgoing
   * record's array.
   */
  switchContent(): void {
    const outgoing = this.contentKey();
    const incoming: RecordName = outgoing === 'A' ? 'B' : 'A';
    const rows = this.parked.get(incoming) ?? this.seeds[incoming];
    this.parked.delete(incoming);
    this.parked.set(outgoing, this.rows());
    this.contentKey.set(incoming);
    this.rows.set(rows);
  }

  /** Destroys or recreates the grid. */
  toggleMount(): void {
    this.mounted.update((value) => !value);
  }
}
