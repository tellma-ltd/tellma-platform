/**
 * Public API Surface of @tellma/core-ui/tree-grid — the tm-tree-grid
 * hierarchical data grid: the same spreadsheet-grade grid surface as
 * tm-grid (virtualization, Excel-style selection, clipboard, undo, state
 * memory) rendered over a flat adjacency-list rows array. A `parentId`
 * accessor derives the hierarchy; rows expand and collapse with optional
 * lazy child loading, and row operations are subtree-aware.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.
export * from './tm-tree-grid';
