/**
 * Public API Surface of @tellma/core/expression-editor — the editor of one
 * Queryex clause, `tm-expression-editor`, and the draft its editors of one
 * query share, which checks every clause with `queryex/discover`; with them
 * the Queryex the editor reads and writes beside the lexer: the parameters
 * a text names and an insertion that never splits a token. The search
 * page's advanced filter, the role editor's permission filters and the
 * report studio load it; no page's first frame does.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The editor.
export { TmExpressionEditor } from './src/tm-expression-editor';

// The draft its editors share.
export {
  injectTmQueryexDraft,
  type TmQueryexDraft,
  type TmQueryexDraftOptions,
  type TmQueryexStatus,
  type TmQxClause,
  type TmQxDiagnostic,
  type TmQxParameter,
} from './src/tm-queryex-draft';

// The wire of `queryex/discover`.
export type {
  TmQueryexConjunct,
  TmQueryexDiscoverRequest,
  TmQueryexDiscoverResult,
  TmQueryexParameterInfo,
  TmQueryexParameterInput,
  TmQueryexSite,
  TmQueryexType,
  TmQueryexWireDiagnostic,
  TmQxPathUse,
} from './src/tm-queryex-wire';

// Queryex text: parameters and insertion.
export { tmQxInsert, tmQxParameters, type TmQxParameterSites } from './src/tm-qx-text';
