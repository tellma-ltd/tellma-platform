// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmTooltip } from '@tellma/core-ui/tooltip';

/**
 * tmTooltip demo host — hover/focus show paths, two hosts for the
 * single-open invariant, and the disabled-control wrapper pattern. Drives
 * the Playwright battery (including the touch long-press project).
 */
@Component({
  imports: [TmButton, TmTooltip],
  template: `
    <h2>Tooltip</h2>

    <section>
      <h3>On controls</h3>
      <div class="row">
        <button
          tmButton
          variant="secondary"
          data-testid="tooltip-first"
          tmTooltip="Refresh the list"
        >
          Refresh
        </button>
        <button
          tmButton
          variant="secondary"
          data-testid="tooltip-second"
          tmTooltip="Post the selected documents"
        >
          Post
        </button>
      </div>
    </section>

    <section>
      <h3>Disabled control (wrapper pattern)</h3>
      <span tabindex="0" data-testid="tooltip-wrapper" tmTooltip="Select at least one line first">
        <button tmButton disabled>Void</button>
      </span>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    .row {
      display: flex;
      gap: 8px;
    }
  `,
})
export class TooltipStory {}
