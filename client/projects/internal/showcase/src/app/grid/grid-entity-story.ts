// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { form } from '@angular/forms/signals';

import type { TmLabelResolution } from '@tellma/core-ui/contracts';
import { TmButton } from '@tellma/core-ui/button';
import type { TmEntityPick, TmEntitySearchResult } from '@tellma/core-ui/entity-picker';
import { TM_GRID_CONTEXT, TmGrid, TmGridColumn } from '@tellma/core-ui/grid';
import { TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';

interface Agent {
  readonly id: number;
  readonly name: string;
}

/**
 * The demo directory: 'Adam Brown' appears twice (the resolver's ambiguous
 * case); 'Alice Green'/'Alan Grey' share the 'Al' prefix so a typed commit
 * of 'Al' is a multi-match (the resolver path) while 'Alice Gr' is unique
 * (the on-screen fast path).
 */
const AGENTS: readonly Agent[] = [
  { id: 1, name: 'Adam Brown' },
  { id: 2, name: 'Adam Brown' },
  { id: 3, name: 'Alice Green' },
  { id: 4, name: 'Alan Grey' },
  { id: 5, name: 'Bob Stone' },
  { id: 6, name: 'Carol White' },
  { id: 7, name: 'Dana Reed' },
];

interface OrderLine {
  readonly id: number;
  readonly description: string | null;
  readonly agentId: number | null;
  readonly otherId: number | null;
  readonly quantity: number | null;
}

/**
 * One page serves all three launch paths in this story: `gp-pick` closes
 * with Alice Green, `gp-null` reports the entity gone, `gp-cancel`
 * dismisses without a result.
 */
@Component({
  imports: [TmButton, TmModalFooter],
  template: `
    <p>Grid picker page</p>
    <div tmModalFooter>
      <button tmButton data-testid="gp-pick" (click)="pick()">Pick Alice Green</button>
      <button tmButton data-testid="gp-null" (click)="clear()">Delete entity</button>
      <button tmButton data-testid="gp-cancel" (click)="ref.close()">Cancel</button>
    </div>
  `,
})
export class GridAgentPage {
  protected readonly ref = inject(TmModalRef) as TmModalRef<TmEntityPick<number, Agent> | null>;

  protected pick(): void {
    this.ref.close({ id: 3, label: 'Alice Green' });
  }

  protected clear(): void {
    this.ref.close(null);
  }
}

/**
 * Entity-column grid demo host: the Agent column mounts the built-in
 * `tm-entity-picker` editor (search + accessors + modal pages + the async
 * paste/typed-commit resolver); the Other column has search but NO resolver
 * (typed labels become invalid inputs directly). The toolbar tunes the
 * search and resolver latencies and counts their calls; the model dump is
 * the committed-value oracle.
 */
@Component({
  imports: [TmGrid, TmGridColumn],
  providers: [
    { provide: TM_GRID_CONTEXT, useValue: { tenantId: signal('t1'), distributionKey: 'd1' } },
  ],
  template: `
    <h2>Grid (entity picker editor)</h2>

    <div class="toolbar">
      <label>
        Search delay (ms, 0 = synchronous)
        <input
          type="number"
          min="0"
          data-testid="search-delay"
          [value]="searchDelay()"
          (input)="onSearchDelayChange($event)"
        />
      </label>
      <span>Search calls: <span data-testid="search-calls">{{ searchCalls() }}</span></span>
      <label>
        Resolver delay (ms)
        <input
          type="number"
          min="0"
          data-testid="resolver-delay"
          [value]="resolverDelay()"
          (input)="onResolverDelayChange($event)"
        />
      </label>
      <span>Resolver calls: <span data-testid="resolver-calls">{{ resolverCalls() }}</span></span>
      <button type="button" data-testid="clear-history" (click)="clearHistory()">
        Clear history
      </button>
    </div>

    <tm-grid
      class="demo-grid"
      gridId="grid-entity"
      data-testid="grid-entity"
      [field]="f.lines"
      [rowId]="rowId"
      [newRow]="newLine"
    >
      <tm-grid-column key="description" header="Description" [flex]="1" [minWidth]="140" />
      <tm-grid-column
        key="agentId"
        type="entity"
        header="Agent"
        [search]="search"
        [itemId]="agentIdOf"
        [itemLabel]="agentNameOf"
        [format]="agentFormat"
        [resolvePastedLabels]="resolveAgents"
        [advancedSearch]="agentPage"
        [create]="agentPage"
        [edit]="agentPage"
        [width]="180"
      />
      <tm-grid-column
        key="otherId"
        type="entity"
        header="Other"
        [search]="search"
        [itemId]="agentIdOf"
        [itemLabel]="agentNameOf"
        [format]="agentFormat"
        [width]="160"
      />
      <tm-grid-column key="quantity" type="number" header="Qty" [width]="90" />
    </tm-grid>

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
    .demo-grid {
      display: block;
      block-size: 420px;
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
export class GridEntityStory {
  readonly model = signal({
    lines: [
      { id: 1, description: 'Consulting', agentId: 5, otherId: null, quantity: 3 },
      { id: 2, description: 'Hardware', agentId: 3, otherId: null, quantity: 12 },
      { id: 3, description: 'Freight', agentId: null, otherId: null, quantity: 1 },
      { id: 4, description: 'Support', agentId: 6, otherId: null, quantity: 8 },
      { id: 5, description: 'License', agentId: null, otherId: null, quantity: 2 },
      { id: 6, description: 'Training', agentId: 7, otherId: null, quantity: 5 },
    ] as OrderLine[],
  });
  readonly f = form(this.model);

  readonly searchDelay = signal(0);
  readonly searchCalls = signal(0);
  readonly resolverDelay = signal(150);
  readonly resolverCalls = signal(0);

  private readonly grid = viewChild.required(TmGrid);
  private nextTempId = -1;

  /** The live model dump the e2e battery parses (one JSON line). */
  readonly modelJson = computed(() => JSON.stringify(this.model().lines));

  readonly rowId = (line: OrderLine): number => line.id;
  readonly newLine = (): OrderLine => ({
    id: this.nextTempId--,
    description: null,
    agentId: null,
    otherId: null,
    quantity: null,
  });

  readonly agentPage = GridAgentPage;
  readonly agentIdOf = (agent: Agent): number => agent.id;
  readonly agentNameOf = (agent: Agent): string => agent.name;
  readonly agentFormat = (value: number | null): string =>
    value === null ? '' : (AGENTS.find((agent) => agent.id === value)?.name ?? `#${String(value)}`);

  /** Sync at delay 0 (the fast path); a delayed promise otherwise. */
  readonly search = (
    query: string,
  ): TmEntitySearchResult<Agent> | Promise<TmEntitySearchResult<Agent>> => {
    this.searchCalls.update((count) => count + 1);
    const q = query.trim().toLowerCase();
    const matches =
      q === '' ? [...AGENTS] : AGENTS.filter((agent) => agent.name.toLowerCase().includes(q));
    const delay = this.searchDelay();
    if (delay <= 0) {
      return matches;
    }
    return new Promise((resolve) => setTimeout(() => resolve(matches), delay));
  };

  /** The typed-commit/paste resolver: exact-label match after the delay. */
  readonly resolveAgents = async (
    labels: string[],
  ): Promise<ReadonlyMap<string, TmLabelResolution<number | null>>> => {
    this.resolverCalls.update((count) => count + 1);
    await new Promise((resolve) => setTimeout(resolve, this.resolverDelay()));
    const map = new Map<string, TmLabelResolution<number | null>>();
    for (const label of labels) {
      const matches = AGENTS.filter(
        (agent) => agent.name.toLowerCase() === label.trim().toLowerCase(),
      );
      if (matches.length === 1) {
        map.set(label, { value: matches[0].id });
      } else if (matches.length > 1) {
        map.set(label, { error: 'ambiguous' });
      } else {
        map.set(label, { error: 'notFound' });
      }
    }
    return map;
  };

  onSearchDelayChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.searchDelay.set(Number.isFinite(value) && value >= 0 ? value : 0);
  }

  onResolverDelayChange(event: Event): void {
    const value = Number((event.target as HTMLInputElement).value);
    this.resolverDelay.set(Number.isFinite(value) && value >= 0 ? value : 0);
  }

  clearHistory(): void {
    this.grid().clearHistory();
  }
}
