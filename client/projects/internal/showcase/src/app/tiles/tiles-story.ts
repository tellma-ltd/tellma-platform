// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import { TmGridDriver, type TmGridRowActivation } from '@tellma/core-ui/grid';
import { TmInput } from '@tellma/core-ui/input';
import { TmTiles, TmTilesEmpty, type TmTileLayout } from '@tellma/core-ui/tiles';

import { HeldResponses, HeldResponsesControls } from '../held-responses';

/** One customer of the story. */
export interface StoryCustomer {
  /** The customer's id. */
  readonly id: number;
  /** The customer's name. */
  readonly name: string;
  /** The customer's code. */
  readonly code: string;
  /** The open balance. */
  readonly balance: number;
  /** Whether the customer is active. */
  readonly active: boolean;
  /** Whether the customer has a photo. */
  readonly photo: boolean;
}

/** `count` deterministic customers: every third has a photo, every fifth is inactive. */
export function storyCustomers(count: number): readonly StoryCustomer[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1,
    name: `Customer ${i + 1}`,
    code: `C${String(i + 1).padStart(5, '0')}`,
    balance: ((i + 1) * 7919) % 100000,
    active: (i + 1) % 5 !== 0,
    photo: (i + 1) % 3 === 1,
  }));
}

/** The balance format, built once: `toLocaleString` would build a formatter per call. */
const BALANCE = new Intl.NumberFormat('en-US');

/** The story's tile layout: a photo or its placeholder, a linked name, the balance and the status. */
export const CUSTOMER_TILE: TmTileLayout<StoryCustomer> = {
  image: (c) => ({ src: c.photo ? '/test-image.png' : null, alt: c.name }),
  title: (c) => c.name,
  href: (c) => `#customer-${c.id}`,
  topEnd: (c) => ({ text: BALANCE.format(c.balance), numeric: true }),
  bottomStart: (c) => c.code,
  bottomEnd: (c) => ({
    text: c.active ? 'Active' : 'Inactive',
    badge: c.active ? 'success' : 'neutral',
  }),
};

/**
 * tm-tiles in every flat state its section names: a search field driving
 * the active tile, selection with its announcements, row activation, the
 * built-in context and marked rows, loading (skeleton tiles, then busy over
 * data), the empty states, an external activeRowId write, a flashed
 * scroll, and the narrow list layout.
 */
@Component({
  imports: [HeldResponsesControls, TmGridDriver, TmInput, TmTiles, TmTilesEmpty],
  template: `
    <h2>Tiles</h2>

    <div class="toolbar">
      <input
        tmInput
        type="search"
        data-testid="search"
        aria-label="Search customers"
        placeholder="Search customers"
        [tmGridDriver]="tiles"
      />
      <app-held-responses [gate]="gate" name="rows" />
      <button type="button" data-testid="load" (click)="load()">Reload</button>
      <button type="button" data-testid="busy" (click)="loading.set(!loading())">
        {{ loading() ? 'Done loading' : 'Load over data' }}
      </button>
      <button type="button" data-testid="empty" (click)="empty()">Show none</button>
      <button type="button" data-testid="custom-empty" (click)="customEmpty.set(!customEmpty())">
        {{ customEmpty() ? 'Built-in empty text' : 'Custom empty template' }}
      </button>
      <!-- A consumer write to the active row: the tiles scroll, focus stays here. -->
      <button type="button" data-testid="jump" (click)="active.set(41)">Activate the 41st</button>
      <button type="button" data-testid="flash" (click)="tiles.scrollToRow(57, { flash: true })">
        Scroll to the 57th
      </button>
    </div>

    <tm-tiles
      #tiles
      class="tiles"
      data-testid="tiles"
      ariaLabel="Customers"
      [data]="rows()"
      [rowId]="idOf"
      [layout]="layout"
      [loading]="loading()"
      [rowClass]="classOf"
      selectable
      [(selectedIds)]="selected"
      [(activeRowId)]="active"
      (rowActivate)="activated.set($event)"
    >
      @if (customEmpty()) {
        <p *tmTilesEmpty data-testid="custom-empty-text">No customers match this search.</p>
      }
    </tm-tiles>

    <dl class="readout">
      <dt>Activated</dt>
      <dd data-testid="activated">{{ activatedText() }}</dd>
      <dt>Active</dt>
      <dd data-testid="active">{{ active() ?? 'none' }}</dd>
      <dt>Selected</dt>
      <dd data-testid="selected">{{ selected().size }}</dd>
    </dl>

    <!-- Below the small breakpoint the lattice is a divided list. -->
    <h3>Narrow</h3>
    <tm-tiles
      class="tiles tiles--narrow"
      data-testid="tiles-narrow"
      ariaLabel="Customers, narrow"
      [data]="narrowRows"
      [rowId]="idOf"
      [layout]="layout"
    />
  `,
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
      margin-block-end: 12px;
    }
    .tiles {
      block-size: 420px;
    }
    .tiles--narrow {
      inline-size: 360px;
      block-size: 260px;
    }
    .readout {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 12px;
    }
  `,
})
export class TilesStory {
  /** The reload's latency gate. */
  protected readonly gate = new HeldResponses();

  /** The rows the reload brings. */
  private readonly all = storyCustomers(120);

  /** The bound rows. */
  protected readonly rows = signal<readonly StoryCustomer[]>(this.all);

  /** The narrow instance's rows. */
  protected readonly narrowRows = this.all.slice(0, 12);

  /** Whether data is loading. */
  protected readonly loading = signal(false);

  /** Whether the custom empty template replaces the built-in text. */
  protected readonly customEmpty = signal(false);

  /** The two-way selection. */
  protected readonly selected = signal<ReadonlySet<TmRowId>>(new Set());

  /** The two-way active row. */
  protected readonly active = signal<TmRowId | null>(null);

  /** The last activation. */
  protected readonly activated = signal<TmGridRowActivation<StoryCustomer> | null>(null);

  /** The last activation, as text. */
  protected readonly activatedText = computed(() => {
    const activation = this.activated();
    return activation === null
      ? 'none'
      : `${activation.rowId} (${activation.via}${activation.newTab ? ', new tab' : ''})`;
  });

  /** A row's id. */
  protected readonly idOf = (row: StoryCustomer): number => row.id;

  /** The tile layout. */
  protected readonly layout = CUSTOMER_TILE;

  /** Inactive customers are context rows; the first is marked. */
  protected readonly classOf = (row: StoryCustomer): string | null =>
    row.id === 1 ? 'tm-tiles__item--marked' : row.active ? null : 'tm-tiles__item--context';

  /** Clears the rows and loads them again through the gate: skeleton tiles, then the data. */
  protected load(): void {
    this.rows.set([]);
    this.loading.set(true);
    void this.gate.wait(600).then(() => {
      this.rows.set(this.all);
      this.loading.set(false);
    });
  }

  /** Shows the empty state. */
  protected empty(): void {
    this.loading.set(false);
    this.rows.set([]);
  }
}
