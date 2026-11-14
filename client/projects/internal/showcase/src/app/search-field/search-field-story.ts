// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, signal } from '@angular/core';

import { TmFormField } from '@tellma/core-ui/form-field';
import { TmInput } from '@tellma/core-ui/input';
import { TmSearch, TmSearchField } from '@tellma/core-ui/search-field';

/**
 * tm-search-field demo host: the composed field with its value and emitted
 * queries on show, a filled field, the invalid state, the three sizes, and
 * the bare `input[tmSearch]` directive in a field of its own.
 */
@Component({
  imports: [TmFormField, TmInput, TmSearch, TmSearchField],
  template: `
    <h2>Search field</h2>

    <div class="stack">
      <tm-search-field
        ariaLabel="Search customers"
        [(value)]="query"
        (search)="onSearch($event)"
        data-testid="search-main"
      />
      <div class="row">
        <output data-testid="search-value">{{ query() }}</output>
        <output data-testid="search-emitted">{{ emittedJson() }}</output>
      </div>

      <tm-search-field
        ariaLabel="Search documents"
        placeholder="Search by number"
        value="INV-1001"
        data-testid="search-filled"
      />

      <tm-search-field
        ariaLabel="Search accounts"
        invalid
        value="(("
        data-testid="search-invalid"
      />

      @for (size of sizes; track size) {
        <tm-search-field
          [ariaLabel]="'Search, size ' + size"
          [size]="size"
          [attr.data-testid]="'search-size-' + size"
        />
      }

      <!-- The bare directive, for custom chrome. -->
      <tm-form-field label="Filter" data-testid="ff-bare">
        <input tmInput tmSearch [(value)]="filter" data-testid="search-bare" />
      </tm-form-field>
      <output data-testid="bare-value">{{ filter() }}</output>
    </div>
  `,
  styles: `
    .stack {
      display: grid;
      gap: 12px;
      max-inline-size: 420px;
    }
    .row {
      display: flex;
      gap: 16px;
    }
    output {
      display: block;
      font-size: 12px;
      color: var(--text-secondary);
    }
  `,
})
export class SearchFieldStory {
  /** The three steps of the size ladder. */
  protected readonly sizes = ['sm', 'md', 'lg'] as const;
  /** The main field's two-way value. */
  protected readonly query = signal('');
  /** Every query the main field emitted, in order. */
  protected readonly emitted = signal<readonly string[]>([]);
  /** The emitted queries as JSON, so an empty query shows. */
  protected readonly emittedJson = computed(() => JSON.stringify(this.emitted()));
  /** The bare directive's two-way value. */
  protected readonly filter = signal('');

  /** Records an emitted query. */
  protected onSearch(query: string): void {
    this.emitted.update((list) => [...list, query]);
  }
}
