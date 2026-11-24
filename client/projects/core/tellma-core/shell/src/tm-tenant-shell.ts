// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TmArrivals, TmBannerHost } from '@tellma/core/attention';

// The tenant shell's stand-in (spec 0024 §9.2; spec 0027 §7): the `:t`
// route's component, holding the banner row and the tenant's pages in the
// document's one main landmark. It starts the arrival engine, which toasts
// what arrives in the tenant's attention documents (spec 0028 §8.2). The
// shell with its app bar, pinned bar and markers replaces it.

/** The tenant shell: the banner row, then the routed tenant page in the document's main landmark. */
@Component({
  selector: 'tm-tenant-shell',
  imports: [RouterOutlet, TmBannerHost],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The banner row, above the page. -->
    <tm-banner-host />
    <!-- The one scrolling region, which navigation focus falls back to. -->
    <main class="tm-tenant-shell__main">
      <router-outlet />
    </main>
  `,
  styles: `
    :host {
      display: block;
    }
  `,
})
export class TmTenantShell {
  /** Creates the shell, which starts the arrival engine for the tenant's life in the tab. */
  constructor() {
    inject(TmArrivals);
  }
}
