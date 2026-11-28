// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, resource, signal } from '@angular/core';

import { TmPager } from '@tellma/core-ui/pager';

import { HeldResponses, HeldResponsesControls } from '../held-responses';

/**
 * tm-pager in every state its section names: range mode over 120 rows that
 * load through a held data source (so a click mid-load is visible), a
 * capped count with a last page, an unknown total, item mode, short size
 * labels, the three sizes and the disabled state.
 */
@Component({
  imports: [HeldResponsesControls, TmPager],
  template: `
    <h2>Pager</h2>

    <!-- Range mode over a data source: every page change is a request,
         and a later one supersedes an earlier one still in flight. -->
    <h3>Range, 120 rows</h3>
    <p class="toolbar"><app-held-responses [gate]="gate" name="page" /></p>
    <tm-pager
      data-testid="pager-range"
      [(page)]="page"
      [(pageSize)]="pageSize"
      [pageSizes]="[20, 50, 100]"
      [total]="120"
    />
    <p data-testid="page-state">Page {{ page() }} of size {{ pageSize() }}</p>
    <p data-testid="page-rows">
      {{ rows.isLoading() ? 'Loading…' : rows.value() }}
    </p>
    <p data-testid="page-requests">Requests: {{ requests().join(', ') }}</p>

    <!-- A capped count: Next stays enabled up to the last page. -->
    <h3>A capped count, at most 50 pages</h3>
    <tm-pager
      data-testid="pager-lower-bound"
      [(page)]="cappedPage"
      [total]="1000"
      totalIsLowerBound
      [maxPage]="50"
      [pageSizes]="[20, 1000]"
      sizeLabel="short"
    />

    <!-- An unknown total: no status, Next always enabled. -->
    <h3>Unknown total</h3>
    <tm-pager data-testid="pager-unknown" [(page)]="unknownPage" />

    <!-- Item mode: the host moves the index. -->
    <h3>Item mode</h3>
    <tm-pager
      data-testid="pager-item"
      mode="item"
      [index]="itemIndex()"
      [total]="293"
      (previous)="itemIndex.set(itemIndex() - 1)"
      (next)="itemIndex.set(itemIndex() + 1)"
    />

    <!-- The size ladder, and the disabled state's unchanged geometry. -->
    <h3>Sizes and disabled</h3>
    <div class="stack">
      <tm-pager size="sm" [page]="2" [total]="55" [pageSizes]="[20, 50]" />
      <tm-pager size="md" [page]="2" [total]="55" [pageSizes]="[20, 50]" />
      <tm-pager size="lg" [page]="2" [total]="55" [pageSizes]="[20, 50]" />
      <tm-pager
        data-testid="pager-disabled"
        disabled
        [page]="2"
        [total]="55"
        [pageSizes]="[20, 50]"
      />
    </div>
  `,
  styles: `
    .toolbar {
      display: flex;
      gap: 12px;
      align-items: center;
    }
    .stack {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: 8px;
    }
  `,
})
export class PagerStory {
  /** The rows' latency gate. */
  protected readonly gate = new HeldResponses();

  /** The range pager's page. */
  protected readonly page = signal(1);

  /** The range pager's page size. */
  protected readonly pageSize = signal(20);

  /** The pages requested so far, in order. */
  protected readonly requests = signal<readonly number[]>([]);

  /** The current page's rows, as a range of row numbers, loaded through the gate. */
  protected readonly rows = resource({
    params: () => ({ page: this.page(), size: this.pageSize() }),
    loader: async ({ params, abortSignal }) => {
      this.requests.update((pages) => [...pages, params.page]);
      await this.gate.wait(150, abortSignal);
      const from = (params.page - 1) * params.size + 1;
      return `Rows ${from} to ${Math.min(120, params.page * params.size)}`;
    },
  });

  /** The capped pager's page. */
  protected readonly cappedPage = signal(49);

  /** The unknown-total pager's page. */
  protected readonly unknownPage = signal(1);

  /** The item pager's position. */
  protected readonly itemIndex = signal(3);
}
