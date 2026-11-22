// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TmLocalization, TmSession } from '@tellma/core';
import { TmButton } from '@tellma/core-ui/button';

// The signed-out page's stand-in (spec 0024 §9.2; spec 0027 §6.3): the one
// route without the session guard. It sends nothing; Sign in returns to `/`,
// where the sign-in may land on a tenant.

/** The signed-out page: a heading and Sign in. */
@Component({
  selector: 'tm-signed-out-page',
  imports: [TmButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The page's heading and its one action. -->
    <main class="tm-signed-out-page">
      <h1 tmPageTitle tabindex="-1">{{ title() }}</h1>
      <button tmButton variant="primary" type="button" (click)="signIn()">{{ signInLabel() }}</button>
    </main>
  `,
  styles: `
    .tm-signed-out-page {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-4);
      padding: var(--space-6) var(--space-4);
    }
  `,
})
export class TmSignedOutPage {
  readonly #session = inject(TmSession);
  readonly #l10n = inject(TmLocalization);

  /** The heading. */
  protected readonly title = this.#l10n.ɵtext('tm.shell.signedOut.title');

  /** The action's label. */
  protected readonly signInLabel = this.#l10n.ɵtext('tm.shell.signedOut.signIn');

  /** Leaves for sign-in, returning to `/`. */
  protected signIn(): void {
    void this.#session.signIn({ returnUrl: '/' });
  }
}
