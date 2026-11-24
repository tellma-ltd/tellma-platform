// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TmArrivals } from '@tellma/core/attention';

import { TmShellFrame } from './frame/tm-shell-frame';

// The tenant shell's stand-in (spec 0024 §9.2; spec 0027 §7): the `:t`
// route's component, the shell frame around the tenant's pages, with the
// banner row in its header and the routed page in its `main`. It starts the
// arrival engine, which toasts what arrives in the tenant's attention
// documents (spec 0028 §8.2). The shell with its app bar, pinned bar and
// markers replaces it; until then the frame shows neither row.

/** The tenant shell: the shell frame, its banner row, and the routed tenant page in its main landmark. */
@Component({
  selector: 'tm-tenant-shell',
  imports: [RouterOutlet, TmShellFrame],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The frame: the banner row, then the routed page in the one scrolling region. -->
    <tm-shell-frame [appBar]="false">
      <router-outlet />
    </tm-shell-frame>
  `,
})
export class TmTenantShell {
  /** Creates the shell, which starts the arrival engine for the tenant's life in the tab. */
  constructor() {
    inject(TmArrivals);
  }
}
