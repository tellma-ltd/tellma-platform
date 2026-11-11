// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The tenant memberships of the signed-in user, as `GET /bff/user` lists them
// (spec 0011 §5.5): each entry is the catalog's description of a tenant plus
// whether the user's membership there is active. A navigation hint, never an
// authorization: every entry into a tenant is verified by the tenant itself.

/** Whether a tenant holds a business's live books or is a sandbox. */
export type TmTenantCategory = 'Live' | 'Sandbox';

/** A tenant's lifecycle state. */
export type TmTenantState = 'Provisioning' | 'Active' | 'ReadOnly' | 'Frozen' | 'Suspended' | 'Retired';

/** One tenant the signed-in user may be shown, as the user document lists it. */
export interface TmMembership {
  /** The tenant's id. */
  readonly id: number;
  /** The tenant's name in its first language. */
  readonly name: string;
  /** The tenant's name in its second language, or `null`. */
  readonly name2: string | null;
  /** The tenant's name in its third language, or `null`. */
  readonly name3: string | null;
  /** The tenant's content languages, first to third. */
  readonly languages: readonly string[];
  /** Whether the tenant is live or a sandbox. */
  readonly category: TmTenantCategory;
  /** The tenant's lifecycle state. */
  readonly state: TmTenantState;
  /** A sandbox's live tenant, or `null`. */
  readonly liveTenantId: number | null;
  /** Whether the user's membership in the tenant is active. */
  readonly isActive: boolean;
}
