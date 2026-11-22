// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { ɵTmShellComponents } from '@tellma/core';

import { TmForbiddenView } from './tm-forbidden-view';
import { TmLaunchRedirect } from './tm-launch-redirect';
import { TmNotFoundPage } from './tm-not-found-page';
import { TmPagePlaceholder } from './tm-page-placeholder';
import { TmSignedOutPage } from './tm-signed-out-page';
import { TmTenantPicker } from './tm-tenant-picker';
import { TmTenantShell } from './tm-tenant-shell';

// The components the primary entry point loads from this one, by name. The
// primary cannot import this entry point, so it names the components in a
// contract of its own; `satisfies` checks this map against it in this entry
// point's build.

/** The shell's components, by the names the primary entry point loads them by. */
export const TM_SHELL = {
  TmTenantShell,
  TmTenantPicker,
  TmSignedOutPage,
  TmLaunchRedirect,
  TmNotFoundPage,
  TmForbiddenView,
  TmPagePlaceholder,
} as const satisfies ɵTmShellComponents;
