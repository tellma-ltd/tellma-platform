// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmQueryRowSet } from '../entity/tm-rows-to-objects';
import type { TmId, TmJsonObject } from '../json/tm-json';
import type { TmSearchQuery } from '../urls/tm-search-query';

// What the search page keeps between visits and in the user's preferences
// (spec 0030 §11.4, §15.1). They are types only, here rather than in the
// search entry point, so a declaration, the details page's paging through
// results (spec 0031 §12) and the tab's memory can name them without
// loading the page.

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
