// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, input, type Signal } from '@angular/core';

import { TmSpinner } from '@tellma/core-ui/spinner';

/** What the busy overlay's host reads from the service. */
export interface TmBusyOverlayState {
  /** A run is in progress: the host blocks the pointer with a progress cursor. */
  readonly active: Signal<boolean>;
  /** The scrim, spinner and label show. */
  readonly scrim: Signal<boolean>;
  /** The label the status region carries while the scrim shows. */
  readonly label: Signal<string>;
  /** The status region has been in the page long enough to announce a change. */
  readonly ready: Signal<boolean>;
}

/**
 * The busy overlay's one host: a `popover="manual"` element on `<body>`
 * covering the viewport, always open. Idle it is transparent to the
 * pointer; active it takes the pointer with a progress cursor; with the
 * scrim it veils the page and centres a card with a spinner and the label.
 * The label's status region always exists and is filled only once the scrim
 * shows, so its text is announced as it changes.
 */
@Component({
  selector: 'tm-busy-overlay',
  imports: [TmSpinner],
  // The card is always rendered (its opacity, not its display, follows the
  // scrim), so the status region inside it stays in the accessibility tree.
  template: `
    <div class="tm-busy-overlay__panel">
      @if (state().scrim()) {
        <tm-spinner class="tm-busy-overlay__spinner" />
      }
      <span class="tm-busy-overlay__label" role="status">{{ text() }}</span>
    </div>
  `,
  styleUrl: './tm-busy-overlay-host.css',
  host: {
    class: 'tm-busy-overlay',
    popover: 'manual',
    '[class.tm-busy-overlay--active]': 'state().active()',
    '[class.tm-busy-overlay--scrim]': 'state().scrim()',
  },
})
export class TmBusyOverlayHost {
  /** The service's state. */
  readonly state = input.required<TmBusyOverlayState>();

  /** The status region's text: the label while the scrim shows, else nothing. */
  protected readonly text = computed(() =>
    this.state().scrim() && this.state().ready() ? this.state().label() : '',
  );
}
