// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, inject, Injector, input, output, signal, viewChild, ViewContainerRef, type OnInit } from '@angular/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

import { TM_ERRORS_LOAD } from '../api/tm-api-seams';
import { TmLocalization } from '../l10n/tm-localization';
import { TM_SESSION_HOST } from '../session/tm-session-host';

// The route's own error views (spec 0024 §11), each 0023's empty state,
// inline where the page would be: a page whose chunk failed to load
// (`client-outdated` or `offline`), a `tmPage` the user may not open
// (`forbidden`, reason `no-grant`), and a path no route matches
// (`not-found`). Reload downloads the new version first (§11): the
// button turns busy, a failed download says so and keeps the view, and from
// the second failure "Reload anyway" reloads as the browser would. That flow
// is the error views' inline state, in its own lazy entry point, which a
// Reload view loads at once and renders in its place; an entry point that
// cannot load leaves a plain reload, which no worker is in the way of.

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
    <!-- The heading, the message and the view's action, until the error views' own state replaces a Reload view. -->
    @if (!replaced()) {
      <tm-empty-state [heading]="title()" [tone]="view().tone ?? 'error'" live="polite">
        {{ message() }}
        @if (view().action; as action) {
          <ng-container tmEmptyStateActions>
            <button tmButton variant="primary" type="button" (click)="act(action[0])">{{ label() }}</button>
          </ng-container>
        }
      </tm-empty-state>
    }
    <!-- Where the error views' state renders. -->
    <ng-container #state />
  `,
})
export class TmRouteError implements OnInit {
  readonly #l10n = inject(TmLocalization);
  readonly #injector = inject(Injector);
  /** Where the error views' state renders. */
  private readonly slot = viewChild.required('state', { read: ViewContainerRef });

  /** The error views' state that replaced a Reload view, or `null` when their entry point did not load. */
  #state: Promise<{ ɵact(action: 'reload'): Promise<void> } | null> | undefined;

  /** Which view. */
  readonly view = input.required<TmRouteErrorView>();

  /** The view's placeholders beyond `{displayName}`, such as the permission a `forbidden` view names. */
  readonly params = input<Readonly<Record<string, string>>>({});

  /** Emits when the user asks to try again. */
  readonly retry = output();

  /** Whether the error views' own state replaced this view. */
  protected readonly replaced = signal(false);

  /** The view's placeholders. */
  readonly #params = computed(() => ({ displayName: this.#l10n.displayName(), ...this.params() }));

  /** The heading. */
  protected readonly title = computed(() => this.#l10n.ɵtext(this.view().title, this.#params())());

  /** The message. */
  protected readonly message = computed(() => this.#l10n.ɵtext(this.view().message, this.#params())());

  /** The action's label. */
  protected readonly label = computed(() => {
    const action = this.view().action;
    return action ? this.#l10n.ɵtext(action[1])() : '';
  });

  /**
   * Loads the error views for a Reload view: their state, whose Reload
   * downloads the new version first, replaces this one once it renders.
   */
  ngOnInit(): void {
    if (this.view().action?.[0] === 'reload') {
      this.#state = this.#injector
        .get(TM_ERRORS_LOAD)()
        .then((errors) => {
          const state = errors.ɵtmClientOutdatedState(this.slot());
          this.replaced.set(true);
          return state;
        })
        .catch(() => null);
    }
  }

  /**
   * Runs the view's action: a retry is its host's. A Reload clicked before
   * the error views' state replaced this view is handed to that state; when
   * their entry point cannot load, no worker can serve the new version
   * either, so a plain reload gets it.
   *
   * @param action - The action.
   */
  protected async act(action: 'reload' | 'retry'): Promise<void> {
    if (action === 'retry') {
      this.retry.emit();
      return;
    }
    const state = await this.#state;
    if (state) {
      await state.ɵact('reload');
    } else {
      this.#injector.get(TM_SESSION_HOST).location.reload();
    }
  }
}
