// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Signal } from '@angular/core';

import type { TmLazyComponent } from '../declarations/tm-declaration-types';

// An app-bar slot (spec 0027 §7.2): an icon button in the tenant shell's app
// bar that opens a panel. All features together have at most three, and the
// ids `help` and `assistant` belong to the platform.

/**
 * A distribution's app-bar slot: an icon button with an optional count badge
 * that opens a lazily loaded panel.
 *
 * @example
 * ```ts
 * const approvals: TmShellSlot = {
 *   id: 'acme-approvals', icon: 'stamp', labelKey: 'Acme_Approvals',
 *   badge: () => inject(AcmeApprovals).pendingCount,
 *   panel: () => import('./approvals/approvals-panel'),
 * };
 * ```
 */
export interface TmShellSlot {
  /** The slot's id, unique across every feature; `help` and `assistant` are reserved. */
  readonly id: string;
  /** A registered icon name. */
  readonly icon: string;
  /** The string key of the button's accessible name. */
  readonly labelKey: string;
  /** Returns the badge's count, or `null` for none; called once, in the shell's injection context. */
  readonly badge?: () => Signal<number | null>;
  /** The panel the button opens, loaded on first open. */
  readonly panel: TmLazyComponent;
}
