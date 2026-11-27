// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  signal,
  TemplateRef,
  untracked,
  viewChild,
  type InputSignal,
  type Signal,
} from '@angular/core';
import { TmLocalization, TmTelemetry, type TmColumnPlan, type TmSearchQuery } from '@tellma/core';
import { TmColumnFilter } from '@tellma/core/column-filter';
import { TmModal, type TmModalRef } from '@tellma/core-ui/modal';
import { TmPopover, TmPopoverContent } from '@tellma/core-ui/popover';

import { tmSearchColumnPlan, tmSearchQuiet } from './tm-search-canonical';
import { ɵTmSearchFiltersForm } from './tm-search-filters-form';
import { ɵTM_SEARCH_FILTERS_STRINGS_EN } from './tm-search-filters-strings';
import type {
  TmSearchCustomFilterView,
  TmSearchHiddenOrder,
  ɵTmSearchFilterPanelsApi,
  ɵTmSearchFiltersHost,
  ɵTmSearchFiltersPage,
} from './tm-search-filters-host';
import { tmSearchFilterable, tmSearchIsActivatable } from './tm-search-model';

// The search page's filter panels (spec 0030 §9.1, §10.1), loaded on the
// first click of a filter button: a column's filter in a small popover at
// its header's button, and the Filters popup in a medium popover at the
// Filters button, end-aligned. Below `sm` each opens as an end drawer
// instead (§3.2). A panel's body is created as it opens and destroyed as
// it closes, so a dismissal discards whatever it staged; Apply closes the
// panel and returns focus to the button that opened it, in the same frame.

/** Each query field the panels change, with the kind of filter `filter-applied` reports for it. */
const KINDS: readonly (readonly [keyof TmSearchQuery, 'column' | 'advanced' | 'custom' | 'inactive' | 'tab'])[] = [
  ['columnFilters', 'column'],
  ['filter', 'advanced'],
  ['custom', 'custom'],
  ['inactive', 'inactive'],
  ['tab', 'tab'],
];

/** The panel open now: which, at which button, and its drawer when it is one. */
interface TmSearchOpenPanel {
  /** The column filter's or the Filters popup's. */
  readonly kind: 'column' | 'filters';
  /** The button that opened it, which takes focus back. */
  readonly anchor: HTMLElement;
  /** The drawer, below `sm`; `null` for a popover. */
  readonly drawer: TmModalRef | null;
}

/**
 * The search page's column filter panel and Filters popup, each a popover
 * from `sm` up and an end drawer below. Internal to the search page, which
 * creates it on the first click of a filter button.
 */
@Component({
  selector: 'tm-search-filter-panels',
  imports: [NgTemplateOutlet, TmColumnFilter, TmPopover, TmPopoverContent, ɵTmSearchFiltersForm],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- A column's filter, at its header's button. -->
    <tm-popover #columnPopover size="sm" [aria-label]="columnName()" (closed)="closed('column')">
      <ng-template tmPopoverContent>
        <ng-container [ngTemplateOutlet]="columnBody" />
      </ng-template>
    </tm-popover>

    <!-- The Filters popup, at the Filters button, end-aligned. -->
    <tm-popover #filtersPopover size="md" align="end" [aria-label]="text.filters()" (closed)="closed('filters')">
      <ng-template tmPopoverContent>
        <tm-search-filters-form [host]="host" [focusEditor]="focusEditor()" (done)="close()" />
      </ng-template>
    </tm-popover>

    <!-- The column filter's body, in the popover or the drawer. -->
    <ng-template #columnBody>
      @if (column(); as shown) {
        <tm-column-filter [column]="shown" [value]="host.query().columnFilters?.[shown.key] ?? null" (applied)="applyColumn(shown.key, $event)" />
      }
    </ng-template>

    <!-- Below sm: the drawers. -->
    <ng-template #columnDrawer>
      <ng-container [ngTemplateOutlet]="columnBody" />
    </ng-template>
    <ng-template #filtersDrawer>
      <tm-search-filters-form [host]="host" [focusEditor]="focusEditor()" [drawer]="true" (done)="close()" />
    </ng-template>
  `,
})
export class ɵTmSearchFilterPanels implements ɵTmSearchFilterPanelsApi {
  readonly #modal = inject(TmModal);
  readonly #l10n = inject(TmLocalization);
  readonly #telemetry = inject(TmTelemetry);

  /** What the page hands the panels. */
  readonly page: InputSignal<ɵTmSearchFiltersPage> = input.required<ɵTmSearchFiltersPage>();

  /** The setting of a drill-through list has no filter expression and no Active only. */
  readonly #expression = computed(() => this.page().setting().mode !== 'list');

  /** What the panels read of the page and how they apply, derived from what the page hands them. */
  protected readonly host: ɵTmSearchFiltersHost = {
    root: computed(() => this.page().setting().descriptor.name),
    expression: this.#expression,
    activeOnly: computed(() => this.#expression() && tmSearchIsActivatable(this.page().setting().descriptor)),
    drawer: computed(() => this.page().drawer()),
    query: computed(() => this.page().query()),
    tabs: computed(() => this.page().tabs()),
    custom: computed(() => this.#customViews()),
    filterable: computed(() => {
      const { descriptor } = this.page().setting();
      return this.page()
        .columns()
        .filter((column) => tmSearchFilterable(descriptor, column));
    }),
    hiddenOrder: computed(() => this.#hiddenOrder()),
    column: (key) => untracked(this.page().columns).find((column) => column.key === key) ?? tmSearchColumnPlan(tmSearchQuiet(untracked(this.page().setting)), key, 'url'),
    applyColumn: (key, value) => {
      const filters: Record<string, string> = { ...untracked(this.page().query).columnFilters };
      if (value === null) {
        delete filters[key];
      } else {
        filters[key] = value;
      }
      this.#apply({ columnFilters: Object.keys(filters).length === 0 ? undefined : filters });
    },
    apply: (patch) => this.#apply(patch),
  };

  /** The declared custom filters as the Filters popup offers them, each label in the active language. */
  readonly #customViews = computed<readonly TmSearchCustomFilterView[]>(() =>
    (this.page().setting().declaration.filters ?? []).map((filter) => ({
      key: filter.key,
      label: this.#l10n.ɵtext(filter.labelKey)(),
      options: 'options' in filter ? filter.options.map((option) => ({ key: option.value, label: this.#l10n.ɵtext(option.labelKey)() })) : null,
    })),
  );

  /** An order on a column the page does not show, which the Filters popup lists; `null` for none. */
  readonly #hiddenOrder = computed<TmSearchHiddenOrder | null>(() => {
    const order = this.page().query().order;
    if (order === undefined) {
      return null;
    }
    const direction = order.endsWith(':desc') ? 'desc' : 'asc';
    const key = direction === 'desc' ? order.slice(0, -5) : order;
    if (this.page().columns().some((column) => column.key === key)) {
      return null;
    }
    const column = tmSearchColumnPlan(tmSearchQuiet(this.page().setting()), key, 'url');
    return column === null ? null : { label: column.label(), direction };
  });

  /** The column filter's popover. */
  private readonly columnPopover = viewChild.required<TmPopover>('columnPopover');

  /** The Filters popup's popover. */
  private readonly filtersPopover = viewChild.required<TmPopover>('filtersPopover');

  /** The column filter's drawer body. */
  private readonly columnDrawer = viewChild.required<TemplateRef<unknown>>('columnDrawer');

  /** The Filters popup's drawer body. */
  private readonly filtersDrawer = viewChild.required<TemplateRef<unknown>>('filtersDrawer');

  /** The panel open now; `null` while none is. */
  readonly #open = signal<TmSearchOpenPanel | null>(null);

  /** The column whose filter shows. */
  protected readonly column = signal<TmColumnPlan | null>(null);

  /** Whether the Filters popup's editor takes focus as it opens. */
  protected readonly focusEditor = signal(false);

  /** The column filter panel's name: its column's label. */
  protected readonly columnName = computed(() => this.column()?.label() ?? '');

  /** The panels' labels. */
  protected readonly text = { filters: this.#l10n.ɵtext('tm.search.filters') };

  /** Registers the popup's own strings, which ship with this chunk. */
  constructor() {
    this.#l10n.ɵregisterStrings('searchFilters', ɵTM_SEARCH_FILTERS_STRINGS_EN);
  }

  /** {@inheritDoc ɵTmSearchFilterPanelsApi.columnOpen} */
  readonly columnOpen: Signal<string | null> = computed(() => (this.#open()?.kind === 'column' ? (this.column()?.key ?? null) : null));

  /** {@inheritDoc ɵTmSearchFilterPanelsApi.filtersOpen} */
  readonly filtersOpen: Signal<boolean> = computed(() => this.#open()?.kind === 'filters');

  /** {@inheritDoc ɵTmSearchFilterPanelsApi.toggleColumn} */
  toggleColumn(anchor: HTMLElement, key: string): void {
    const open = untracked(this.#open);
    if (open?.kind === 'column' && open.anchor === anchor) {
      this.close();
      return;
    }
    const column = this.host.column(key);
    if (column === null) {
      return;
    }
    this.#closeOther();
    this.column.set(column);
    this.#show('column', anchor, column.label());
  }

  /** {@inheritDoc ɵTmSearchFilterPanelsApi.toggleFilters} */
  toggleFilters(anchor: HTMLElement, focusEditor: boolean): void {
    if (untracked(this.#open)?.kind === 'filters') {
      this.close();
      return;
    }
    this.#closeOther();
    this.focusEditor.set(focusEditor);
    this.#show('filters', anchor, untracked(this.text.filters));
  }

  /** Closes the open panel, discarding what it staged, and returns focus to its button. */
  close(): void {
    const open = untracked(this.#open);
    if (open === null) {
      return;
    }
    this.#open.set(null);
    if (open.drawer !== null) {
      open.drawer.close();
    } else {
      (open.kind === 'column' ? this.columnPopover() : this.filtersPopover()).close();
    }
    // Focus is back on the button in the frame that closed the panel, whichever way it closed.
    open.anchor.focus();
  }

  /**
   * Applies a column filter from its panel, which then closes.
   *
   * @param key - The column's key.
   * @param value - The filter, or `null` to clear it.
   */
  protected applyColumn(key: string, value: string | null): void {
    this.host.applyColumn(key, value);
    this.close();
  }

  /**
   * Forgets a popover the user dismissed (Escape, an outside click, a Tab out).
   *
   * @param kind - The popover's panel.
   */
  protected closed(kind: 'column' | 'filters'): void {
    const open = untracked(this.#open);
    if (open?.kind === kind && open.drawer === null) {
      this.#open.set(null);
    }
  }

  /**
   * Applies filter changes as one change of the query, and reports a
   * `filter-applied` event for each kind of filter that changed.
   *
   * @param patch - The filters' fields to change.
   */
  #apply(patch: Partial<TmSearchQuery>): void {
    const page = this.page();
    const before = untracked(page.query);
    page.update(patch);
    const after = untracked(page.query);
    for (const [field, kind] of KINDS) {
      if (JSON.stringify(before[field]) !== JSON.stringify(after[field])) {
        this.#telemetry.event('tm.search.filter-applied', { kind });
      }
    }
  }

  /**
   * Opens a panel: a popover at its button from `sm` up, else an end drawer.
   *
   * @param kind - Which panel.
   * @param anchor - The button that opens it.
   * @param title - The drawer's title.
   */
  #show(kind: 'column' | 'filters', anchor: HTMLElement, title: string): void {
    if (!untracked(this.host.drawer)) {
      this.#open.set({ kind, anchor, drawer: null });
      (kind === 'column' ? this.columnPopover() : this.filtersPopover()).open(anchor);
      return;
    }
    const drawer = this.#modal.open(kind === 'column' ? this.columnDrawer() : this.filtersDrawer(), {
      placement: 'end',
      size: kind === 'column' ? 'sm' : 'md',
      title,
      returnFocus: anchor,
    });
    const open: TmSearchOpenPanel = { kind, anchor, drawer };
    this.#open.set(open);
    // A dismissal (the close button, Escape, the backdrop) forgets the drawer as Apply does.
    void drawer.closed.then(() => {
      if (untracked(this.#open) === open) {
        this.#open.set(null);
      }
    });
  }

  /** Closes the other panel, if one is open, before a panel opens. */
  #closeOther(): void {
    if (untracked(this.#open) !== null) {
      this.close();
    }
  }
}
