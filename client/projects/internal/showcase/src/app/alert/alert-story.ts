// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmAlert } from '@tellma/core-ui/alert';
import { TmButton } from '@tellma/core-ui/button';

/**
 * tm-alert demo host — the four kinds (± heading) and a dynamically
 * inserted assertive alert for the announcement battery.
 */
@Component({
  imports: [TmAlert, TmButton],
  template: `
    <h2>Alert</h2>

    <div class="stack" data-testid="kinds">
      <tm-alert kind="info">Posting runs nightly at 02:00 server time.</tm-alert>
      <tm-alert kind="success">The invoice was posted.</tm-alert>
      <tm-alert kind="warning" heading="Unposted lines">
        Three lines are still drafts and will not appear in reports.
      </tm-alert>
      <tm-alert kind="error" heading="Save failed">
        The document could not be saved — fix the highlighted fields.
      </tm-alert>
    </div>

    <section>
      <h3>Dynamic insertion (role="alert")</h3>
      <button tmButton variant="danger" data-testid="insert-alert" (click)="failed.set(true)">
        Simulate failed save
      </button>
      <div class="stack" data-testid="dynamic-region">
        @if (failed()) {
          <tm-alert kind="error" live="assertive" data-testid="dynamic-alert">
            Saving failed — try again.
          </tm-alert>
        }
      </div>
    </section>
  `,
  styles: `
    .stack {
      display: grid;
      gap: 12px;
      max-inline-size: 560px;
      margin-block: 12px;
    }
  `,
})
export class AlertStory {
  readonly failed = signal(false);
}
