// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { provideTmPageScope, TmLocalization, TmPageContexts, TmSession } from '@tellma/core';
import { TmButton } from '@tellma/core-ui/button';

import { TmTenantlessShell } from './tm-tenantless-shell';
import { tmRegisterTenantlessStrings } from './tm-tenantless-strings';

// The signed-out page (spec 0027 §6.3; spec 0024 §8.3, §9.2): the one route
// without the session guard, in the tenantless shell with the wordmark
// alone. It calls no API. Sign in returns to `/`, where the platform picks
// the landing. A profile cookie that still names a user (a sign-in in
// another tab) adds who is signed in and Continue to `/`.

/** The signed-out page: its heading, the message and Sign in; Continue while a profile cookie names a user. */
@Component({
  selector: 'tm-signed-out-page',
  imports: [RouterLink, TmButton, TmTenantlessShell],
  providers: [provideTmPageScope()],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <tm-tenantless-shell signedOut>
      <div class="tm-signed-out-page">
        <h1 tmPageTitle tabindex="-1">{{ title() }}</h1>
        <p>{{ message() }}</p>
        <!-- With a valid profile cookie: who is signed in, and Continue to the landing. -->
        @if (signedInAs(); as signedInAs) {
          <p>{{ signedInAs }}</p>
        }
        <div class="tm-signed-out-page__actions">
          <button tmButton variant="primary" (click)="signIn()">{{ signInLabel() }}</button>
          @if (signedInAs()) {
            <a tmButton routerLink="/">{{ continueLabel() }}</a>
          }
        </div>
      </div>
    </tm-tenantless-shell>
  `,
  styles: `
    :host {
      display: contents;
    }

    .tm-signed-out-page {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-4);
      max-inline-size: 40rem;
      margin-inline: auto;
      padding: var(--space-8) var(--space-4);
      color: var(--text-body);
    }

    .tm-signed-out-page h1 {
      margin: 0;
      color: var(--text-strong);
      font-size: var(--text-lg);
    }

    .tm-signed-out-page p {
      margin: 0;
    }

    .tm-signed-out-page__actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
})
export class TmSignedOutPage {
  readonly #session = inject(TmSession);
  readonly #l10n = inject(TmLocalization);

  /** The heading. */
  protected readonly title = this.#l10n.ɵtext('tm.tenantless.signedOut.title');

  /** The message, which names the distribution. */
  protected readonly message = computed(() =>
    this.#l10n.ɵtext('tm.tenantless.signedOut.message', { displayName: this.#l10n.displayName() })(),
  );

  /** Who the profile cookie names, or `null` without one. */
  protected readonly signedInAs = computed(() => {
    const profile = this.#session.profile();
    return profile === null ? null : this.#l10n.ɵtext('tm.tenantless.signedOut.signedInAs', { name: profile.name })();
  });

  /** Sign in's label. */
  protected readonly signInLabel = this.#l10n.ɵtext('tm.shell.signedOut.signIn');

  /** Continue's label. */
  protected readonly continueLabel = this.#l10n.ɵtext('tm.tenantless.signedOut.continue');

  /** Creates the page, which registers its page context. */
  constructor() {
    tmRegisterTenantlessStrings(this.#l10n);
    // Spec 0027 §14: a custom context for `/signed-out`, with nothing to read or do.
    inject(TmPageContexts).register({
      kind: 'custom',
      routeTemplate: '/signed-out',
      title: this.title,
      status: signal('ready' as const),
      state: computed(() => ({ signedIn: this.#session.profile() !== null })),
      errors: signal([]),
      actions: signal([]),
    });
  }

  /** Leaves for sign-in, returning to `/`. */
  protected signIn(): void {
    void this.#session.signIn({ returnUrl: '/' });
  }
}
