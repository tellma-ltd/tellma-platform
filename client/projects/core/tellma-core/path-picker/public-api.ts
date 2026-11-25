/**
 * Public API Surface of @tellma/core/path-picker — `tm-path-picker`, a root
 * entity's fields as a tree with navigations as branches, built from the
 * local descriptors: the column chooser's, the advanced filter's, the role
 * editor's and the report studio's way to pick, drag or insert a field.
 * Its hosts load it; no page's first frame does.
 *
 * @packageDocumentation
 */

// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

export { TmPathPicker } from './src/tm-path-picker';
export type { TmPathNode } from './src/tm-path-tree';
