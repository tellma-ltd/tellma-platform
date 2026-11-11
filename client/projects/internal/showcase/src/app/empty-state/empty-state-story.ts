// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmReference } from '@tellma/core-ui/copy';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

/**
 * tm-empty-state as a page (an empty list with its action), as a panel
 * that replaces a list's rows when a search finds nothing (announced),
 * and as a failed load with a reference and a retry.
 */
@Component({
  imports: [TmButton, TmEmptyState, TmReference],
  template: `
    <h2>Empty states</h2>

    <!-- Page: centred in a frame of fixed height. -->
    <section class="frame">
      <tm-empty-state heading="No invoices yet" icon="inbox" data-testid="page">
        Invoices you create appear here.
        <button tmButton variant="primary" type="button" tmEmptyStateActions>New invoice</button>
      </tm-empty-state>
    </section>

    <!-- Panel: the search toggles between rows and the announced state. -->
    <section class="panel">
      <button tmButton type="button" data-testid="toggle-search" (click)="empty.set(!empty())">
        {{ empty() ? 'Clear the search' : 'Search for "zzz"' }}
      </button>
      @if (empty()) {
        <tm-empty-state
          heading="No matches"
          icon="search-x"
          size="panel"
          live="polite"
          data-testid="no-matches"
        >
          Try fewer or different words.
        </tm-empty-state>
      } @else {
        <ul data-testid="rows">
          <li>Anvil</li>
          <li>Rope</li>
        </ul>
      }
    </section>

    <!-- Error: a failed load, with the reference to quote. -->
    <section class="panel">
      <tm-empty-state
        heading="Couldn't load the invoices"
        icon="circle-x"
        tone="error"
        size="panel"
        data-testid="error"
      >
        <tm-reference value="4bf92f3577b34da6a3ce929d0e0e4736" />
        <button tmButton type="button" tmEmptyStateActions>Try again</button>
      </tm-empty-state>
    </section>
  `,
  styles: `
    .frame {
      display: flex;
      flex-direction: column;
      block-size: 280px;
      border: 1px solid var(--border-subtle);
      margin-block-end: 24px;
    }
    .panel {
      max-inline-size: 420px;
      padding: 8px;
      border: 1px solid var(--border-subtle);
      margin-block-end: 24px;
    }
  `,
})
export class EmptyStateStory {
  /** Whether the search found nothing. */
  protected readonly empty = signal(false);
}
