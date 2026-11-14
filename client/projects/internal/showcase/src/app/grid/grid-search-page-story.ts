// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';
import { Columns3, Funnel } from 'lucide';

import { TmCheckbox } from '@tellma/core-ui/checkbox';
import type { TmRowId } from '@tellma/core-ui/contracts';
import {
  TmGrid,
  TmGridColumn,
  TmGridDisplayDef,
  TmGridDriver,
  TmGridEmptyDef,
  TmGridHeaderAction,
  TmGridHeaderEndDef,
  TmGridRowEndHeaderDef,
  type TmGridRowActivation,
  type TmGridSort,
} from '@tellma/core-ui/grid';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmInput } from '@tellma/core-ui/input';
import { TmPopover, TmPopoverContent, TmPopoverTrigger } from '@tellma/core-ui/popover';

import { makeRow, type DemoRow } from './grid-readonly-story';

/** Rows per page of the paged source. */
const PAGE_SIZE = 50;

/** Rows in the whole source. */
const SOURCE_SIZE = 230;

/** The id of the synthetic totals row. */
const TOTALS_ID = -1;

/** The regions the Region filter offers. */
const REGIONS = ['North', 'South', 'East', 'West', 'Central'] as const;

/** The columns the column editor shows and hides. */
type ToggleableColumn = 'qty' | 'region' | 'manager';

/**
 * The search-page shape: a search field driving a readonly list that opens
 * rows. The field moves the active row (ArrowUp/Down, PageUp/Down) and opens
 * it (Enter, Mod+Enter) while keeping focus; a click, a middle click or a tap
 * on a row opens it too, while the Name cell stays a real link with the
 * browser's own menu. Sortable headers edit the two-way sort and this host
 * orders the rows; the source is paged, so checked rows survive paging and
 * select-all works on the current page only. Rows of inactive records render
 * muted, an optional totals row is emphasized, and the list carries no row
 * numbers. Header actions open filter popovers — Region is sortable and
 * filterable, Manager filter-only — and the row-end header holds the column
 * editor; a filter matching nothing shows the empty template under the
 * headers that undo it. The toolbar readouts mirror the grid's models for
 * the Playwright battery.
 */
@Component({
  imports: [
    TmCheckbox,
    TmGrid,
    TmGridColumn,
    TmGridDisplayDef,
    TmGridDriver,
    TmGridEmptyDef,
    TmGridHeaderAction,
    TmGridHeaderEndDef,
    TmGridRowEndHeaderDef,
    TmIcon,
    TmInput,
    TmPopover,
    TmPopoverContent,
    TmPopoverTrigger,
  ],
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
      <dt>Filters</dt>
      <dd data-testid="filters">{{ filtersText() }}</dd>
      <dt>Columns</dt>
      <dd data-testid="columns">{{ columnsText() }}</dd>
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
      @if (shown().has('qty')) {
        <tm-grid-column key="qty" type="number" header="Qty" [width]="90" sortable />
      }
      @if (shown().has('region')) {
        <tm-grid-column key="region" header="Region" [width]="150" sortable>
          <!-- Sortable and filterable: Enter sorts, Alt+ArrowDown filters. -->
          <ng-template tmGridHeaderEnd>
            <button
              tmGridHeaderAction
              data-testid="region-filter"
              aria-label="Filter Region"
              [active]="regionFilter() !== ''"
              [tmPopoverTriggerFor]="regionPopover"
            >
              <tm-icon [svg]="funnelIcon" />
            </button>
            <tm-popover #regionPopover aria-label="Filter Region">
              <ng-template tmPopoverContent>
                <label class="filter-field">
                  Region
                  <select
                    data-testid="region-filter-select"
                    [value]="regionFilter()"
                    (change)="onRegionFilter($event)"
                  >
                    <option value="">Any region</option>
                    @for (region of regions; track region) {
                      <option [value]="region">{{ region }}</option>
                    }
                  </select>
                </label>
              </ng-template>
            </tm-popover>
          </ng-template>
        </tm-grid-column>
      }
      @if (shown().has('manager')) {
        <tm-grid-column key="manager" header="Manager" [flex]="1" [minWidth]="170">
          <!-- Filter-only: the header's Enter opens the filter. -->
          <ng-template tmGridHeaderEnd>
            <button
              tmGridHeaderAction
              data-testid="manager-filter"
              aria-label="Filter Manager"
              [active]="managerFilter() !== ''"
              [tmPopoverTriggerFor]="managerPopover"
            >
              <tm-icon [svg]="funnelIcon" />
            </button>
            <tm-popover #managerPopover aria-label="Filter Manager">
              <ng-template tmPopoverContent>
                <label class="filter-field">
                  Manager contains
                  <input
                    tmInput
                    type="search"
                    data-testid="manager-filter-input"
                    [value]="managerFilter()"
                    (input)="onManagerFilter($event)"
                  />
                </label>
              </ng-template>
            </tm-popover>
          </ng-template>
        </tm-grid-column>
      }

      <!-- The row-end column: the list's column editor. -->
      <ng-template tmGridRowEndHeader>
        <button
          tmGridHeaderAction
          data-testid="column-editor"
          aria-label="Choose columns"
          [active]="shown().size < toggleable.length"
          [tmPopoverTriggerFor]="columnsPopover"
        >
          <tm-icon [svg]="columnsIcon" />
        </button>
        <tm-popover #columnsPopover aria-label="Choose columns">
          <ng-template tmPopoverContent>
            <div class="column-list">
              @for (column of toggleable; track column.key) {
                <tm-checkbox
                  [attr.data-testid]="'column-' + column.key"
                  [checked]="shown().has(column.key)"
                  (checkedChange)="toggleColumn(column.key, $event)"
                  >{{ column.label }}</tm-checkbox
                >
              }
            </div>
          </ng-template>
        </tm-popover>
      </ng-template>

      <!-- The empty state keeps the headers, so the filters that emptied
           the list stay in reach. -->
      <div *tmGridEmpty class="empty" data-testid="empty-state">
        <span>No items match.</span>
        <button type="button" data-testid="clear-filters" (click)="clearFilters()">
          Clear filters
        </button>
      </div>
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
    /* Two readouts a line, so the grid's lower rows stay above the fold. */
    .readouts {
      display: grid;
      grid-template-columns: repeat(2, max-content 1fr);
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
    .filter-field,
    .column-list {
      display: grid;
      gap: 4px;
    }
    .empty {
      display: flex;
      gap: 12px;
      align-items: center;
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
  /** The Region filter: the one region shown, or `''` for any. */
  readonly regionFilter = signal('');
  /** The Manager filter: text the manager's name contains, or `''`. */
  readonly managerFilter = signal('');
  /** The columns the column editor shows. */
  readonly shown = signal<ReadonlySet<ToggleableColumn>>(new Set(['qty', 'region', 'manager']));

  /** The regions the Region filter offers. */
  protected readonly regions = REGIONS;
  /** The columns the column editor lists, in grid order. */
  protected readonly toggleable: readonly { key: ToggleableColumn; label: string }[] = [
    { key: 'qty', label: 'Qty' },
    { key: 'region', label: 'Region' },
    { key: 'manager', label: 'Manager' },
  ];
  /** The filter actions' glyph. */
  protected readonly funnelIcon = Funnel;
  /** The column editor's glyph. */
  protected readonly columnsIcon = Columns3;

  /** The matching rows of the whole source, ordered by the sort. */
  private readonly matches = computed<readonly DemoRow[]>(() => {
    if (!this.loaded()) {
      return [];
    }
    const query = this.query().trim().toLowerCase();
    const region = this.regionFilter();
    const manager = this.managerFilter().trim().toLowerCase();
    const rows = Array.from({ length: SOURCE_SIZE }, (_, i) => makeRow(i)).filter(
      (row) =>
        (query === '' || row.name.toLowerCase().includes(query)) &&
        (region === '' || row.region === region) &&
        (manager === '' || row.manager.toLowerCase().includes(manager)),
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

  /** The filters, as the readout shows them. */
  readonly filtersText = computed(() => {
    const parts = [
      this.regionFilter() === '' ? null : `region=${this.regionFilter()}`,
      this.managerFilter() === '' ? null : `manager~${this.managerFilter()}`,
    ].filter((part) => part !== null);
    return parts.length === 0 ? 'none' : parts.join(', ');
  });

  /** The shown toggleable columns, as the readout shows them. */
  readonly columnsText = computed(() =>
    this.toggleable
      .filter((column) => this.shown().has(column.key))
      .map((column) => column.key)
      .join(', '),
  );

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

  /** Applies the Region filter from the first page. */
  onRegionFilter(event: Event): void {
    this.regionFilter.set((event.target as HTMLSelectElement).value);
    this.page.set(0);
  }

  /** Applies the Manager filter from the first page. */
  onManagerFilter(event: Event): void {
    this.managerFilter.set((event.target as HTMLInputElement).value);
    this.page.set(0);
  }

  /** Clears both header filters. */
  clearFilters(): void {
    this.regionFilter.set('');
    this.managerFilter.set('');
    this.page.set(0);
  }

  /** Shows or hides a column from the column editor. */
  toggleColumn(key: ToggleableColumn, shown: boolean): void {
    const next = new Set(this.shown());
    if (shown) {
      next.add(key);
    } else {
      next.delete(key);
    }
    this.shown.set(next);
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
