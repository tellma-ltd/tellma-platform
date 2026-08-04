// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import { TmGrid, TmGridColumn } from '@tellma/core-ui/grid';

import { makeRow, type DemoRow } from './grid-readonly-story';

/**
 * The list-screen shape: a readonly grid with `selectable` (row checkbox
 * bulk selection into `selectedIds`) and `searchable` (the Mod+F find
 * bar). The seeded rows match the readonly story's, so text and formatted
 * numbers are assertable at any row count; the toolbar readout mirrors
 * `selectedIds` for the Playwright battery.
 */
@Component({
  imports: [TmGrid, TmGridColumn],
  template: `
    <h2>Grid (list screen)</h2>

    <div class="toolbar">
      <label>
        Rows
        <select data-testid="row-count" (change)="onRowCountChange($event)">
          <option value="1000" selected>1,000</option>
          <option value="100000">100,000</option>
        </select>
      </label>
      <span data-testid="selected-count">{{ selectedIds().size }} selected</span>
    </div>

    <tm-grid
      class="demo-grid"
      gridId="grid-list-screen"
      data-testid="grid-list-screen"
      [data]="rows()"
      [rowId]="rowId"
      selectable
      searchable
      [(selectedIds)]="selectedIds"
    >
      <tm-grid-column key="code" header="Code" [width]="110" />
      <tm-grid-column key="name" header="Name" [flex]="2" [minWidth]="150" />
      <tm-grid-column key="qty" type="number" header="Qty" [width]="90" />
      <tm-grid-column key="price" type="number" header="Price" [width]="100" />
      <tm-grid-column key="active" type="boolean" header="Active" [width]="80" />
      <tm-grid-column key="region" header="Region" [width]="100" />
      <tm-grid-column key="manager" header="Manager" [flex]="1" [minWidth]="150" />
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
  `,
})
export class GridListScreenStory {
  readonly rowCount = signal(1_000);
  readonly rows = computed<readonly DemoRow[]>(() =>
    Array.from({ length: this.rowCount() }, (_, i) => makeRow(i)),
  );
  readonly selectedIds = signal<ReadonlySet<string | number>>(new Set());

  readonly rowId = (row: DemoRow): number => row.id;

  onRowCountChange(event: Event): void {
    this.rowCount.set(Number((event.target as HTMLSelectElement).value));
  }
}
