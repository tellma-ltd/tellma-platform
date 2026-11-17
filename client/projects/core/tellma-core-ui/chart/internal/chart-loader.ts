// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';
import type { ɵTmChartEngineFactory } from './chart-plot';

// The engine's one way in: a dynamic import, so the bundler splits ECharts
// and the adapter into their own chunk, which loads with the page's first
// plot. A token rather than a bare import() lets the chart's tests stand in
// a loader that fails, throws or never settles.

/**
 * Loads the engine chunk and resolves to its instance factory. The default
 * imports the chart's adapter; only tests provide another.
 */
export const ɵTM_CHART_ENGINE_LOADER = new InjectionToken<() => Promise<ɵTmChartEngineFactory>>(
  'ɵTM_CHART_ENGINE_LOADER',
  {
    providedIn: 'root',
    factory: () => () => import('./chart-engine').then((chunk) => chunk.ɵtmCreateChartEngine),
  },
);
