// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, DOCUMENT, inject, input, output } from '@angular/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

import { TmPlatformText } from '../app/tm-platform-text';

// The route's own error views (spec 0024 §11), each 0023's empty state,
// inline where the page would be. The two a failed chunk load shows
// (`client-outdated` and `offline`) live here, since they render when no
// chunk can load; the views of a `tmPage` the user may not open
// (`forbidden`) and of a path no route matches (`not-found`) render from the
// shell entry point, over the same component.

/** The error views a route renders when a chunk does not load. */
export type TmRouteErrorKind = 'client-outdated' | 'offline';

/** One error view: the keys and the English of its title and message, its one action, and its tone. */
export interface TmRouteErrorView {
  /** The title's key and its English. */
  readonly title: readonly [key: string, english: string];
  /** The message's key and its English. */
  readonly message: readonly [key: string, english: string];
  /** The action's name, its label's key and the label's English. */
  readonly action?: readonly [name: 'reload' | 'retry', key: string, english: string];
  /** The empty state's tone; `error` when absent. */
  readonly tone?: 'neutral' | 'error';
}

// The strings are worded as spec 0024 §11's table words them.
/** The views of a chunk that did not load. */
export const TM_ROUTE_ERROR_VIEWS: Readonly<Record<TmRouteErrorKind, TmRouteErrorView>> = {
  'client-outdated': {
    title: ['tm.errors.clientOutdated.title', '{displayName} was updated'],
    message: ['tm.errors.clientOutdated.message', 'Reload to continue. Your unsaved changes come back after the reload.'],
    action: ['reload', 'tm.errors.reload', 'Reload'],
  },
  offline: {
    title: ['tm.errors.offline.title', "You're offline"],
    message: ['tm.errors.offline.message', "Check your connection. We'll keep trying."],
    action: ['retry', 'tm.errors.retry', 'Try again'],
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
  readonly #text = inject(TmPlatformText);
  readonly #document = inject(DOCUMENT);

  /** Which view. */
  readonly view = input.required<TmRouteErrorView>();

  /** The view's placeholders beyond `{displayName}`, such as the permission a `forbidden` view names. */
  readonly params = input<Readonly<Record<string, string>>>({});

  /** Emits when the user asks to try again. */
  readonly retry = output();

  /** The view's placeholders. */
  readonly #params = computed(() => ({ displayName: this.#text.displayName(), ...this.params() }));

  /** The heading. */
  protected readonly title = computed(() => this.#text.text(...this.view().title, this.#params())());

  /** The message. */
  protected readonly message = computed(() => this.#text.text(...this.view().message, this.#params())());

  /** The action's label. */
  protected readonly label = computed(() => {
    const action = this.view().action;
    return action ? this.#text.text(action[1], action[2])() : '';
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
