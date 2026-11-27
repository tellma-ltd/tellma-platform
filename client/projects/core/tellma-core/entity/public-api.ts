/**
 * Public API Surface of @tellma/core/entity — the view side of what renders
 * and queries through the entity registry, which every entity page loads
 * with it and the app never loads at start: select plans that expand column
 * keys into query terms and read their rows back, the typed-value formats,
 * the view components a value descriptor chooses, the read-only grid
 * columns of a plan, the client check of a blob kind's policy, the picker
 * pages' contract, the entity actions with the confirmation loop and their
 * lazily loaded dialogs, the client Queryex (a lexer, and a builder and
 * printer for the Queryex the client writes), and the column filters with
 * their descriptions. The edit side (the value editor, the entity picker
 * binding with the entity fields and the editable grid columns) is
 * `@tellma/core/entity-editing`'s; a form's uploads are
 * `@tellma/core/uploads`'; the control that edits a column filter is
 * `@tellma/core/column-filter`'s.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Select plans; their shapes are the primary's types.
export { tmSelectPlan, ɵtmDisplayNameOrder, type TmSelectPlanOptions } from './src/tm-select-plan';

// Typed values: formats and view; the control a value takes in edit mode, which the editing entry point renders.
export { TmFormats, type TmValueStyle } from './src/tm-formats';
export { TmValue } from './src/tm-value';
export { ɵtmEditorKind, type ɵTmEditorKind } from './src/tm-editor-kind';
export {
  tmGridColumnFor,
  ɵtmGridColumnFor,
  ɵtmIsNewRow,
  type TmGridColumnConfig,
  type TmGridColumnOptions,
} from './src/tm-grid-column-for';

// The pages an entity picker opens: what they are opened for, and the seam each page family fills.
export { type TmEntityPickerContext, ɵTM_PICKER_PAGES, type ɵTmPickerPages } from './src/tm-picker-pages';

// Blob-kind policies.
export { tmCheckBlobFile, ɵtmCropCoverSquare, type TmBlobFileCheck } from './src/tm-blob-check';

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

// Queryex: the lexer, and the builder and printer of the Queryex the client writes.
export {
  tmQxLex,
  type TmQxLexDiagnostic,
  type TmQxLexResult,
  type TmQxToken,
  type TmQxTokenKind,
} from './src/tm-qx-lex';
export { tmQx, tmQxPrint, type TmQx, type TmQxBinaryOp } from './src/tm-qx';

// Column filters: the URL value, its Queryex leaf and its description; the
// operators and strings the column filter entry point's control offers.
export {
  TM_ENUM_PICKER_THRESHOLD,
  tmDescribeFilter,
  tmFilterLeaf,
  TmFilterDescriptionText,
  tmFormatFilter,
  tmParseFilter,
  type TmDescribeFilterOptions,
  type TmFilterDescription,
  type TmFilterOp,
  type TmFilterSpec,
  ɵTM_FILTER_OPS,
} from './src/tm-filter';
export { ɵTM_FILTER_STRINGS_EN } from './src/tm-entity-strings';
export { ɵtmParseExact, type ɵTmExactParse } from './src/tm-exact-number';
