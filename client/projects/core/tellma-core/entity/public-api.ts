/**
 * Public API Surface of @tellma/core/entity — what edits, renders and queries
 * through the entity registry, which every entity page loads with it and the
 * app never loads at start: select plans that expand column keys into query
 * terms and read their rows back, the typed-value formats, the view and edit
 * components a value descriptor chooses, a form's uploads, the grid columns
 * of a plan, the client check of a blob kind's policy, the entity picker
 * binding with the entity fields built on it, and the entity actions with
 * their dialogs, the confirmation loop and the server error paths a form's
 * errors land by.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Select plans.
export {
  tmSelectPlan,
  type TmColumnKind,
  type TmColumnPlan,
  type TmColumnSource,
  type TmDroppedKey,
  type TmDroppedReason,
  type TmFilterKind,
  type TmSelectPlan,
  type TmSelectPlanOptions,
} from './src/tm-select-plan';

// Typed values: formats, view and edit.
export { TmFormats, type TmValueStyle } from './src/tm-formats';
export { TmValue } from './src/tm-value';
export { TmValueEditor } from './src/tm-value-editor';
export { TmUploads } from './src/tm-uploads';
export { tmGridColumnFor, type TmGridColumnConfig, type TmGridColumnOptions } from './src/tm-grid-column-for';

// Entity pickers: the binding, the fields and the pages they open.
export { TmEntityPickers, type TmEntityPickerBinding, type TmEntityPickerOptions } from './src/tm-entity-pickers';
export { TmEntityField } from './src/tm-entity-field';
export { TmEntityMultiField } from './src/tm-entity-multi-field';
export { TmLazyPickerPage, type TmEntityPickerContext, ɵTM_PICKER_PAGES, type ɵTmPickerPages } from './src/tm-lazy-picker-page';

// Blob-kind policies.
export { tmCheckBlobFile, type TmBlobFileCheck } from './src/tm-blob-check';

// Entity actions: the runner, its dialogs and the confirmation loop.
export {
  injectTmActionDialog,
  TmEntityActions,
  type TmActionDialogContext,
  type TmActionDialogRunOptions,
  type TmEntitiesResult,
  type TmEntityActionOptions,
  type TmEntityActionOutcome,
  type TmOfferedActionsOptions,
} from './src/tm-entity-actions';
export { TmConfirmations, type TmConfirmationAttempt, type TmConfirmationContext } from './src/tm-confirmations';

// Server error paths onto a form's fields.
export {
  tmSplitErrorPath,
  tmWalkErrorPath,
  type TmErrorPathSegment,
  type TmErrorPathTarget,
  type TmErrorPathWalkOptions,
} from './src/tm-error-paths';
