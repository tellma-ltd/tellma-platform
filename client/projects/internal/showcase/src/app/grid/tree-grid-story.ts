// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';
import { applyEach, form, required } from '@angular/forms/signals';

import { TmGridColumn } from '@tellma/core-ui/grid';
import { TmTreeGrid } from '@tellma/core-ui/tree-grid';

/** A chart-of-accounts line: a flat adjacency-list row. */
interface Account {
  readonly id: number;
  readonly parentId: number | null;
  readonly name: string | null;
  readonly code: string | null;
  readonly balance: number | null;
  readonly active: boolean;
}

/** The lazy root: its children exist only after `loadChildren` resolves. */
const LAZY_ROOT_ID = 800;

/** One deterministic account row (code/balance/active derive from the id). */
function a(id: number, parentId: number | null, name: string): Account {
  return {
    id,
    parentId,
    name,
    code: String(id),
    balance: ((id * 7919) % 90000) / 10,
    active: id % 4 !== 0,
  };
}

/**
 * The seeded accounts tree (4 levels, authored in DFS order so view rows
 * mirror the array while fully expanded), with DELIBERATE anomalies at the
 * end: an orphan (parent id 777 resolves to nothing) and a two-node parent
 * cycle (910 ⇄ 911) — dev mode warns for each, and the grid renders the
 * orphan and the cycle-break row as roots instead of dropping them.
 */
const SEED_ACCOUNTS: readonly Account[] = [
  a(1, null, 'Assets'),
  a(10, 1, 'Current assets'),
  a(100, 10, 'Cash and equivalents'),
  a(1000, 100, 'Petty cash'),
  a(1001, 100, 'Bank – main'),
  a(1002, 100, 'Bank – payroll'),
  a(101, 10, 'Receivables'),
  a(1010, 101, 'Trade debtors'),
  a(1011, 101, 'Allowance for doubtful debts'),
  a(102, 10, 'Inventory'),
  a(1020, 102, 'Raw materials'),
  a(1021, 102, 'Finished goods'),
  a(1022, 102, 'Work in progress'),
  a(11, 1, 'Non-current assets'),
  a(110, 11, 'Property, plant and equipment'),
  a(1100, 110, 'Land'),
  a(1101, 110, 'Buildings'),
  a(1102, 110, 'Vehicles'),
  a(1103, 110, 'Machinery'),
  a(111, 11, 'Intangibles'),
  a(1110, 111, 'Software'),
  a(2, null, 'Liabilities'),
  a(20, 2, 'Current liabilities'),
  a(200, 20, 'Payables'),
  a(2000, 200, 'Trade creditors'),
  a(2001, 200, 'Accrued expenses'),
  a(201, 20, 'Taxes payable'),
  a(2010, 201, 'VAT payable'),
  a(2011, 201, 'Payroll taxes'),
  a(21, 2, 'Non-current liabilities'),
  a(210, 21, 'Long-term loans'),
  a(3, null, 'Equity'),
  a(30, 3, 'Share capital'),
  a(31, 3, 'Retained earnings'),
  a(4, null, 'Revenue'),
  a(40, 4, 'Sales revenue'),
  a(400, 40, 'Product sales'),
  a(401, 40, 'Service revenue'),
  a(402, 40, 'Subscriptions'),
  a(41, 4, 'Other income'),
  a(410, 41, 'Interest income'),
  a(5, null, 'Expenses'),
  a(50, 5, 'Cost of sales'),
  a(500, 50, 'Materials cost'),
  a(501, 50, 'Direct labour'),
  a(51, 5, 'Operating expenses'),
  a(510, 51, 'Salaries'),
  a(511, 51, 'Rent'),
  a(512, 51, 'Utilities'),
  a(513, 51, 'Depreciation'),
  a(514, 51, 'Marketing'),
  a(515, 51, 'Insurance'),
  a(52, 5, 'Finance costs'),
  a(520, 52, 'Interest expense'),
  a(LAZY_ROOT_ID, null, 'Loaded lazily'),
  a(900, 777, 'Orphan account'), // parent 777 does not exist → renders as a root
  a(910, 911, 'Cycle account A'), // 910 ⇄ 911: the cycle breaks at 910 (root)
  a(911, 910, 'Cycle account B'),
];

/** The rows `loadChildren` appends under the lazy root. */
const LAZY_CHILDREN: readonly Account[] = [
  a(8001, LAZY_ROOT_ID, 'Lazy child one'),
  a(8002, LAZY_ROOT_ID, 'Lazy child two'),
  a(8003, LAZY_ROOT_ID, 'Lazy child three'),
];

/**
 * Editable tm-tree-grid demo host: a ~4-level accounts tree over a Signal
 * Forms field tree (name required), the new-row placeholder plus
 * insert-child through `newRow(parent)`, lazy loading behind a
 * configurable delay and failure toggle, the readonly flip, a
 * default-depth select, contentKey switching and unmount/remount buttons
 * to exercise expansion-state persistence, `searchable` (the find bar's
 * tree deep-search), and a live JSON model dump the Playwright battery
 * asserts against.
 */
@Component({
  imports: [TmTreeGrid, TmGridColumn],
  template: `
    <h2>Tree grid</h2>

    <div class="toolbar">
      <label>
        <input
          type="checkbox"
          data-testid="toggle-readonly"
          [checked]="readonly()"
          (change)="onReadonlyChange($event)"
        />
        Readonly
      </label>
      <label>
        Lazy delay (ms)
        <input
          type="number"
          min="0"
          data-testid="lazy-delay"
          [value]="lazyDelay()"
          (input)="onDelayChange($event)"
        />
      </label>
      <label>
        <input
          type="checkbox"
          data-testid="lazy-fail-toggle"
          [checked]="lazyFail()"
          (change)="onLazyFailChange($event)"
        />
        Fail lazy loads
      </label>
      <label>
        Default depth
        <!-- Static options (first = the default): dynamic bindings inside
             the select trip a WebKit style-invalidation quirk that leaves
             the label's custom properties stale after a theme switch. -->
        <select data-testid="depth-select" (change)="onDepthChange($event)">
          <option value="all">all</option>
          <option value="1">1</option>
          <option value="0">0</option>
        </select>
      </label>
      <button type="button" data-testid="switch-content" (click)="switchContent()">
        Content: {{ contentName() }}
      </button>
      <label>
        <input
          type="checkbox"
          data-testid="toggle-mounted"
          [checked]="mounted()"
          (change)="onMountedChange($event)"
        />
        Mounted
      </label>
    </div>

    @if (mounted()) {
      <tm-tree-grid
        class="demo-grid"
        gridId="tree-grid"
        data-testid="tree-grid"
        [contentKey]="contentKey()"
        [field]="f.accounts"
        [rowId]="accountId"
        [parentId]="accountParentId"
        [parentIdKey]="'parentId'"
        [hasChildren]="hasChildren"
        [loadChildren]="loadChildren"
        [defaultExpandedDepth]="depth()"
        [newRow]="newAccount"
        [readonly]="readonly()"
        searchable
      >
        <tm-grid-column key="name" header="Name" [flex]="2" [minWidth]="240" />
        <tm-grid-column key="code" header="Code" [width]="90" />
        <tm-grid-column key="balance" type="number" header="Balance" [width]="120" />
        <tm-grid-column key="active" type="boolean" header="Active" [width]="90" />
      </tm-tree-grid>
    }

    <h3>Model</h3>
    <!-- tabindex: the dump scrolls, so keyboard users must be able to
         reach it (axe scrollable-region-focusable). -->
    <pre
      class="model-dump"
      data-testid="model-json"
      tabindex="0"
      aria-label="Model JSON"
    >{{ modelJson() }}</pre>
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 16px;
      align-items: center;
      margin-block-end: 12px;
    }
    .toolbar input[type='number'] {
      inline-size: 80px;
    }
    /* Token colors on the native controls: WebKit's UA form styling under
       a dark color-scheme fails the axe contrast floor otherwise. */
    .toolbar select,
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
    .demo-grid {
      display: block;
      block-size: 480px;
    }
    .model-dump {
      max-block-size: 160px;
      overflow: auto;
      font-size: 11px;
      background: var(--surface-raised, transparent);
      padding: 8px;
    }
  `,
})
export class TreeGridStory {
  readonly model = signal({ accounts: [...SEED_ACCOUNTS] });
  readonly f = form(this.model, (p) => {
    applyEach(p.accounts, (account) => {
      required(account.name);
    });
  });

  readonly readonly = signal(false);
  readonly lazyDelay = signal(400);
  readonly lazyFail = signal(false);
  readonly depth = signal<number | undefined>(undefined);
  readonly contentName = signal<'a' | 'b'>('a');
  readonly mounted = signal(true);

  private nextTempId = -1;

  /**
   * The content identity: switching either the a/b toggle or the depth
   * select starts a FRESH content slice, so a depth change re-seeds the
   * expansion set while same-key returns restore the remembered one.
   */
  readonly contentKey = computed(() => `${this.contentName()}:${this.depth() ?? 'all'}`);

  /** The live model dump the e2e battery parses (one JSON line). */
  readonly modelJson = computed(() => JSON.stringify(this.model().accounts));

  readonly accountId = (account: Account): number => account.id;
  readonly accountParentId = (account: Account): number | null => account.parentId;
  /** Only the lazy root advertises unloaded children. */
  readonly hasChildren = (account: Account): boolean => account.id === LAZY_ROOT_ID;

  /** New rows mint negative client-side temp ids, parent stamped when given. */
  readonly newAccount = (parent?: Account): Account => ({
    id: this.nextTempId--,
    parentId: parent?.id ?? null,
    name: null,
    code: null,
    balance: null,
    active: false,
  });

  /**
   * The lazy loader: waits the configured delay, rejects while the failure
   * toggle is on, and otherwise appends the lazy children to the model
   * array (once) — the grid re-derives the tree and expands over them.
   */
  readonly loadChildren = async (account: Account): Promise<void> => {
    const delay = this.lazyDelay();
    await new Promise((resolve) => setTimeout(resolve, delay));
    if (this.lazyFail()) {
      throw new Error('lazy load failed (story toggle)');
    }
    if (
      account.id === LAZY_ROOT_ID &&
      !this.model().accounts.some((candidate) => candidate.parentId === LAZY_ROOT_ID)
    ) {
      this.model.update((m) => ({ accounts: [...m.accounts, ...LAZY_CHILDREN] }));
    }
  };

  onReadonlyChange(event: Event): void {
    this.readonly.set((event.target as HTMLInputElement).checked);
  }

  onDelayChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.lazyDelay.set(Number.isFinite(value) && value >= 0 ? value : 0);
  }

  onLazyFailChange(event: Event): void {
    this.lazyFail.set((event.target as HTMLInputElement).checked);
  }

  onDepthChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.depth.set(value === 'all' ? undefined : Number(value));
  }

  switchContent(): void {
    this.contentName.update((name) => (name === 'a' ? 'b' : 'a'));
  }

  onMountedChange(event: Event): void {
    this.mounted.set((event.target as HTMLInputElement).checked);
  }
}
