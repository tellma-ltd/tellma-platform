/**
 * Public API Surface of @tellma/core/page-snapshot — a page context read once
 * into plain JSON, with page data or with it left out and the user's text
 * masked: the form in which the support dump and a future assistant carry a
 * page. Nothing on the first frame reads it, so its callers load it with
 * import().
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export {
  tmSerializePageContext,
  type TmActionSnapshot,
  type TmErrorSnapshot,
  type TmFieldSnapshot,
  type TmFormSnapshot,
  type TmPageContextSnapshot,
  type TmPageContextSnapshotOptions,
  type TmVisibleSnapshot,
} from './src/tm-page-snapshot';
