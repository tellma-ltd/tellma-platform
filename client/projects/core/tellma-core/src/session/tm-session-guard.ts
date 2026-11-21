// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject } from '@angular/core';
import type { CanActivateFn } from '@angular/router';

import { TmSession } from './tm-session';
import { TmSessionPaths } from './tm-session-paths';

// The session guard (spec 0024 §8.2). It sits on every route but the
// signed-out page and reads only the profile cookie, so it never waits on the
// network.

/**
 * Allows a route while the profile cookie names the tab's user. With no
 * session it runs the expiry path, and with another user's session the
 * changed-user path; either way it denies.
 *
 * @example
 * ```ts
 * const routes: Routes = [{ path: ':tenantId', canActivate: [tmSessionGuard], children }];
 * ```
 */
export const tmSessionGuard: CanActivateFn = () => {
  const paths = inject(TmSessionPaths);
  switch (inject(TmSession).check()) {
    case 'same':
      return true;
    case 'missing':
      paths.expired('cookie');
      return false;
    case 'changed':
      paths.changedUser();
      return false;
  }
};
