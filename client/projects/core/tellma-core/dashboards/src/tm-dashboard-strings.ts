// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmStringTree } from '@tellma/core';

// The home page's and the dashboard grid's English strings (spec 0035 §8.6,
// §9.2, §15.2, Appendix A), merged under `tm.home` and `tm.dashboard` when
// the entry point's first component is created. The home page's title,
// "Home", stays with the primary entry point, since the route sets the
// document's title before this chunk loads. A language pack carries the
// same trees; the Arabic one is @tellma/locale-ar/dashboards-strings, which
// also holds the title's Arabic, as its `home` scope is the whole of it.

/** The home page's English strings, merged under `tm.home` beside the primary's `title`. */
export const TM_HOME_STRINGS_EN = {
  greeting: {
    morning: 'Good morning, {name}',
    afternoon: 'Good afternoon, {name}',
    evening: 'Good evening, {name}',
  },
  grid: 'Your work',
} as const satisfies TmStringTree;

/** The dashboard grid's English strings, merged under `tm.dashboard`. */
export const TM_DASHBOARD_STRINGS_EN = {
  unavailable: "This widget isn't available.",
  loadFailed: "This widget couldn't be loaded.",
  recents: {
    title: 'Recently visited',
    empty: 'Pages you visit show up here',
  },
} as const satisfies TmStringTree;
