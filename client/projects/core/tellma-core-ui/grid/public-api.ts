/**
 * Public API Surface of @tellma/core-ui/grid — the tm-grid data grid: a
 * thin Angular component layer (rendering, virtualization, focus, DOM
 * events, clipboard transport, state memory) composed over the headless
 * grid engine, plus the column-definition directive, the cell/header/state
 * template directives, the search-field driver directive, and the grid
 * state store. The ɵ-prefixed exports
 * are the private-by-convention internals the tree-grid entry point builds
 * on; they carry no compatibility promise.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './tm-grid';
export * from './tm-grid-base';
export * from './tm-grid-column';
export * from './tm-grid-context';
export * from './tm-grid-driver';
export * from './tm-grid-keys';
export * from './tm-grid-templates';
export * from './tm-grid-state-store';
export * from './tm-grid-types';

// The shared-internals surface for the tree grid, and the editing-load seams
// tests and the showcase hold open (golden-excluded ɵ names).
export {
  ɵTM_GRID_EDITING_LOAD_TIMEOUT,
  ɵTM_GRID_EDITING_LOADER,
  ɵTmGridCore,
  type ɵTmGridCellVm,
  type ɵTmGridColumnVm,
  type ɵTmGridCoreDeps,
  type ɵTmGridDraft,
  type ɵTmGridEditingCode,
  type ɵTmGridHeaderSortVm,
  type ɵTmGridRowVm,
  type ɵTmGridTreeConfig,
  type ɵTmGridViewCore,
} from './internal/grid-core';
export { ɵTmGridView } from './internal/grid-view';
export { ɵTmGridColumnResize } from './internal/column-resize';
