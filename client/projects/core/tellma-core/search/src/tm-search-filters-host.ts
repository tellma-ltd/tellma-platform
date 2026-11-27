// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';
import type { TmColumnPlan, TmSearchQuery } from '@tellma/core';

import type { TmSearchSetting } from './tm-search-model';

// What the search host and its filter panels say to each other (spec 0030
// §9, §10). The panels (a column's filter and the Filters popup) load in a
// chunk of their own on the first click of a filter button, so the host
// knows them by these types alone: it hands them what the page shows and
// takes back the changes they apply.

/** A declared tab as the Filters popup offers it. */
export interface TmSearchTabView {
  /** The tab's key. */
  readonly key: string;
  /** The tab's label, in the active language. */
  readonly label: string;
}

/** A declared custom filter as the Filters popup offers it. */
export interface TmSearchCustomFilterView {
  /** The filter's key, as `x.{key}` carries it. */
  readonly key: string;
  /** The filter's label, in the active language. */
  readonly label: string;
  /** A radio group's options, each value with its label; `null` for a switch. */
  readonly options: readonly TmSearchTabView[] | null;
}

/** An order on a column the page does not show, which the Filters popup lists. */
export interface TmSearchHiddenOrder {
  /** The column's label. */
  readonly label: string;
  /** The direction. */
  readonly direction: 'asc' | 'desc';
}

/** What the filter panels read of the page and how they change its query. Internal. */
export interface ɵTmSearchFiltersHost {
  /** The entity the filter expression reads. */
  readonly root: Signal<string>;
  /** Whether the page has a filter expression to edit: not in a drill-through list. */
  readonly expression: Signal<boolean>;
  /** Whether the Active only switch shows: an activatable entity, outside a drill-through list. */
  readonly activeOnly: Signal<boolean>;
  /** Whether the panels open as end drawers: below `sm`. */
  readonly drawer: Signal<boolean>;
  /** The query the page shows. */
  readonly query: Signal<TmSearchQuery>;
  /** The tabs the Filters popup offers: the declared ones below `sm`, else none. */
  readonly tabs: Signal<readonly TmSearchTabView[]>;
  /** The declared custom filters, in declaration order. */
  readonly custom: Signal<readonly TmSearchCustomFilterView[]>;
  /** The shown columns that take a column filter, in their order. */
  readonly filterable: Signal<readonly TmColumnPlan[]>;
  /** An order on a column the page does not show; `null` for none. */
  readonly hiddenOrder: Signal<TmSearchHiddenOrder | null>;
  /**
   * The column of a filter's key, shown or not.
   *
   * @param key - The column's key.
   * @returns The column, or `null` when the key does not resolve.
   */
  column(key: string): TmColumnPlan | null;
  /**
   * Applies or clears one column's filter: one URL write and one read, on the first page.
   *
   * @param key - The column's key.
   * @param value - The filter, or `null` to clear it.
   */
  applyColumn(key: string, value: string | null): void;
  /**
   * Applies the Filters popup's staged edits as one query change.
   *
   * @param patch - The fields the popup edits.
   */
  apply(patch: Partial<TmSearchQuery>): void;
}

/**
 * What the search host hands its filter panels: the page's setting, its
 * shown columns and query, and the one way the panels change the query.
 * The panels derive the rest. Internal.
 */
export interface ɵTmSearchFiltersPage {
  /** The page's setting: its entity, declaration and mode. */
  readonly setting: Signal<TmSearchSetting>;
  /** The columns the page shows, in their order. */
  readonly columns: Signal<readonly TmColumnPlan[]>;
  /** The query the page shows. */
  readonly query: Signal<TmSearchQuery>;
  /** Whether the panels open as end drawers: below `sm`. */
  readonly drawer: Signal<boolean>;
  /** The tabs the Filters popup offers: the declared ones below `sm`, else none. */
  readonly tabs: Signal<readonly TmSearchTabView[]>;
  /**
   * Changes the query as one change: one URL write and one read, on the first page.
   *
   * @param patch - The fields to change.
   */
  update(patch: Partial<TmSearchQuery>): void;
}

/** The filter panels as the host drives them. Internal. */
export interface ɵTmSearchFilterPanelsApi {
  /** The key of the column whose filter panel is open; `null` while none is. */
  readonly columnOpen: Signal<string | null>;
  /** Whether the Filters popup is open. */
  readonly filtersOpen: Signal<boolean>;
  /**
   * Opens a column's filter at its header's button, or closes it when it is open there.
   *
   * @param anchor - The header's filter button.
   * @param key - The column's key.
   */
  toggleColumn(anchor: HTMLElement, key: string): void;
  /**
   * Opens the Filters popup at the Filters button, or closes it when it is open.
   *
   * @param anchor - The Filters button.
   * @param focusEditor - Whether the filter expression's editor takes focus, as Edit filter asks.
   */
  toggleFilters(anchor: HTMLElement, focusEditor: boolean): void;
}
