// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, DOCUMENT, inject, input, output } from '@angular/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

import { TmLocalization } from '../l10n/tm-localization';

// The route's own error views (spec 0024 §11), each 0023's empty state,
// inline where the page would be: a page whose chunk failed to load
// (`client-outdated` or `offline`), a `tmPage` the user may not open
// (`forbidden`, reason `no-grant`), and a path no route matches
// (`not-found`).

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
    <!-- The heading, the message, and the view's one action. -->
    <tm-empty-state [heading]="title()" [tone]="view().tone ?? 'error'" live="polite">
      {{ message() }}
      @if (view().action; as action) {
        <button tmButton variant="primary" tmEmptyStateActions type="button" (click)="act(action[0])">{{ label() }}</button>
      }
    </tm-empty-state>
  `,
})
export class TmRouteError {
  readonly #l10n = inject(TmLocalization);
  readonly #document = inject(DOCUMENT);

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

  /** The message. */
  protected readonly message = computed(() => this.#l10n.ɵtext(this.view().message, this.#params())());

  /** The action's label. */
  protected readonly label = computed(() => {
    const action = this.view().action;
    return action ? this.#l10n.ɵtext(action[1])() : '';
  });

  /**
   * Runs the view's action: a reload of the document, or a retry its host runs.
   *
   * @param action - The action.
   */
  protected act(action: 'reload' | 'retry'): void {
    if (action === 'reload') {
      this.#document.location.reload();
    } else {
      this.retry.emit();
    }
  }
}
