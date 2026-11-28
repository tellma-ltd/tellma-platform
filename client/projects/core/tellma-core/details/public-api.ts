/**
 * Public API Surface of @tellma/core/details — an entity record's details
 * page: the routed host that reads, views, edits and creates the record,
 * the source page over any other source, the context a layout reads with
 * `injectTmDetails()`, the layout kit a layout is written in, and the
 * validation schema every record's form gets. It loads with the first
 * details route; the edit controls load on the first edit, and a child
 * grid's grid once it nears the viewport.
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

// --- The layout kit ---
export { TmDetailsKit } from './src/tm-details-kit';
export { TmDetailsCard, TmDetailsCardSection } from './src/tm-details-card';
export { TmField, type TmFieldEditContext, TmFieldEdit, TmFieldView, type TmFieldViewContext } from './src/tm-field';
export { TmFieldGroup } from './src/tm-field-group';
export { TmChildGrid } from './src/tm-child-grid';
export { TmChildFiles } from './src/tm-child-files';
export { TmDetailsTab, TmDetailsTabContent, TmDetailsTabs } from './src/tm-details-tabs';
export { TmDetailsAside, ɵTmDetailsAsideContent } from './src/tm-details-aside';
export { TmDetailsAudit } from './src/tm-details-audit';
export { TmDetailsExtra, type TmDetailsExtraContext, TmDetailsTitleExtra } from './src/tm-details-extra';

// --- Validation ---
export {
  tmDetailsPlatformSchema,
  type TmDetailsSchemaOptions,
  type TmDetailsServerError,
  ɵtmDetailsNodeAt,
} from './src/tm-details-schema';

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
export {
  TM_DETAILS_RECORD as ɵTM_DETAILS_RECORD,
  type TmDetailsHostView as ɵTmDetailsHostView,
  TmDetailsRecordState as ɵTmDetailsRecordState,
} from './src/tm-details-record';
export { tmDetailsComplete as ɵtmDetailsComplete, tmDetailsCreateModel as ɵtmDetailsCreateModel } from './src/tm-details-model';
