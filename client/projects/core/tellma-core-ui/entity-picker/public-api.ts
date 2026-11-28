/**
 * Public API Surface of @tellma/core-ui/entity-picker — `tm-entity-picker`,
 * the server-searched foreign-key selector: an editable combobox whose text
 * is a query surface over a consumer-supplied search function, with
 * optional advanced-search, create, and edit modal pages — and
 * `tm-entity-multi-picker`, which holds several entities as chips over the
 * same search.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export { TmEntityMultiPicker } from './tm-entity-multi-picker';
export { TmEntityPicker } from './tm-entity-picker';
export {
  ɵtmEntitySearchOnce,
  ɵtmNormalizeEntityResult,
  type ɵTmEntitySearchPage,
} from './tm-entity-search';
export type {
  ɵTmEntityAdoptedSearch,
  TmEntityId,
  TmEntityMultiPick,
  TmEntityPick,
  TmEntityPicked,
  TmEntityPickerPage,
  TmEntityPickerPageData,
  TmEntitySearchFn,
  TmEntitySearchResult,
} from './tm-entity-picker-types';
