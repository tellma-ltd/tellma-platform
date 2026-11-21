// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, InjectionToken } from '@angular/core';

import type { TmBootResult } from './tm-boot-types';

// What the boot script decided, as the app reads it (spec 0024 §4.2, §4.3).
// The script leaves it on `window.__tmBoot` before the app's code loads; the
// session and the launch mark seed themselves from it once, at their first
// injection.

/** The boot script's result on this document, or `null` when no boot script ran. */
export const TM_BOOT_RESULT = new InjectionToken<TmBootResult | null>('TM_BOOT_RESULT', {
  providedIn: 'root',
  factory: () => (inject(DOCUMENT).defaultView as (Window & { __tmBoot?: TmBootResult }) | null)?.__tmBoot ?? null,
});
