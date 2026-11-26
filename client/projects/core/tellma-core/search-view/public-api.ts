/**
 * Public API Surface of @tellma/core/search-view — the search page's view
 * parts: the field in the form field's chrome with its keys (type-to-search
 * and the pause the live region waits for), and the results region's
 * states, and a refused query's diagnostics. The search host loads it with
 * its route; nothing else uses it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The field the host renders in its header.
export { ɵTmSearchPageField } from './src/tm-search-page-field';

// The keys, the live region's pause and the row menu's copied link.
export { ɵTmSearchKeys, type ɵTmSearchKeysOptions } from './src/tm-search-keys';
export type { TmSearchFieldMode } from './src/tm-search-typing';

// A refused query's diagnostics, mapped back to their controls.
export {
  ɵtmSearchContextErrors,
  ɵtmSearchDiagnose,
  ɵtmSearchRefusedState,
  type TmSearchDiagnosis as ɵTmSearchDiagnosis,
  type TmSearchDiagnosisState as ɵTmSearchDiagnosisState,
  type TmSearchDiagnostic as ɵTmSearchDiagnostic,
  type TmSearchOriginLike as ɵTmSearchOriginLike,
  type TmSearchOrigins as ɵTmSearchOrigins,
  type TmSearchRefusedRequest as ɵTmSearchRefusedRequest,
} from './src/tm-search-diagnostics';

// The results region's states.
export {
  TmSearchState as ɵTmSearchState,
  ɵTM_SEARCH_ERROR_VIEWS,
  type TmSearchErrorViewsLoader as ɵTmSearchErrorViewsLoader,
  type TmSearchRegionState as ɵTmSearchRegionState,
  type TmSearchStateCreate as ɵTmSearchStateCreate,
} from './src/tm-search-state';
