// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

// The tenant shell's stand-in (spec 0024 §9.2; spec 0027 §7): the `:t`
// route's component, holding the tenant's pages in the document's one main
// landmark. The shell with its app bar, pinned bar and markers replaces it.

/** The tenant shell: the routed tenant page in the document's main landmark. */
@Component({
  selector: 'tm-tenant-shell',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
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
export class TmTenantShell {}
