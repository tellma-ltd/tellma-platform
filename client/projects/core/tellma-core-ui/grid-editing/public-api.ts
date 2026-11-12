/**
 * Public API Surface of @tellma/core-ui/grid-editing — the editable grid's
 * on-demand code: the editing controller (opening, mounting, committing and
 * cancelling cell editors), the built-in cell editors and the status bar.
 * `tm-grid` and `tm-tree-grid` load it themselves, with a dynamic import,
 * once a grid becomes editable, so a readonly grid never downloads it. Every
 * export is ɵ-prefixed: private-by-convention wiring with no compatibility
 * promise, never imported by app code.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The editing controller and the seam the grid core implements for it.
export {
  ɵTmGridEditing,
  type ɵTmGridEditingColumn,
  type ɵTmGridEditingHost,
  type ɵTmGridEditorOpenOptions,
} from './editing';
export type { ɵTmGridEditorTemplateContext, ɵTmGridMountedEditor } from './editor-session';

// The status bar the grid's view renders in its reserved slot.
export { ɵTmGridStatusBar, type ɵTmGridStatusSource } from './status-bar';
