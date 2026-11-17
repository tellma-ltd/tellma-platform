// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component } from '@angular/core';

/**
 * Acme's root component, in the `tm-app` element every distribution's
 * `index.html` holds. It stands in for the framework's own root component,
 * which `bootstrapTellma` will start in its place, and shows only the
 * distribution's name.
 */
@Component({
  // The boot rules require this element of every distribution, so the
  // selector cannot carry Acme's prefix.
  // eslint-disable-next-line @angular-eslint/component-selector -- the element every distribution's index.html holds
  selector: 'tm-app',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The page's one landmark and heading. -->
    <main class="acme-app">
      <h1>Acme</h1>
    </main>
  `,
  styles: `
    .acme-app {
      padding: var(--space-4);
    }
  `,
})
export class AcmeApp {}
