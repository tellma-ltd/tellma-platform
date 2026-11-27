// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The tiles view's own English strings, under `tm.searchTiles`. The tiles
// register them as their chunk first runs, so they ship with the tiles
// rather than with the search route. A locale pack's table holds their
// translations under the same keys.

/**
 * The tiles view's English strings, under `tm.searchTiles`. Internal: the
 * tiles register them.
 */
export const ɵTM_SEARCH_TILES_STRINGS_EN = {
  // A tile's badge of an activatable entity's IsActive.
  active: 'Active',
  inactive: 'Inactive',
} as const satisfies TmStringTree;
