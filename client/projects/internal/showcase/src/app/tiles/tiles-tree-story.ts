// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import type { TmGridRowActivation } from '@tellma/core-ui/grid';
import { TmTiles, type TmTileLayout } from '@tellma/core-ui/tiles';

import { HeldResponses, HeldResponsesControls } from '../held-responses';

/** One account of the story's chart of accounts. */
export interface StoryAccount {
  /** The account's id. */
  readonly id: number;
  /** The parent account's id; `null` for a root. */
  readonly parentId: number | null;
  /** The account's name. */
  readonly name: string;
  /** The account's code. */
  readonly code: string;
  /** Whether the account's children load on demand: `fails` loads with an error. */
  readonly lazy?: 'loads' | 'fails';
}

/** The roots: two that load, one that fails, and a loaded branch. */
const ROOTS: readonly StoryAccount[] = [
  { id: 1, parentId: null, name: 'Assets', code: '1', lazy: 'loads' },
  { id: 2, parentId: null, name: 'Liabilities', code: '2', lazy: 'loads' },
  { id: 3, parentId: null, name: 'Archive', code: '9', lazy: 'fails' },
  { id: 4, parentId: null, name: 'Equity', code: '3' },
  { id: 41, parentId: 4, name: 'Capital', code: '31' },
  { id: 42, parentId: 4, name: 'Retained earnings', code: '32' },
];

/** The children the data source brings for a parent. */
function childrenOf(parent: StoryAccount): readonly StoryAccount[] {
  return [1, 2].map((n) => ({
    id: parent.id * 100 + n,
    parentId: parent.id,
    name: `${parent.name} ${n}`,
    code: `${parent.code}${n}`,
  }));
}

/**
 * tm-tiles in tree mode: a single-column treegrid over flat rows whose
 * lazy branches load in batches through a held data source, one branch
 * that fails, and an already loaded branch.
 */
@Component({
  imports: [HeldResponsesControls, TmTiles],
  template: `
    <h2>Tiles (tree)</h2>
    <p class="toolbar"><app-held-responses [gate]="gate" name="children" /></p>

    <tm-tiles
      class="tiles"
      data-testid="tiles-tree"
      ariaLabel="Accounts"
      [data]="rows()"
      [rowId]="idOf"
      [layout]="layout"
      [parentId]="parentOf"
      [hasChildren]="lazyOf"
      [loadChildren]="load"
      [(expandedIds)]="expanded"
      (rowActivate)="activated.set($event)"
    />

    <dl class="readout">
      <dt>Expanded</dt>
      <dd data-testid="expanded">{{ expandedText() }}</dd>
      <dt>Batches</dt>
      <dd data-testid="batches">{{ batchesText() }}</dd>
      <dt>Activated</dt>
      <dd data-testid="activated">{{ activated()?.rowId ?? 'none' }}</dd>
    </dl>
  `,
  styles: `
    .toolbar {
      display: flex;
      gap: 12px;
      align-items: center;
    }
    .tiles {
      inline-size: 420px;
      block-size: 360px;
    }
    .readout {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 12px;
    }
  `,
})
export class TilesTreeStory {
  /** The lazy branches' latency gate. */
  protected readonly gate = new HeldResponses();

  /** The flat rows: the roots, then whatever the loads append. */
  protected readonly rows = signal<readonly StoryAccount[]>(ROOTS);

  /** The two-way expansion. */
  protected readonly expanded = signal<ReadonlySet<TmRowId>>(new Set());

  /** Each `loadChildren` call's row ids, in order. */
  private readonly batches = signal<readonly (readonly number[])[]>([]);

  /** The last activation. */
  protected readonly activated = signal<TmGridRowActivation<StoryAccount> | null>(null);

  /** The expansion, as text. */
  protected readonly expandedText = computed(() => [...this.expanded()].join(', ') || 'none');

  /** The batches, as text: `1+2; 3`. */
  protected readonly batchesText = computed(
    () =>
      this.batches()
        .map((batch) => batch.join('+'))
        .join('; ') || 'none',
  );

  /** A row's id. */
  protected readonly idOf = (row: StoryAccount): number => row.id;

  /** A row's parent id. */
  protected readonly parentOf = (row: StoryAccount): number | null => row.parentId;

  /** Whether a row's children load on demand. */
  protected readonly lazyOf = (row: StoryAccount): boolean => row.lazy !== undefined;

  /** The tile layout: the name and the code. */
  protected readonly layout: TmTileLayout<StoryAccount> = {
    title: (row) => row.name,
    topEnd: (row) => ({ text: row.code, numeric: true }),
  };

  /** Loads a batch through the gate, appending the children; `fails` rows reject the batch. */
  protected readonly load = async (parents: readonly StoryAccount[]): Promise<void> => {
    this.batches.update((batches) => [...batches, parents.map((row) => row.id)]);
    await this.gate.wait(300);
    if (parents.some((row) => row.lazy === 'fails')) {
      throw new Error('The archive is unavailable.');
    }
    this.rows.update((rows) => [...rows, ...parents.flatMap(childrenOf)]);
  };
}
