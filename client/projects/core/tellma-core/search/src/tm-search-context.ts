// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken } from '@angular/core';
import type { TmSearchContext } from '@tellma/core';

// The search page's context (spec 0030 §2.5), as its parts reach it: the
// host provides it to everything it renders, so a `search` component, a
// custom cell and a declaration's page action read and drive the page
// through `injectTmSearch()`.

/** The search page's context, which the search host provides. Internal: parts call `injectTmSearch()`. */
export const ɵTM_SEARCH_CONTEXT = new InjectionToken<TmSearchContext>('TmSearchContext');

/**
 * The context of the search page this code runs in: its query, plan, rows
 * and read state, and the changes it may ask for.
 *
 * @returns The context.
 * @throws Error outside a search page's injection context.
 *
 * @example
 * ```ts
 * export default class ReportsResults {
 *   protected readonly search = injectTmSearch();
 *   protected open(id: TmId): void {
 *     this.search.open(id);
 *   }
 * }
 * ```
 */
export function injectTmSearch(): TmSearchContext {
  return inject(ɵTM_SEARCH_CONTEXT);
}
