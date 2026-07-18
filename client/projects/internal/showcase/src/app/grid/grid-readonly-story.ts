// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import { TmGrid, TmGridColumn, TmGridDisplayDef } from '@tellma/core-ui/grid';

import { mulberry32 } from './seeded-random';

interface Status {
  readonly value: number;
  readonly label: string;
}

const STATUSES: readonly Status[] = [
  { value: 0, label: 'Draft' },
  { value: 1, label: 'Active' },
  { value: 2, label: 'On hold' },
  { value: 3, label: 'Closed' },
];

const REGIONS = ['North', 'South', 'East', 'West', 'Central'] as const;
const FIRST_NAMES = ['Ada', 'Grace', 'Alan', 'Edsger', 'Barbara', 'Donald', 'Radia', 'Vint'] as const;
const LAST_NAMES = ['Lovelace', 'Hopper', 'Turing', 'Dijkstra', 'Liskov', 'Knuth', 'Perlman', 'Cerf'] as const;

export interface DemoRow {
  readonly id: number;
  readonly code: string;
  readonly name: string;
  readonly qty: number;
  readonly price: number;
  readonly active: boolean;
  readonly status: number;
  readonly region: string;
  readonly manager: string;
  readonly score: number;
  readonly flagged: boolean;
  readonly note: string;
}

/**
 * Row i is a pure function of i (own PRNG stream per row), so values stay
 * identical — and assertable — whether 1k or 100k rows are generated, and
 * every text cell embeds the row index. (Shared with the list-screen story.)
 */
export function makeRow(i: number): DemoRow {
  const random = mulberry32(0x9e3779b9 ^ i);
  const pick = <T>(values: readonly T[]): T => values[Math.floor(random() * values.length)];
  return {
    id: i,
    code: `PRD-${i}`,
    name: `Item ${i} ${pick(REGIONS)}`,
    qty: 1 + Math.floor(random() * 999),
    price: Math.round(random() * 90000) / 100,
    active: random() < 0.5,
    status: Math.floor(random() * STATUSES.length),
    region: pick(REGIONS),
    manager: `${pick(FIRST_NAMES)} ${pick(LAST_NAMES)} ${i}`,
    score: Math.round(random() * 100),
    flagged: random() < 0.2,
    note: `Note ${i}: ${pick(['follow up', 'reviewed', 'pending audit', 'archived'])}`,
  };
}

/**
 * Readonly tm-grid demo host: 100k seeded rows × 12 columns across the
 * built-in column types plus an accessor column and a record-link cell.
 * The Playwright battery drives it via the data-testid hooks.
 */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridDisplayDef],
  template: `
    <h2>Grid (readonly)</h2>

    <div class="toolbar">
      <label>
        Rows
        <select data-testid="row-count" (change)="onRowCountChange($event)">
          <option value="1000">1,000</option>
          <option value="100000" selected>100,000</option>
        </select>
      </label>
    </div>

    <tm-grid
      class="demo-grid"
      gridId="grid-readonly"
      data-testid="grid-readonly"
      [data]="rows()"
      [rowId]="rowId"
    >
      <tm-grid-column key="code" header="Code" [width]="110">
        <!-- Same-document fragment jump built from the CURRENT URL: keeping
             the path + search string preserves the ?dir/?theme matrix params
             instead of dropping them. (A bare "#fragment" href would resolve
             against <base href="/"> and unload the story; a fixed "/story/…"
             href would drop the query string.) -->
        <a
          *tmGridDisplay="let value; let id = rowId"
          [attr.href]="recordHref(id)"
          >{{ value }}</a
        >
      </tm-grid-column>
      <tm-grid-column key="name" header="Name" [flex]="2" [minWidth]="150" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="90" />
      <tm-grid-column key="price" type="number" header="Price" [width]="100" />
      <tm-grid-column key="active" type="boolean" header="Active" [width]="80" />
      <tm-grid-column
        key="status"
        type="enum"
        header="Status"
        [options]="statuses"
        [optionLabel]="statusLabel"
        [optionValue]="statusValue"
        [width]="100"
      />
      <tm-grid-column key="region" header="Region" [width]="100" />
      <tm-grid-column key="manager" header="Manager" [flex]="1" [minWidth]="150" />
      <tm-grid-column key="score" type="number" header="Score" [width]="90" />
      <tm-grid-column key="flagged" type="boolean" header="Flagged" [width]="90" />
      <tm-grid-column type="number" header="Total" [value]="total" [width]="110" />
      <tm-grid-column key="note" header="Note" [flex]="2" [minWidth]="180" />
    </tm-grid>
  `,
  styles: `
    .toolbar {
      display: flex;
      gap: 12px;
      align-items: center;
      margin-block-end: 12px;
    }
    .demo-grid {
      display: block;
      block-size: 60vh;
    }
    /* Record links render in the brand teal link color. */
    .demo-grid a {
      color: var(--text-link);
    }
  `,
})
export class GridReadonlyStory {
  readonly rowCount = signal(100_000);
  readonly rows = computed<readonly DemoRow[]>(() =>
    Array.from({ length: this.rowCount() }, (_, i) => makeRow(i)),
  );

  readonly rowId = (row: DemoRow): number => row.id;
  readonly total = (row: DemoRow): number => Math.round(row.qty * row.price * 100) / 100;

  /**
   * Builds the record link's href from the current URL so the same-document
   * fragment jump preserves any query string (e.g. the ?dir/?theme matrix
   * params). Using the full path + search keeps a bare '#fragment' from
   * resolving against <base href="/"> and unloading the story, and the
   * fragment stays exactly '#record-{id}'.
   */
  readonly recordHref = (id: TmRowId): string =>
    `${location.pathname}${location.search}#record-${id}`;

  readonly statuses = STATUSES;
  readonly statusLabel = (status: Status): string => status.label;
  readonly statusValue = (status: Status): number => status.value;

  onRowCountChange(event: Event): void {
    this.rowCount.set(Number((event.target as HTMLSelectElement).value));
  }
}
