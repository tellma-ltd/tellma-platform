// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmLazyComponent } from '../declarations/tm-declaration-types';

// The settings-tab hatch (spec 0032 §7.5): a feature replaces the generic body
// of one settings tab with its own component.

/**
 * Replaces the body of one settings tab with a feature's own component.
 *
 * @example
 * ```ts
 * const invoicing: TmSettingsTabDeclaration = {
 *   category: 'invoicing', component: () => import('./settings/invoicing-tab'),
 * };
 * ```
 */
export interface TmSettingsTabDeclaration {
  /** The setting category the tab shows, which the metadata must declare. */
  readonly category: string;
  /** The component that replaces the tab's generic body. */
  readonly component: TmLazyComponent;
}
