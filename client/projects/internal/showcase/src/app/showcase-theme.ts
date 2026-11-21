// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmThemeSetting } from '@tellma/core-ui';

/**
 * The showcase's theme setting from its `?theme=` query value. Only an
 * explicit `system` follows the OS preference: an absent or unknown value
 * is light, so every story renders the same on every machine unless a test
 * asks for the OS preference on purpose.
 */
export function showcaseThemeFromQuery(value: string | null): TmThemeSetting {
  return value === 'dark' || value === 'system' ? value : 'light';
}
