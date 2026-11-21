// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

// The app's clock (spec 0024 §2.2). Platform code that stamps or compares
// times reads it, so a test replaces it with a fake clock.

/**
 * The current time in epoch milliseconds: `Date.now` unless a test provides
 * a fake clock.
 *
 * @example
 * ```ts
 * const now = inject(TM_NOW)();
 * ```
 */
export const TM_NOW = new InjectionToken<() => number>('TM_NOW', {
  providedIn: 'root',
  factory: () => Date.now,
});
