// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Provider } from '@angular/core';

import { TmPageActions } from './tm-page-actions';
import { TmPageContexts } from './tm-page-contexts';
import { TmPageScope } from './tm-page-registry';

// The page scope (spec 0026 §9.2, §9.4). Every routed page and every
// context-bearing dialog provides one: the injector region that owns its
// page context and the actions registered inside it. The scope gives the
// region its own `TmPageContexts` and `TmPageActions`, bound to it, so a
// dialog's action never lands on the page beneath.

/**
 * The providers of one page scope, for a routed page's host or a dialog that
 * publishes its own page context (`overlay: true`). Inside it,
 * `TmPageContexts.register` and `TmPageActions.register` belong to the
 * scope, and everything registered in it ends with it.
 *
 * @returns The providers.
 *
 * @example
 * ```ts
 * @Component({ providers: [provideTmPageScope()] })
 * ```
 */
export function provideTmPageScope(): Provider[] {
  return [TmPageScope, TmPageContexts, TmPageActions];
}
