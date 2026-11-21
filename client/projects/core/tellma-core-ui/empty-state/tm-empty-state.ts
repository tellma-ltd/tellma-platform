// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, input, signal } from '@angular/core';

import { TmIcon } from '@tellma/core-ui/icon';

/**
 * Every "nothing to show" and inline-failure state, with one anatomy,
 * centred: an icon, the heading, the detail (the projected content, at
 * most 42 characters wide) and the actions (content marked
 * `tmEmptyStateActions`).
 *
 * Size `page` centres it in the height that remains (an empty list's
 * page); `panel` is the compact form for a card or a side panel. Tone
 * `error` tints the icon for a load that failed; a `tm-reference` may sit
 * in the detail.
 *
 * @tmGroup indicator
 * @tmA11yNotes The heading and detail are read as text. Set `live` to
 *   `polite` when the state replaces content (a search with no results):
 *   it renders `role="status"`, populated a moment after insertion so it
 *   is announced reliably. The icon is decorative.
 */
@Component({
  selector: 'tm-empty-state',
  imports: [TmIcon],
  // A live state's region enters the DOM empty and fills a microtask
  // later: a change inside an existing region is the dependable
  // announcement. The detail and actions wrappers collapse when empty.
  template: `
    <div class="tm-empty-state__body" [attr.role]="live() === 'polite' ? 'status' : null">
      @if (revealed() || live() === 'off') {
        @if (icon(); as name) {
          <tm-icon class="tm-empty-state__icon" [name]="name" />
        }
        <p class="tm-empty-state__heading">{{ heading() }}</p>
        <div class="tm-empty-state__detail"><ng-content /></div>
        <div class="tm-empty-state__actions"><ng-content select="[tmEmptyStateActions]" /></div>
      }
    </div>
  `,
  styleUrl: './tm-empty-state.css',
  host: {
    class: 'tm-empty-state',
    '[attr.data-size]': 'size()',
    '[attr.data-tone]': 'tone()',
  },
})
export class TmEmptyState {
  /** The one-line heading ("No invoices yet"). */
  readonly heading = input.required<string>();

  /** A registered icon name, such as `inbox`, `search-x` or `circle-x`. */
  readonly icon = input<string | undefined>(undefined);

  /** `error` tints the icon, for a load that failed. Default `neutral`. */
  readonly tone = input<'neutral' | 'error'>('neutral');

  /**
   * `page` centres the state in the remaining height; `panel` is the
   * compact form. Default `page`.
   */
  readonly size = input<'page' | 'panel'>('page');

  /**
   * `polite` announces the state when it replaces content; the default
   * `off` keeps a state that is there from the start silent.
   */
  readonly live = input<'off' | 'polite'>('off');

  /** Whether a live state's content has been filled in after insertion. */
  protected readonly revealed = signal(false);

  constructor() {
    queueMicrotask(() => this.revealed.set(true));
  }
}
