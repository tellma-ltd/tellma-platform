// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Provider } from '@angular/core';

// The page-scope seam (spec 0026 §9.2, §9.4). Every routed page and every
// context-bearing dialog provides a page scope: the injector region that owns
// its page context and the actions registered inside it. Until the page
// context registry is provided, a scope holds nothing of its own.

/**
 * The providers of one page scope, for the providers of a routed page's host
 * or of a dialog that publishes its own page context.
 *
 * @example
 * ```ts
 * @Component({ providers: [provideTmPageScope()] })
 * ```
 */
export function provideTmPageScope(): Provider[] {
  return [];
}
