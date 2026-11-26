/**
 * Public API Surface of @tellma/core/search — an entity's search page, the
 * advanced search a picker opens and the drill-through list, all one host
 * loaded with the route. The host resolves the query state from the URL,
 * reads the rows, and shows them in a grid under the page's toolbar, with
 * each state of the read in their place. A declaration's custom cell
 * extends `TmSearchCell`; its search component and its page actions reach
 * the page through `injectTmSearch()`. The query model (the query state's
 * sources, its canonical URL, how a change reaches history and the
 * preferences, and the rows request) is internal to the entry point; the
 * page's view parts (the field and its keys, the region's states, a refused
 * query's diagnostics) load with it from `@tellma/core/search-view`.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The host the route renders.
export { TmSearchHost } from './src/tm-search-host';

// What the host's parts extend and inject.
export { TmSearchCell } from './src/tm-search-cell';
export { injectTmSearch } from './src/tm-search-context';
