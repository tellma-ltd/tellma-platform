// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmProgressBar, TmProgressRing } from '@tellma/core-ui/progress';

/**
 * tm-progress-bar at fixed values and in every tone, sweeping, and behind
 * a toggle that shows the minimum display time; tm-progress-ring spinning
 * and at a share, decorative around an icon button and named on its own.
 */
@Component({
  imports: [TmButton, TmIcon, TmProgressBar, TmProgressRing],
  template: `
    <h2>Progress</h2>

    <!-- Determinate bars and the tones. -->
    <section class="bars">
      <tm-progress-bar [value]="0" label="Not started" data-testid="bar-0" />
      <tm-progress-bar [value]="40" label="Uploading" data-testid="bar-40" />
      <tm-progress-bar [value]="100" label="Done" data-testid="bar-100" />
      <tm-progress-bar [value]="70" tone="success" label="Imported" />
      <tm-progress-bar [value]="90" tone="warning" label="Storage used" />
      <tm-progress-bar [value]="30" tone="error" label="Failed rows" />
    </section>

    <!-- The sweep, overlaid on a region's top edge as consumers use it. -->
    <section class="region">
      <tm-progress-bar class="edge" label="Loading invoices" data-testid="sweep" />
      <p>A region that is loading.</p>
    </section>

    <!-- The hold: a task that ends at once still shows the bar for 250 ms. -->
    <section class="row">
      <button tmButton type="button" data-testid="toggle-active" (click)="active.set(!active())">
        {{ active() ? 'Finish' : 'Start' }}
      </button>
      <tm-progress-bar
        class="grow"
        [active]="active()"
        label="Saving"
        data-testid="held"
      />
    </section>

    <!-- Rings: spinning and at a share, around a button and named. -->
    <section class="row">
      <span class="ringed">
        <tm-progress-ring class="ring" data-testid="ring-spin" />
        <button tmButton variant="ghost" type="button" aria-label="Jobs, 1 running">
          <tm-icon name="plus" />
        </button>
      </span>
      <tm-progress-ring [value]="60" [diameter]="36" data-testid="ring-60" />
      <tm-progress-ring
        [value]="25"
        [decorative]="false"
        aria-label="Export progress"
        data-testid="ring-named"
      />
    </section>
  `,
  styles: `
    .bars {
      display: grid;
      gap: 12px;
      max-inline-size: 360px;
      margin-block-end: 24px;
    }
    .region {
      position: relative;
      max-inline-size: 360px;
      padding: 16px;
      border: 1px solid var(--border-subtle);
      margin-block-end: 24px;
    }
    .edge {
      position: absolute;
      inset-block-start: 0;
      inset-inline: 0;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 24px;
      margin-block-end: 24px;
    }
    .grow {
      flex: 1;
      max-inline-size: 240px;
    }
    .ringed {
      position: relative;
      display: inline-grid;
      place-items: center;
    }
    .ring {
      position: absolute;
    }
  `,
})
export class ProgressStory {
  /** Whether the held bar's task is running. */
  protected readonly active = signal(false);
}
