// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, Injector, input, output, signal, type Signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TmButton } from '@tellma/core-ui/button';
import { TmReference } from '@tellma/core-ui/copy';
import { TmEmptyState } from '@tellma/core-ui/empty-state';
import { TmLocalization, TmSession, TmUrls } from '@tellma/core';

import { TmErrorUpdates } from './tm-error-updates';
import { TM_VIEW_ERRORS, type TmErrorAction, type TmErrorView } from './tm-error-view';

// The inline error state (spec 0024 §11): 0023's empty state with the view's
// words, the server's items, the reference and the raw permission pair for
// support, and the view's actions after the host's way out, such as a
// details page's link to its list. It runs its actions itself, but Try
// again, which is its host's: Reload gets the new version first, the way the
// route's own error views do, sign-in and a step-up's Continue sign in
// again, and Switch tenant goes to the picker. The tenant-state page that
// replaces the shell's main area for a suspended tenant is this state too.

/** The label key of each action the inline state shows. */
const LABELS: Readonly<Partial<Record<TmErrorAction, string>>> = {
  retry: 'tm.errors.retry',
  reload: 'tm.errors.reload',
  'sign-in': 'tm.shell.signedOut.signIn',
  continue: 'tm.errors.stepUp.continue',
  'switch-tenant': 'tm.errors.suspended.switch',
};

/** An error, inline where the content would be: for a read that failed, and for a tenant that is suspended. */
@Component({
  selector: 'tm-error-state',
  imports: [RouterLink, TmButton, TmEmptyState, TmReference],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The words, the server's items, the reference and the permission's raw pair. -->
    <tm-empty-state
      [heading]="view().title()"
      [icon]="view().icon"
      [tone]="view().tone === 'error' ? 'error' : 'neutral'"
      [size]="size()"
      live="polite"
    >
      @if (message(); as message) {
        <p class="tm-error-state__message">{{ message }}</p>
      }
      @if (view().items().length > 0) {
        <ul class="tm-error-state__items">
          @for (item of view().items(); track $index) {
            <li>{{ item }}</li>
          }
        </ul>
      }
      @if (view().permission; as permission) {
        <p class="tm-error-state__permission"><bdi class="tm-error-state__code" dir="ltr">{{ permission }}</bdi></p>
      }
      @if (view().traceId; as traceId) {
        <tm-reference class="tm-error-state__reference" [value]="traceId" />
      }
      <!-- The host's way out, the main action; then the view's, the main one first; Reload anyway after a second failed download. -->
      @if (link(); as wayOut) {
        <a tmButton tmEmptyStateActions variant="primary" [routerLink]="wayOut.url">{{ wayOut.label() }}</a>
      }
      @for (action of actions(); track action; let first = $first) {
        <button
          tmButton
          tmEmptyStateActions
          type="button"
          [variant]="first && !link() ? 'primary' : 'secondary'"
          [pending]="action === 'reload' && pending()"
          [attr.aria-label]="action === 'reload' && pending() ? updating() : null"
          (click)="act(action)"
        >
          {{ label(action) }}
        </button>
      }
      @if (failures() > 1) {
        <button tmButton tmEmptyStateActions type="button" (click)="reloadAnyway()">{{ reloadAnywayLabel() }}</button>
      }
    </tm-empty-state>
  `,
  styles: `
    .tm-error-state__items {
      margin: 0;
      padding-inline-start: var(--space-4);
      text-align: start;
    }
    .tm-error-state__code {
      font-family: var(--font-mono);
    }
  `,
})
export class TmErrorState {
  readonly #injector = inject(Injector);
  readonly #l10n = inject(TmLocalization);
  readonly #updates = inject(TmErrorUpdates);

  /** The view, as the error presenter describes it. */
  readonly view = input.required<TmErrorView>();

  /** `page` centres the state in the remaining height; `panel` is the compact form. Default `page`. */
  readonly size = input<'page' | 'panel'>('page');

  /** The host's way out, shown first as the main action, such as a details page's link to its list; `null` for none. */
  readonly link = input<{ readonly label: Signal<string>; readonly url: string } | null>(null);

  /** Emits when the user asks to try again. */
  readonly retry = output();

  /** How many times Reload failed to download the update. */
  protected readonly failures = signal(0);

  /** Whether Reload is getting the update. */
  protected readonly pending = signal(false);

  /** The message: the view's, or the failed download's. */
  protected readonly message = computed(() =>
    this.failures() > 0 ? this.#l10n.ɵtext('tm.errors.updateFailed')() : this.view().message(),
  );

  /** The actions the state shows: the view's, but Close, which belongs to a dialog. */
  protected readonly actions = computed(() =>
    this.view()
      .actions()
      .filter((action) => LABELS[action] !== undefined),
  );

  /** Reload's accessible name while it gets the update. */
  protected readonly updating = this.#l10n.ɵtext('tm.errors.updating');

  /** The label of Reload anyway. */
  protected readonly reloadAnywayLabel = this.#l10n.ɵtext('tm.errors.reloadAnyway');

  /**
   * An action's label.
   *
   * @param action - The action.
   * @returns The label, in the user's language.
   */
  protected label(action: TmErrorAction): string {
    return this.#l10n.ɵtext(LABELS[action] ?? '')();
  }

  /**
   * Runs an action: Try again is the host's; Reload gets the new version and
   * reloads into it; sign-in and Continue sign in again; Switch tenant goes
   * to the picker.
   *
   * @param action - The action.
   */
  protected async act(action: TmErrorAction): Promise<void> {
    switch (action) {
      case 'retry':
        this.retry.emit();
        return;
      case 'reload':
        return this.#reload();
      case 'sign-in':
        await this.#injector.get(TmSession).signIn();
        return;
      case 'continue': {
        const challenge = TM_VIEW_ERRORS.get(this.view())?.challenge;
        await this.#injector.get(TmSession).signIn(challenge ? { stepUp: challenge } : {});
        return;
      }
      case 'switch-tenant':
        await this.#injector.get(Router).navigateByUrl(this.#injector.get(TmUrls).picker());
        return;
    }
  }

  /**
   * Runs an action as a click on its button would. Internal: the route's
   * error view hands it a Reload clicked before this state rendered.
   *
   * @param action - The action.
   * @returns A promise that settles when the action has.
   */
  ɵact(action: TmErrorAction): Promise<void> {
    return this.act(action);
  }

  /** Reloads as the browser would: the degraded worker sends the load to the network. */
  protected reloadAnyway(): void {
    this.#updates.reload();
  }

  /** Reload: the button turns busy, and a failed download says so and keeps the state. */
  async #reload(): Promise<void> {
    this.pending.set(true);
    const result = await this.#updates.update();
    // On `ready` and `disabled` the page is reloading already.
    if (result !== 'ready' && result !== 'disabled') {
      this.pending.set(false);
      this.failures.update((failures) => failures + 1);
    }
  }
}
