// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

import { TM_API_UPDATES } from '../api/tm-api-seams';
import { TmLocalization } from '../l10n/tm-localization';

// The route's own error views (spec 0024 §11), each 0023's empty state,
// inline where the page would be: a page whose chunk failed to load
// (`client-outdated` or `offline`), a `tmPage` the user may not open
// (`forbidden`, reason `no-grant`), and a path no route matches
// (`not-found`). Reload downloads the new version first (§11): the button
// turns busy, a failed download says so and keeps the view, and from the
// second failure "Reload anyway" reloads as the browser would.

/** The error views a route renders when a chunk does not load. */
export type TmRouteErrorKind = 'client-outdated' | 'offline';

/** One error view: the keys of its title and message, its one action, and its tone. */
export interface TmRouteErrorView {
  /** The title's key. */
  readonly title: string;
  /** The message's key. */
  readonly message: string;
  /** The action's name and its label's key. */
  readonly action?: readonly [name: 'reload' | 'retry', key: string];
  /** The empty state's tone; `error` when absent. */
  readonly tone?: 'neutral' | 'error';
}

// The English strings, in the platform's English table, are worded as spec
// 0024 §11's table words them.
/** The route's error views: a chunk that did not load, a page the user may not open, and a path no route matches. */
export const TM_ROUTE_ERROR_VIEWS: Readonly<Record<TmRouteErrorKind | 'forbidden' | 'not-found', TmRouteErrorView>> = {
  'client-outdated': {
    title: 'tm.errors.clientOutdated.title',
    message: 'tm.errors.clientOutdated.message',
    action: ['reload', 'tm.errors.reload'],
  },
  offline: {
    title: 'tm.errors.offline.title',
    message: 'tm.errors.offline.message',
    action: ['retry', 'tm.errors.retry'],
  },
  forbidden: {
    title: 'tm.errors.forbidden.title',
    message: 'tm.errors.forbidden.message',
  },
  'not-found': {
    title: 'tm.errors.notFound.title',
    message: 'tm.errors.notFound.message',
    tone: 'neutral',
  },
};

/** A route's inline error view: a heading, a message and its one action. */
@Component({
  selector: 'tm-route-error',
  imports: [TmButton, TmEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The heading, the message, and the view's action; Reload anyway after a second failed download. -->
    <tm-empty-state [heading]="title()" [tone]="view().tone ?? 'error'" live="polite">
      {{ message() }}
      @if (view().action; as action) {
        <ng-container tmEmptyStateActions>
          <button
            tmButton
            variant="primary"
            type="button"
            [pending]="pending()"
            [attr.aria-label]="pending() ? updating() : null"
            (click)="act(action[0])"
          >
            {{ label() }}
          </button>
          @if (failures() > 1) {
            <button tmButton type="button" (click)="reload()">{{ reloadAnyway() }}</button>
          }
        </ng-container>
      }
    </tm-empty-state>
  `,
})
export class TmRouteError {
  readonly #l10n = inject(TmLocalization);
  readonly #updates = inject(TM_API_UPDATES);

  /** Which view. */
  readonly view = input.required<TmRouteErrorView>();

  /** The view's placeholders beyond `{displayName}`, such as the permission a `forbidden` view names. */
  readonly params = input<Readonly<Record<string, string>>>({});

  /** Emits when the user asks to try again. */
  readonly retry = output();

  /** The view's placeholders. */
  readonly #params = computed(() => ({ displayName: this.#l10n.displayName(), ...this.params() }));

  /** The heading. */
  protected readonly title = computed(() => this.#l10n.ɵtext(this.view().title, this.#params())());

  /** How many times Reload failed to download the update. */
  protected readonly failures = signal(0);

  /** Whether Reload is getting the update. */
  protected readonly pending = signal(false);

  /** The message: the view's, or the failed download's. */
  protected readonly message = computed(() =>
    this.#l10n.ɵtext(this.failures() > 0 ? 'tm.errors.updateFailed' : this.view().message, this.#params())(),
  );

  /** Reload's accessible name while it gets the update. */
  protected readonly updating = this.#l10n.ɵtext('tm.errors.updating');

  /** The label of Reload anyway. */
  protected readonly reloadAnyway = this.#l10n.ɵtext('tm.errors.reloadAnyway');

  /** The action's label. */
  protected readonly label = computed(() => {
    const action = this.view().action;
    return action ? this.#l10n.ɵtext(action[1])() : '';
  });

  /**
   * Runs the view's action: Reload gets the new version and reloads into
   * it, and a retry is its host's.
   *
   * @param action - The action.
   */
  protected async act(action: 'reload' | 'retry'): Promise<void> {
    if (action === 'retry') {
      this.retry.emit();
      return;
    }
    this.pending.set(true);
    const result = await this.#updates.update();
    // On `ready` and `disabled` the page is reloading already.
    if (result !== 'ready' && result !== 'disabled') {
      this.pending.set(false);
      this.failures.update((failures) => failures + 1);
    }
  }

  /** Reloads as the browser would: the degraded worker sends the load to the network. */
  protected reload(): void {
    this.#updates.reload();
  }
}
