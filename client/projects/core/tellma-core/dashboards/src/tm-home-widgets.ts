// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmDashboardWidgetModel } from '@tellma/core';

// The home page's code-defined grid (spec 0035 §15.3): My assignments over
// Assigned by me, with Recently visited beside them, 7 : 5. Each list holds
// up to ten rows in its 350 px tile. The home page renders the widgets whose
// types the app declares, so a type that has not shipped leaves no hole.

/**
 * The home page's widgets, for everyone: My assignments, Recently visited
 * and Assigned by me.
 *
 * @example
 * ```html
 * <tm-dashboard-grid [widgets]="widgets" label="Your work" />
 * ```
 */
export const TM_HOME_WIDGETS: readonly TmDashboardWidgetModel[] = [
  { key: 'home.to-me', type: 'core.assignments-to-me', x: 0, y: 0, w: 7, h: 9 },
  { key: 'home.recents', type: 'core.recents', x: 7, y: 0, w: 5, h: 9 },
  { key: 'home.by-me', type: 'core.assignments-by-me', x: 0, y: 9, w: 7, h: 9 },
];
