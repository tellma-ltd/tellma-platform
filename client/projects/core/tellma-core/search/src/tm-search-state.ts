// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
  viewChild,
  ViewContainerRef,
  type InputSignal,
  type OutputEmitterRef,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { TmLocalization, TmTelemetry, type TmApiError } from '@tellma/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

// The results region's states (spec 0030 §5.3): each a `tm-empty-state`
// with a polite live region, or the error view of any other failed read.
// The error views load with the first error they present (`/errors` is
// lazy), so a failed read renders them into a slot once their chunk lands,
// and a line with Try again of its own while the chunk cannot load. The host
// renders the state inside the grid's empty template, so the
// header and its Columns button stay reachable; the copy is
// entity-neutral.

/** The Create link a state offers: the URL and the entity's "New customer" label. */
export interface TmSearchStateCreate {
  /** The create page's URL. */
  readonly href: string;
  /** The label. */
  readonly label: string;
}

/** A state of the results region, in place of rows. */
export type TmSearchRegionState =
  | {
      /** No rows, and nothing narrows the query: the entity has none yet. */
      readonly kind: 'no-data';
      /** The Create link, when the user may create one. */
      readonly create: TmSearchStateCreate | null;
    }
  | {
      /** No rows the query matches. */
      readonly kind: 'no-matches';
      /** What the way out clears: the filters when any applies, else the search text; `null` for neither. */
      readonly clear: 'filters' | 'search' | null;
    }
  | {
      /** The advanced filter is not valid, or a leaf or a named filter from code is not. */
      readonly kind: 'invalid-filter' | 'invalid-code';
      /** The first diagnostic's message; `null` when the server sent none. */
      readonly message: string | null;
      /** The text the diagnostic points at, shown as code; `null` for none. */
      readonly token: string | null;
    }
  | {
      /** A drill-through list's link has no valid filter. */
      readonly kind: 'invalid-link';
    }
  | {
      /** A column or a filter cannot be calculated for some rows. */
      readonly kind: 'calculation';
      /** Why. */
      readonly reason: 'divide-by-zero' | 'overflow' | 'other';
    }
  | {
      /** Any other failed read, shown as the error table's view of it. */
      readonly kind: 'error';
      /** The read's error. */
      readonly error: TmApiError;
      /** The name of the entity the read was for. */
      readonly entity: string;
    };

/** One state of the search page's results region. Internal to the search host. */
@Component({
  selector: 'tm-search-state',
  imports: [RouterLink, TmButton, TmEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let shown = state();
    @switch (shown.kind) {
      <!-- Any other failed read: the error view, its Try again re-reading; a line of its own while the error views cannot load. -->
      @case ('error') {
        <ng-container #errorSlot />
        @if (errorViewsMissing()) {
          <p class="tm-search-state__failed">
            {{ text.failed() }}
            <button tmButton size="sm" type="button" (click)="retry.emit()">{{ text.retry() }}</button>
          </p>
        }
      }
      <!-- No rows yet, with the way to add the first one. -->
      @case ('no-data') {
        <tm-empty-state size="panel" icon="inbox" live="polite" [heading]="text.noData()">
          {{ text.noDataMessage() }}
          @if (shown.create; as create) {
            <a tmButton tmEmptyStateActions variant="primary" [routerLink]="create.href">{{ create.label }}</a>
          }
        </tm-empty-state>
      }
      <!-- Nothing matches: clear the filters, else the search. -->
      @case ('no-matches') {
        <tm-empty-state size="panel" icon="search-x" live="polite" [heading]="text.noMatches()">
          {{ text.noMatchesMessage() }}
          @if (shown.clear === 'filters') {
            <button tmButton tmEmptyStateActions type="button" (click)="clear.emit('filters')">{{ text.clearFilters() }}</button>
          } @else if (shown.clear === 'search') {
            <button tmButton tmEmptyStateActions type="button" (click)="clear.emit('search')">{{ text.clearSearch() }}</button>
          }
        </tm-empty-state>
      }
      <!-- A drill-through link whose filter is missing or bad. -->
      @case ('invalid-link') {
        <tm-empty-state size="panel" icon="circle-alert" tone="error" live="polite" [heading]="text.invalidLink()" />
      }
      <!-- A column or filter that fails for some rows, and why. -->
      @case ('calculation') {
        <tm-empty-state size="panel" icon="circle-alert" tone="error" live="polite" [heading]="text.calculation()">
          @if (shown.reason === 'divide-by-zero') {
            {{ text.divideByZero() }}
          } @else if (shown.reason === 'overflow') {
            {{ text.overflow() }}
          }
        </tm-empty-state>
      }
      <!-- An invalid filter, the user's or code's: the first diagnostic, its token as left-to-right code. -->
      @default {
        @if (shown.kind === 'invalid-filter' || shown.kind === 'invalid-code') {
          <tm-empty-state size="panel" icon="circle-alert" tone="error" live="polite" [heading]="text.invalidFilter()">
            @if (shown.message) {
              <span class="tm-search-state__message">{{ shown.message }}</span>
            }
            @if (shown.token) {
              <code class="tm-search-state__token" dir="ltr">{{ shown.token }}</code>
            }
          </tm-empty-state>
        }
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .tm-search-state__failed {
      margin: 0;
      padding: var(--space-4);
      text-align: center;
    }

    .tm-search-state__token {
      display: inline-block;
      max-inline-size: 100%;
      margin-inline-start: var(--space-1);
      padding-inline: var(--space-1);
      border-radius: var(--radius-sm);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      vertical-align: bottom;
      font-family: var(--font-mono);
      background: var(--surface-sunken);
    }
  `,
})
export class TmSearchState {
  readonly #l10n = inject(TmLocalization);
  /** Where the error view renders, while an error shows. */
  private readonly errorSlot = viewChild('errorSlot', { read: ViewContainerRef });
  // Whether the error views failed to load, so the state shows its own line.
  readonly #errorViewsMissing = signal(false);

  /** The state. */
  readonly state: InputSignal<TmSearchRegionState> = input.required<TmSearchRegionState>();

  /** Emits when the user asks to clear the filters or the search text. */
  readonly clear: OutputEmitterRef<'filters' | 'search'> = output<'filters' | 'search'>();

  /** Emits when the user asks the error view to try again. */
  readonly retry: OutputEmitterRef<void> = output<void>();

  /** The states' strings, in the active language. */
  protected readonly text = {
    noData: this.#l10n.ɵtext('tm.search.noData'),
    noDataMessage: this.#l10n.ɵtext('tm.search.noDataMessage'),
    noMatches: this.#l10n.ɵtext('tm.search.noMatches'),
    noMatchesMessage: this.#l10n.ɵtext('tm.search.noMatchesMessage'),
    clearFilters: this.#l10n.ɵtext('tm.search.clearFilters'),
    clearSearch: this.#l10n.ɵtext('tm.search.clearSearch'),
    invalidFilter: this.#l10n.ɵtext('tm.search.invalidFilter'),
    invalidLink: this.#l10n.ɵtext('tm.search.invalidLink'),
    calculation: this.#l10n.ɵtext('tm.search.calculation'),
    divideByZero: this.#l10n.ɵtext('tm.search.divideByZero'),
    overflow: this.#l10n.ɵtext('tm.search.overflow'),
    failed: this.#l10n.ɵtext('tm.search.failed'),
    retry: this.#l10n.ɵtext('tm.errors.retry'),
  };

  /** Whether the error views failed to load. */
  protected readonly errorViewsMissing = this.#errorViewsMissing.asReadonly();

  /** Renders the error view of a failed read once the error views load. */
  constructor() {
    // --- The error view, from the lazily loaded error views; a line of its own when they fail to load ---
    const telemetry = inject(TmTelemetry);
    effect((onCleanup) => {
      const slot = this.errorSlot();
      const shown = this.state();
      if (slot === undefined || shown.kind !== 'error') {
        return;
      }
      let live = true;
      onCleanup(() => {
        live = false;
        slot.clear();
        this.#errorViewsMissing.set(false);
      });
      import('@tellma/core/errors').then(
        ({ TmErrorPresenter, TmErrorState }) => {
          if (live) {
            const view = slot.injector.get(TmErrorPresenter).describe(shown.error, { kind: 'read', entity: shown.entity });
            const state = slot.createComponent(TmErrorState);
            state.setInput('view', view);
            state.setInput('size', 'panel');
            state.instance.retry.subscribe(() => this.retry.emit());
          }
        },
        (error: unknown) => {
          // Offline, or after a deploy removed the chunk: say so, and offer Try again.
          telemetry.error(error, { area: 'tm.search' });
          if (live) {
            this.#errorViewsMissing.set(true);
          }
        },
      );
    });
  }
}
