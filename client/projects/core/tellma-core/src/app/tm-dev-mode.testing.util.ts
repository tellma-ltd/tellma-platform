// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Provider } from '@angular/core';

import { TM_DEV_MODE } from './tm-dev-mode';

// The development-mode seam for specs, including the specs of other entry
// points, which reach it as a test rig: a spec that sets production mode
// reaches the lenient half of a check that throws in development.

/**
 * Provides whether the app runs in development mode, as a spec sets it.
 *
 * @param dev - Whether the app runs in development mode.
 * @returns The provider.
 */
export function tmDevModeProvider(dev: boolean): Provider {
  return { provide: TM_DEV_MODE, useValue: () => dev };
}
