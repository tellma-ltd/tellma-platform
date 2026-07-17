/**
 * Public API Surface of @tellma/core-ui/grid — the tm-grid data grid: a
 * thin Angular component layer (rendering, virtualization, focus, DOM
 * events, clipboard transport, state memory) composed over the headless
 * grid engine, plus the column-definition directive, the cell/header/state
 * template directives, and the grid state store. The ɵ-prefixed exports
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
export * from './tm-grid-column';
export * from './tm-grid-templates';
export * from './tm-grid-state-store';

// The shared-internals surface for the tree grid (golden-excluded ɵ names).
export { ɵTmGridBase } from './internal/grid-base';
export {
  ɵTmGridCore,
  type ɵTmGridCellVm,
  type ɵTmGridColumnVm,
  type ɵTmGridCoreDeps,
  type ɵTmGridRowVm,
  type ɵTmGridViewCore,
} from './internal/grid-core';
export { ɵTmGridView } from './internal/grid-view';
export { ɵTmGridColumnResize } from './internal/column-resize';
