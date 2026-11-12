// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import {
  TmGrid,
  TmGridColumn,
  TmGridDisplayDef,
  TmGridDriver,
  type TmGridRowActivation,
  type TmGridSort,
} from '@tellma/core-ui/grid';
import { TmInput } from '@tellma/core-ui/input';

import { makeRow, type DemoRow } from './grid-readonly-story';

/** Rows per page of the paged source. */
const PAGE_SIZE = 50;

/** Rows in the whole source. */
const SOURCE_SIZE = 230;

/** The id of the synthetic totals row. */
const TOTALS_ID = -1;

/**
 * The search-page shape: a search field driving a readonly list that opens
 * rows. The field moves the active row (ArrowUp/Down, PageUp/Down) and opens
 * it (Enter, Mod+Enter) while keeping focus; a click, a middle click or a tap
 * on a row opens it too, while the Name cell stays a real link with the
 * browser's own menu. Sortable headers edit the two-way sort and this host
 * orders the rows; the source is paged, so checked rows survive paging and
 * select-all works on the current page only. Rows of inactive records render
 * muted, an optional totals row is emphasized, and the list carries no row
 * numbers. The toolbar readouts mirror the
 * grid's models for the Playwright battery.
 */
@Component({
  imports: [TmGrid, TmGridColumn, TmGridDisplayDef, TmGridDriver, TmInput],
  template: `
    <h2>Grid (search page)</h2>

    <div class="toolbar">
      <input
        tmInput
        type="search"
        class="search"
        data-testid="search"
        aria-label="Search items"
        placeholder="Search items"
        [tmGridDriver]="grid"
        [value]="query()"
        (input)="onQuery($event)"
      />
      <button type="button" data-testid="page-prev" [disabled]="page() === 0" (click)="turn(-1)">
        Previous page
      </button>
      <span data-testid="page">Page {{ page() + 1 }} of {{ pageCount() }}</span>
      <button
        type="button"
        data-testid="page-next"
        [disabled]="page() + 1 >= pageCount()"
        (click)="turn(1)"
      >
        Next page
      </button>
      <button type="button" data-testid="toggle-rows" (click)="loaded.set(!loaded())">
        {{ loaded() ? 'Clear rows' : 'Load rows' }}
      </button>
      <button type="button" data-testid="toggle-totals" (click)="totals.set(!totals())">
        {{ totals() ? 'Hide totals' : 'Show totals' }}
      </button>
      <!-- A consumer write to the active row: the grid scrolls, focus stays here. -->
      <button type="button" data-testid="jump" (click)="jump()">Jump to the 41st row</button>
    </div>

    <!-- Readouts of the grid's models and events. -->
    <dl class="readouts">
      <dt>Opened</dt>
      <dd data-testid="activated">{{ activatedText() }}</dd>
      <dt>Activations</dt>
      <dd data-testid="activation-count">{{ activationCount() }}</dd>
      <dt>Active row</dt>
      <dd data-testid="active-row">{{ activeRowId() ?? 'none' }}</dd>
      <dt>Sort</dt>
      <dd data-testid="order">{{ orderText() }}</dd>
      <dt>Checked</dt>
      <dd data-testid="selected-count">{{ selectedIds().size }} selected</dd>
      <dt>Link</dt>
      <dd data-testid="link-clicked">{{ linkClicked() ?? 'none' }}</dd>
    </dl>

    <tm-grid
      #grid
      class="demo-grid"
      gridId="grid-search-page"
      data-testid="grid-search-page"
      [data]="pageRows()"
      [rowId]="rowId"
      rowActivation="row"
      [rowHeaders]="false"
      [rowClass]="rowClass"
      selectable
      [(selectedIds)]="selectedIds"
      [(activeRowId)]="activeRowId"
      [(orderBy)]="orderBy"
      (rowActivate)="onActivate($event)"
    >
      <tm-grid-column key="code" header="Code" [width]="110" sortable />
      <tm-grid-column key="name" header="Name" [flex]="2" [minWidth]="160" sortable>
        <!-- A real link: open-in-new-tab and copy-link stay native. -->
        <a
          *tmGridDisplay="let value; rowId as id"
          class="item-link"
          [href]="'#/items/' + id"
          (click)="onLinkClick($event, id)"
          >{{ value }}</a
        >
      </tm-grid-column>
      <tm-grid-column key="qty" type="number" header="Qty" [width]="90" sortable />
      <tm-grid-column key="region" header="Region" [width]="110" sortable />
      <tm-grid-column key="manager" header="Manager" [flex]="1" [minWidth]="150" />
    </tm-grid>
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
      margin-block-end: 12px;
    }
    .search {
      inline-size: 240px;
    }
    .readouts {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 2px 12px;
      margin: 0 0 12px;
      font-size: 13px;
    }
    .readouts dd {
      margin: 0;
    }
    .demo-grid {
      display: block;
      block-size: 50vh;
    }
  `,
})
export class GridSearchPageStory {
  /** The search text. */
  readonly query = signal('');
  /** The zero-based page. */
  readonly page = signal(0);
  /** Whether the source has delivered its rows (cleared for the gutter test). */
  readonly loaded = signal(true);
  /** Whether the page ends in an emphasized totals row. */
  readonly totals = signal(false);
  /** The two-way checked ids, across pages. */
  readonly selectedIds = signal<ReadonlySet<string | number>>(new Set());
  /** The two-way active row. */
  readonly activeRowId = signal<TmRowId | null>(null);
  /** The two-way sort; this host orders the rows by it. */
  readonly orderBy = signal<TmGridSort | null>(null);
  /** The last opened row. */
  readonly lastActivation = signal<TmGridRowActivation<DemoRow> | null>(null);
  /** How many rows were opened. */
  readonly activationCount = signal(0);
  /** The id of the last clicked Name link. */
  readonly linkClicked = signal<TmRowId | null>(null);

  /** The matching rows of the whole source, ordered by the sort. */
  private readonly matches = computed<readonly DemoRow[]>(() => {
    if (!this.loaded()) {
      return [];
    }
    const query = this.query().trim().toLowerCase();
    const rows = Array.from({ length: SOURCE_SIZE }, (_, i) => makeRow(i)).filter(
      (row) => query === '' || row.name.toLowerCase().includes(query),
    );
    const sort = this.orderBy();
    if (sort === null) {
      return rows;
    }
    const key = sort.key as keyof DemoRow;
    const sign = sort.direction === 'asc' ? 1 : -1;
    return [...rows].sort((a, b) => {
      const x = a[key];
      const y = b[key];
      const order =
        typeof x === 'number' && typeof y === 'number' ? x - y : String(x).localeCompare(String(y));
      return sign * order;
    });
  });

  /** The page count (at least one). */
  readonly pageCount = computed(() => Math.max(1, Math.ceil(this.matches().length / PAGE_SIZE)));

  /** The current page's rows (and the totals row, when shown): what the grid binds. */
  readonly pageRows = computed<readonly DemoRow[]>(() => {
    const rows = this.matches().slice(this.page() * PAGE_SIZE, (this.page() + 1) * PAGE_SIZE);
    if (!this.totals() || rows.length === 0) {
      return rows;
    }
    const total: DemoRow = {
      ...makeRow(0),
      id: TOTALS_ID,
      code: 'Total',
      name: `${rows.length} items`,
      qty: rows.reduce((sum, row) => sum + row.qty, 0),
      active: true,
      region: '',
      manager: '',
    };
    return [...rows, total];
  });

  /** The last activation, as the readout shows it. */
  readonly activatedText = computed(() => {
    const activation = this.lastActivation();
    return activation === null
      ? 'none'
      : `${activation.rowId} via ${activation.via}${activation.newTab ? ' in a new tab' : ''}`;
  });

  /** The sort, as the readout shows it. */
  readonly orderText = computed(() => {
    const sort = this.orderBy();
    return sort === null ? 'none' : `${sort.key} ${sort.direction}`;
  });

  /** Row identity. */
  readonly rowId = (row: DemoRow): number => row.id;

  /** Inactive records render muted; the totals row is emphasized. */
  readonly rowClass = (row: DemoRow): string | null =>
    row.id === TOTALS_ID ? 'tm-grid__row--emphasis' : row.active ? null : 'tm-grid__row--muted';

  /** Re-runs the search from the first page. */
  onQuery(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.page.set(0);
  }

  /** Turns the page. */
  turn(by: number): void {
    this.page.update((page) => Math.min(this.pageCount() - 1, Math.max(0, page + by)));
  }

  /** Makes the page's 41st row the active one, as a page seeding its results would. */
  jump(): void {
    const row = this.pageRows()[40];
    if (row !== undefined) {
      this.activeRowId.set(row.id);
    }
  }

  /** Records an opened row (a real page would navigate). */
  onActivate(activation: TmGridRowActivation<DemoRow>): void {
    this.lastActivation.set(activation);
    this.activationCount.update((count) => count + 1);
  }

  /** Records a followed Name link instead of navigating away from the story. */
  onLinkClick(event: MouseEvent, id: TmRowId): void {
    event.preventDefault();
    this.linkClicked.set(id);
  }
}
