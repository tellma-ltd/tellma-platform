// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken, signal, type Signal } from '@angular/core';

/**
 * Ambient grid context, provided once per app — the same shape as the UI
 * message context (a plain object carrying signal fields). The app runs under a
 * single tenant at any moment and no two grids in the DOM host different
 * tenants' data, so a single ambient signal serves every grid.
 */
export interface TmGridContext {
  /**
   * The current tenant's stable id. Stamped into clipboard metadata and used
   * as the cross-tenant paste guard: raw values pasted from another grid are
   * trusted only when the source `tenantId` matches this one; otherwise the
   * pasted labels re-parse or re-resolve, so raw ids never cross tenants.
   */
  readonly tenantId: Signal<string | undefined>;
}

/**
 * DI seam for {@link TmGridContext}. Override it at the app root to feed every
 * grid the live tenant id:
 *
 * ```ts
 * providers: [{ provide: TM_GRID_CONTEXT, useValue: { tenantId: myTenantId } }]
 * ```
 *
 * The default carries `tenantId: undefined` (no cross-tenant guard).
 */
export const TM_GRID_CONTEXT = new InjectionToken<TmGridContext>('TM_GRID_CONTEXT', {
  providedIn: 'root',
  factory: () => ({ tenantId: signal<string | undefined>(undefined).asReadonly() }),
});
