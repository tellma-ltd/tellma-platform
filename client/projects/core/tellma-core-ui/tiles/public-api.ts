/**
 * Rows as a virtualized lattice of fixed-size tiles, an image and four text
 * corners each: a divided list on narrow containers, on the data grid's
 * members, so a page swaps grid and tiles over one state. Tree mode, a
 * single-column tree with lazy children, is `@tellma/core-ui/tiles-tree`'s,
 * so a flat list never downloads it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmTiles } from './tm-tiles';
export { TmTileContent, TmTilesEmpty, TmTilesLoading } from './tm-tiles-templates';
export type { TmTileContentContext, TmTileImage, TmTileLayout, TmTileText } from './tm-tiles-types';

// The seam tree mode plugs into: private-by-convention wiring for the
// tiles-tree entry point, with no compatibility promise.
export type { ɵTmTilesRow, ɵTmTilesTree, ɵTmTilesTreePlace } from './internal/tm-tiles-tree-seam';
