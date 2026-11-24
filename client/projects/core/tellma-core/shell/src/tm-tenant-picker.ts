// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterNextRender, ChangeDetectionStrategy, Component, inject, type Signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TmLaunchCache, TmLocalization, type TmMembership, TmUrls } from '@tellma/core';
import { TmBannerHost } from '@tellma/core/attention';
import { TmSkeleton } from '@tellma/core-ui/skeleton';

// The tenant picker's stand-in (spec 0024 §9.2; spec 0027 §6.2): the banner
// row, then the user's memberships from the cache, revalidated in the
// background, each a link into its tenant. It never redirects, so a stale
// membership cannot loop.

/** The tenant picker: the banner row, then the user's tenants, each a link to its home. */
@Component({
  selector: 'tm-tenant-picker',
  imports: [RouterLink, TmSkeleton, TmBannerHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The banner row, above the page. -->
    <tm-banner-host />
    <main class="tm-tenant-picker">
      <h1 tmPageTitle tabindex="-1">{{ title() }}</h1>
      <!-- The cached memberships, else skeleton rows while they are read. -->
      @if (tenants(); as tenants) {
        <ul>
          @for (tenant of tenants; track tenant.id) {
            <li><a [routerLink]="urls.forTenant(tenant.id).home()">{{ tenant.name }}</a></li>
          }
        </ul>
      } @else {
        <tm-skeleton shape="line" [lines]="4" />
      }
    </main>
  `,
  styles: `
    .tm-tenant-picker {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      max-inline-size: 40rem;
      margin-inline: auto;
      padding: var(--space-6) var(--space-4);
    }
  `,
})
export class TmTenantPicker {
  readonly #cache = inject(TmLaunchCache);

  /** The heading. */
  protected readonly title = inject(TmLocalization).ɵtext('tm.shell.tenantPicker.title');

  /** The app's URLs. */
  protected readonly urls = inject(TmUrls);

  /** The user's memberships, or `undefined` before the first read. */
  protected readonly tenants: Signal<readonly TmMembership[] | undefined> = this.#cache.tenants;

  /** Creates the picker, which revalidates the memberships in the background once it has rendered. */
  constructor() {
    // The picker renders from the cache first: a launch's first request starts
    // after its shell renders (spec 0024 §14).
    afterNextRender(() => void this.#cache.revalidateUser());
  }
}
