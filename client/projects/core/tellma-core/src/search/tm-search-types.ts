// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal, WritableSignal } from '@angular/core';

import type { TmApiError } from '../api/tm-api-error';
import type { TmPlanRow } from '../entity/tm-entity-declaration';
import type { TmEntityDescriptor } from '../entity/tm-entity-descriptors';
import type { TmQueryRowSet } from '../entity/tm-rows-to-objects';
import type { TmSelectPlan } from '../entity/tm-select-plan-types';
import type { TmId, TmJsonObject } from '../json/tm-json';
import type { TmSearchQuery } from '../urls/tm-search-query';

// What the search page keeps between visits and in the user's preferences
// (spec 0030 §11.4, §15.1), and the context its parts read (§2.5). They are
// types only, here rather than in the search entry point, so a declaration
// (whose page actions receive the context), the details page's paging
// through results (spec 0031 §12) and the tab's memory can name them
// without loading the page.

/**
 * One entry of a user's shown columns, as the column preference stores it: a
 * column key, or an expression column, whether declared or the user's own.
 * An expression's text matching a declared expression column takes that
 * column's label and type when read back.
 *
 * @example
 * ```ts
 * const columns: readonly TmColumnPreference[] = ['Code', 'Name', 'Parent', { expression: 'CreditLimit * 2' }];
 * ```
 */
export type TmColumnPreference =
  | string
  | {
      /** The Queryex expression the column shows. */
      readonly expression: string;
    };

/**
 * A search page as its host left it: the query, the rows that answered it
 * and where the user was among them. The page remembers it in the tab's
 * memory, so a return shows the same rows at once, and the details page
 * pages through its rows.
 *
 * @example
 * ```ts
 * const snapshot: TmSearchSnapshot = {
 *   entity: 'gl.Center', query: { q: 'jed' }, canonical: '/17/centers?q=jed',
 *   request: { route: 'centers/query', body: { select: 'Id,Code', search: 'jed', skip: 0, take: 50, includeCount: true } },
 *   result: { set: { columns: [], rows: [] }, total: 0, capped: false },
 *   ids: [], selection: [], highlight: null,
 * };
 * ```
 */
export interface TmSearchSnapshot {
  /** The entity's name, such as `gl.Center`. */
  readonly entity: string;
  /** The query state the page showed. */
  readonly query: TmSearchQuery;
  /** The canonical URL of that query state: the identity of the result. */
  readonly canonical: string;
  /** The canonical URL of a query state this snapshot replaces, when it was written for another page of the same results. */
  readonly supersedes?: string;
  /** The last rows request; `null` for a tree shown roots first, which loads by parent. */
  readonly request: {
    /** The route, such as `centers/query`. */
    readonly route: string;
    /** The request body. */
    readonly body: TmJsonObject;
  } | null;
  /** What that request returned; `null` when no rows were shown. */
  readonly result: {
    /** The matched rows. */
    readonly set: TmQueryRowSet;
    /** In a tree search, the ancestors of the matched rows. */
    readonly ancestors?: TmQueryRowSet;
    /** The number of matching rows; `null` when unknown. */
    readonly total: number | null;
    /** Whether the count was capped, so `total` is a lower bound. */
    readonly capped: boolean;
  } | null;
  /** The shown rows' ids, in display order. */
  readonly ids: readonly TmId[];
  /** The selected rows' ids. */
  readonly selection: readonly TmId[];
  /** The row last opened, else the highlighted row; `null` for none. */
  readonly highlight: TmId | null;
  /** In a tree, the expanded rows' ids. */
  readonly expanded?: readonly TmId[];
  /** In the tiles view, the scroll offset in pixels. */
  readonly scroll?: number;
}

/**
 * A search page as its parts read and drive it: the query, the plan and
 * the rows it shows, the read's state, the selection and the highlight, and
 * the changes a part may ask for. A `search` component, a custom cell and a
 * declaration's page actions receive it; `injectTmSearch()` returns it
 * inside the search page.
 *
 * @example
 * ```ts
 * const search = injectTmSearch();
 * const count = computed(() => search.rows().length);
 * search.update({ q: 'jed' });
 * ```
 */
export interface TmSearchContext {
  /** Where the page runs: an entity's search page, a picker's advanced search, or a drill-through list. */
  readonly mode: 'page' | 'picker' | 'list';
  /** The entity's descriptor. */
  readonly descriptor: TmEntityDescriptor;
  /** The query state the page shows, in its canonical form. */
  readonly query: Signal<TmSearchQuery>;
  /** The select plan: its columns are the shown ones; the tile's and the tree's paths join its select only. */
  readonly plan: Signal<TmSelectPlan>;
  /** The rows on screen. */
  readonly rows: Signal<readonly TmPlanRow[]>;
  /** In a search tree, the ancestors of the matched rows. */
  readonly ancestors: Signal<readonly TmPlanRow[]>;
  /** The number of matching rows; `null` while unknown. */
  readonly total: Signal<number | null>;
  /** Whether `total` is a lower bound, the count having been capped. */
  readonly totalIsLowerBound: Signal<boolean>;
  /** The read's state: `loading` while a read runs, also while held rows show. */
  readonly status: Signal<'loading' | 'ready' | 'error'>;
  /** The error the last read failed with; `null` after a read that succeeded. */
  readonly error: Signal<TmApiError | null>;
  /** The selected rows' ids, across pages. */
  readonly selection: Signal<ReadonlySet<TmId>>;
  /** The highlighted row's id; `null` for none. */
  readonly highlight: WritableSignal<TmId | null>;
  /**
   * Changes the selection.
   *
   * @param ids - The rows' ids.
   * @param mode - Whether the ids join the selection, leave it, or replace it.
   */
  select(ids: Iterable<TmId>, mode: 'add' | 'remove' | 'replace'): void;
  /**
   * Changes the query under the page's history and paging rules: a page
   * change alone pushes a history entry, any other change replaces it, and
   * a change of the text, a filter or the order goes back to the first page.
   *
   * @param patch - The fields to change; a field given as `undefined` goes back to its default.
   */
  update(patch: Partial<TmSearchQuery>): void;
  /** Reads the current query again, keeping the page, the selection and the highlight. */
  refresh(): void;
  /**
   * Opens a row's target: its declared link, else its details page.
   *
   * @param id - The row's id.
   * @param options - Whether it opens in a new tab.
   */
  open(id: TmId, options?: { readonly newTab?: boolean }): void;
  /**
   * The URL of a row's target.
   *
   * @param id - The row's id.
   * @returns The URL; `null` when the row has none, or is not on screen.
   */
  url(id: TmId): string | null;
}
