// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The types of Lucide's per-icon modules, published as
// `@tellma/core-tools/lucide-icons`. The `lucide` package types only its
// index, and an import of the index reaches every icon's module, so a bundler
// that splits code puts each icon some lazy chunk draws in a chunk the first
// frame loads too. Importing each icon from its own module keeps it with the
// code that draws it; a file that does so names these types once:
//
//   /// <reference types="@tellma/core-tools/lucide-icons" />
//   import HandCoins from 'lucide/dist/esm/icons/hand-coins.mjs';
//
// `lucide` is resolved from the project that references these types, which
// depends on it to import the icons at all.

/**
 * One Lucide icon's module, such as `lucide/dist/esm/icons/hand-coins.mjs`:
 * its default export is the icon's data, the same value the `lucide` index
 * exports under the icon's name.
 */
declare module 'lucide/dist/esm/icons/*.mjs' {
  import type { IconNode } from 'lucide';

  /** The icon's data: the SVG children of one 24×24 glyph, as `[tag, attributes]` pairs. */
  const node: IconNode;
  export default node;
}
