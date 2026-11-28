// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Type } from '@angular/core';
import type { ɵTmShellComponents } from '@tellma/core';

import { TmPagePlaceholder } from './tm-page-placeholder';
import { TmTenantShell } from './tm-tenant-shell';

// The components the primary entry point loads from this one, by name, each
// through its loader. The primary cannot import this entry point, so it
// names the components in a contract of its own; `satisfies` checks this map
// against it in this entry point's build. The tenantless pages live in a
// chunk of their own, which only `/tenants` and `/signed-out` load.

/** Loads the tenantless pages' chunk. */
const tenantless = (): Promise<typeof import('./tenantless/tm-tenantless')> => import('./tenantless/tm-tenantless');

/** The shell's components' loaders, by the names the primary entry point loads them by. */
export const TM_SHELL = {
  TmTenantShell: (): Promise<Type<unknown>> => Promise.resolve(TmTenantShell),
  TmTenantPicker: (): Promise<Type<unknown>> => tenantless().then((chunk) => chunk.TmTenantPicker),
  TmSignedOutPage: (): Promise<Type<unknown>> => tenantless().then((chunk) => chunk.TmSignedOutPage),
  TmPagePlaceholder: (): Promise<Type<unknown>> => Promise.resolve(TmPagePlaceholder),
} as const satisfies ɵTmShellComponents;
