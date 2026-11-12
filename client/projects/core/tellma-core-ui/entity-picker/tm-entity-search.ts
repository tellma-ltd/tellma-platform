// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The search facility's result handling, shared by the picker and by a host
// that runs the same search without a picker (the grid resolving text typed
// before its editors arrived), so both judge a result set alike.

import type {
  ɵTmEntityAdoptedSearch,
  TmEntityId,
  TmEntitySearchFn,
  TmEntitySearchResult,
} from './tm-entity-picker-types';

/**
 * A search result set normalized to the object form.
 * @internal
 */
export interface ɵTmEntitySearchPage<T> {
  /** The result items, in the order the consumer returned them, one per id. */
  readonly items: readonly T[];
  /** Whether the consumer flagged the set as server-truncated. */
  readonly hasMore: boolean;
}

/**
 * Normalizes both result forms to the object form, dropping repeated ids
 * (the first occurrence stays). The search contract does not promise
 * distinct ids — a joined or unioned query can fan one entity out across
 * rows — while the listbox tracks rows BY id, and duplicate track keys abort
 * the render of the whole dropdown. Dropping the repeat is also the only
 * honest COUNT: two rows for one entity make the text that found them
 * unique, not ambiguous.
 * @internal
 */
export function ɵtmNormalizeEntityResult<T>(
  result: TmEntitySearchResult<T>,
  itemId: (item: T) => TmEntityId,
): ɵTmEntitySearchPage<T> {
  // Array.isArray does not narrow the readonly-array member out of the
  // union in the false branch, so the object form is asserted.
  const objectForm = Array.isArray(result)
    ? { items: result as readonly T[], hasMore: false }
    : (result as { readonly items: readonly T[]; readonly hasMore?: boolean });
  const items = objectForm.items;
  const hasMore = objectForm.hasMore === true;
  if (items.length < 2) {
    return { items, hasMore };
  }
  const seen = new Set<TmEntityId>();
  const distinct: T[] = [];
  for (const item of items) {
    const id = itemId(item);
    if (!seen.has(id)) {
      seen.add(id);
      distinct.push(item);
    }
  }
  return { items: distinct.length === items.length ? items : distinct, hasMore };
}

/**
 * Runs one search for `query` outside any picker, as an adopted search the
 * caller owns: it settles to the normalized result set, or `'failed'` when
 * the search throws or rejects, and `abort` cancels it.
 * @internal
 */
export function ɵtmEntitySearchOnce<T>(
  search: TmEntitySearchFn<T>,
  itemId: (item: T) => TmEntityId,
  query: string,
): ɵTmEntityAdoptedSearch<T> {
  const controller = new AbortController();
  let settled: ɵTmEntityAdoptedSearch<T>['settled'];
  try {
    settled = Promise.resolve(search(query, controller.signal)).then(
      (result) => ɵtmNormalizeEntityResult(result, itemId),
      () => 'failed' as const,
    );
  } catch {
    // A synchronous throw is a search failure, as in the picker.
    settled = Promise.resolve('failed' as const);
  }
  return { settled, abort: () => controller.abort() };
}
