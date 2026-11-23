/**
 * Public API Surface of @tellma/core/entity — what edits, renders and queries
 * through the entity registry, which every entity page loads with it and the
 * app never loads at start: select plans that expand column keys into query
 * terms and read their rows back, the typed-value formats, the view and edit
 * components a value descriptor chooses, a form's uploads, the grid columns
 * of a plan, and the client check of a blob kind's policy.
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

// Blob-kind policies.
export { tmCheckBlobFile, type TmBlobFileCheck } from './src/tm-blob-check';
