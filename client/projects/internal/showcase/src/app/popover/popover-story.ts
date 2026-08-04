// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmPopover, TmPopoverContent, TmPopoverTrigger } from '@tellma/core-ui/popover';

/**
 * tm-popover demo host — trigger toggle, lazy content with a small form,
 * logical placement (with a bottom-edge trigger forcing the flip),
 * programmatic anchoring, and the focus in/out battery hooks.
 */
@Component({
  imports: [TmButton, TmPopover, TmPopoverContent, TmPopoverTrigger],
  template: `
    <h2>Popover</h2>

    <section>
      <h3>Trigger + content</h3>
      <button
        tmButton
        variant="secondary"
        data-testid="popover-trigger"
        [tmPopoverTriggerFor]="filters"
      >
        Filters
      </button>
      <tm-popover #filters aria-label="Filters">
        <ng-template tmPopoverContent>
          <p data-testid="popover-content">Restrict the list before exporting.</p>
          <label>
            Cost center:
            <input data-testid="popover-input" />
          </label>
          <button
            tmButton
            variant="ghost"
            data-testid="popover-done"
            (click)="filters.close()"
          >
            Done
          </button>
        </ng-template>
      </tm-popover>
      <input data-testid="after-trigger" placeholder="Next field" />
    </section>

    <section>
      <h3>Programmatic anchor</h3>
      <button
        tmButton
        variant="secondary"
        data-testid="open-at-rect"
        (click)="note.open(rectAnchor)"
      >
        Open at a rectangle
      </button>
      <tm-popover #note aria-label="Note">
        <ng-template tmPopoverContent>
          <p data-testid="rect-content">Anchored to a synthetic rectangle.</p>
        </ng-template>
      </tm-popover>
    </section>

    <section class="pin-bottom">
      <h3>Flip at the viewport edge</h3>
      <button
        tmButton
        variant="secondary"
        data-testid="edge-trigger"
        [tmPopoverTriggerFor]="edge"
      >
        Near the bottom
      </button>
      <tm-popover #edge aria-label="Edge">
        <ng-template tmPopoverContent>
          <p data-testid="edge-content">Prefers below, flips above when there is no room.</p>
        </ng-template>
      </tm-popover>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    /* Pins the flip trigger against the bottom of the viewport. */
    .pin-bottom {
      position: fixed;
      inset-block-end: 8px;
      inset-inline-start: 16px;
      margin-block-end: 0;
    }
  `,
})
export class PopoverStory {
  readonly rectAnchor = new DOMRect(240, 160, 1, 1);
}
