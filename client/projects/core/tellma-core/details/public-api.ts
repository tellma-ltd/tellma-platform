/**
 * Public API Surface of @tellma/core/details — an entity record's details
 * page: the routed host that reads, views, edits and creates the record,
 * the source page over any other source, and the context a layout reads
 * with `injectTmDetails()`. It loads with the first details route.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// --- The routed host and the source page ---
export { TmDetailsHost } from './src/tm-details-host';
export { TmDetailsPage } from './src/tm-details-page';

// --- The record's context, as its layout sees it ---
export {
  injectTmDetails,
  TM_DETAILS_CONTEXT as ɵTM_DETAILS_CONTEXT,
  type TmDetailsCollectionRef,
  type TmDetailsContext,
  type TmDetailsFieldRef,
  type TmDetailsMode,
  type TmDetailsStatus,
  type TmRelated,
} from './src/tm-details-context';

// --- Sources ---
export {
  type TmDetailsAbilities,
  type TmDetailsChange,
  type TmDetailsRecord,
  type TmDetailsSource,
  tmEntitySource,
  type TmValidationProblem,
} from './src/tm-details-source';

// --- Internal: the record's state and the model rules, for the testing package ---
export { type TmDetailsHostView as ɵTmDetailsHostView, TmDetailsRecordState as ɵTmDetailsRecordState } from './src/tm-details-record';
export { tmDetailsCreateModel as ɵtmDetailsCreateModel } from './src/tm-details-model';
