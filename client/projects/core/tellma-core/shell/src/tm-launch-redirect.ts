// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { TmLaunch, TmLaunchCache, ɵtmLaunchTarget } from '@tellma/core';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

// The launch page at `/` (spec 0024 §9.3): the launch route renders it only
// when the user's memberships are not cached, so its `canMatch` could not
// decide. It shows the picker's skeleton rows while `/bff/user` is read, then
// replaces `/` with the tenant or the picker.

/**
 * The page at `/` while the user's memberships are read: the picker's
 * skeleton rows, then a replace to the tenant or the picker.
 */
@Component({
  selector: 'tm-launch-redirect',
  imports: [TmSkeleton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The picker's list geometry, while the memberships are read. -->
    <main class="tm-launch-redirect" aria-busy="true">
      <tm-skeleton shape="line" width="16rem" />
      <tm-skeleton shape="line" [lines]="4" />
    </main>
  `,
  styles: `
    .tm-launch-redirect {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      max-inline-size: 40rem;
      margin-inline: auto;
      padding: var(--space-6) var(--space-4);
    }
  `,
})
export class TmLaunchRedirect {
  /** Reads the memberships, then replaces `/` with the decision. */
  constructor() {
    const router = inject(Router);
    const cache = inject(TmLaunchCache);
    const launch = inject(TmLaunch);
    // A failed read leaves the memberships unknown, which leads to the picker and its error state.
    void cache.revalidateUser().then(() => router.navigateByUrl(ɵtmLaunchTarget(untracked(cache.tenants), launch), { replaceUrl: true }));
  }
}
