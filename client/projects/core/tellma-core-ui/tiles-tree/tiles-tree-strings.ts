// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The tiles tree's built-in English strings, under the `tilesTree` key of
 * the library's strings (`tmUi.tilesTree.loadFailed` in a translation
 * table). They ship with the tree, not in the core table, and a locale pack
 * or the app's own table translates or overrides them as it does the core's.
 */
export const TM_TILES_TREE_STRINGS_EN = {
  // Announced when a batch of tree rows fails to load its children; the
  // rows collapse. The label lists the rows' titles.
  loadFailed: "Couldn't load {label}",
} as const;
