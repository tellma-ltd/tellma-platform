/**
 * Tree mode for `tm-tiles`: `TmTilesTree`, applied with `[parentId]`, makes
 * the tiles a single-column tree over their flat rows, with two-way
 * expansion and children that load lazily in batches. A page imports it
 * only where its tiles are a tree, so a flat list never downloads it.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmTilesTree } from './tm-tiles-tree';
