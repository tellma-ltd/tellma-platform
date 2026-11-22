// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, DOCUMENT, inject, input, output } from '@angular/core';
import { TmButton } from '@tellma/core-ui/button';
import { TmEmptyState } from '@tellma/core-ui/empty-state';

import { TmPlatformText } from '../app/tm-platform-text';
import type { TmSecurableRef } from '../declarations/tm-declaration-types';

// The route's own error views (spec 0024 §11): a page whose chunk failed to
// load (`client-outdated` or `offline`), a `tmPage` the user may not open
// (`forbidden`, reason `no-grant`), and a path no route matches. Each is
// 0023's empty state, inline where the page would be.

/** The error views a route renders in place of its page. */
export type TmRouteErrorKind = 'client-outdated' | 'offline' | 'forbidden' | 'not-found';

/** One view's strings and action: the keys, their English, and the action's. */
interface TmRouteErrorStrings {
  readonly title: readonly [key: string, english: string];
  readonly message: readonly [key: string, english: string];
  readonly action?: readonly [name: 'reload' | 'retry', key: string, english: string];
}

// The strings are worded as spec 0024 §11's table words them.
/** Each view's strings: the key and the English fallback of its title, message and action. */
const VIEWS: Readonly<Record<TmRouteErrorKind, TmRouteErrorStrings>> = {
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
  forbidden: {
    title: ['tm.errors.forbidden.title', "You don't have permission for this"],
    message: ['tm.errors.forbidden.message', 'It needs “{permission}”. Ask your administrator to grant it.'],
  },
  'not-found': {
    title: ['tm.errors.notFound.title', 'Page not found'],
    message: ['tm.errors.notFound.message', 'Check the address, or go back to the start.'],
  },
};

/** A route's inline error view: a heading, a message and its one action. */
@Component({
  selector: 'tm-route-error',
  imports: [TmButton, TmEmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The heading, the message, and the view's one action. -->
    <tm-empty-state [heading]="title()" [tone]="kind() === 'not-found' ? 'neutral' : 'error'" live="polite">
      {{ message() }}
      @if (strings().action; as action) {
        <button tmButton variant="primary" tmEmptyStateActions type="button" (click)="act(action[0])">{{ label() }}</button>
      }
    </tm-empty-state>
  `,
})
export class TmRouteError {
  readonly #text = inject(TmPlatformText);
  readonly #document = inject(DOCUMENT);

  /** Which view. */
  readonly kind = input.required<TmRouteErrorKind>();

  /** The permission a `forbidden` view names. */
  readonly securable = input<TmSecurableRef | undefined>(undefined);

  /** Emits when the user asks to try again. */
  readonly retry = output();

  /** The view's strings. */
  protected readonly strings = computed(() => VIEWS[this.kind()]);

  /** The view's placeholders. */
  readonly #params = computed(() => {
    const securable = this.securable();
    return { displayName: this.#text.displayName(), permission: securable ? `${securable[0]} · ${securable[1]}` : '' };
  });

  /** The heading. */
  protected readonly title = computed(() => this.#text.text(...this.strings().title, this.#params())());

  /** The message. */
  protected readonly message = computed(() => this.#text.text(...this.strings().message, this.#params())());

  /** The action's label. */
  protected readonly label = computed(() => {
    const action = this.strings().action;
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
