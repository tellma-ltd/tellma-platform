// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken, isDevMode } from '@angular/core';

// Whether the app runs in development mode. Platform code that is strict in
// development and lenient in production reads it, so a spec reaches the
// production half.

/**
 * Whether the app runs in development mode: `isDevMode` unless a spec
 * provides another answer. Internal.
 */
export const TM_DEV_MODE = new InjectionToken<() => boolean>('TM_DEV_MODE', {
  providedIn: 'root',
  factory: () => isDevMode,
});
