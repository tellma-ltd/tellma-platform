// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmCard } from '@tellma/core-ui/card';

/**
 * tm-card with a full header (heading, count, a two-part subtitle, an
 * action), as a disclosure that drops its body, as one that keeps it
 * (`preserveContent`) with the error dot toggled from outside, flush with
 * a footer, and with no header at all.
 */
@Component({
  imports: [TmButton, TmCard],
  template: `
    <h2>Card</h2>

    <div class="stack">
      <!-- The full header. -->
      <tm-card heading="Lines" [count]="12" data-testid="card-full">
        <div tmCardSubtitle>
          <span
            >Posted entries for the Jeddah sales and distribution center, its sub-centers and its
            warehouses, this quarter and the last</span
          >
          <span>Updated 10:42</span>
        </div>
        <button tmButton tmCardActions variant="ghost" size="sm" type="button">Add line</button>
        <p class="body">The document's lines.</p>
      </tm-card>

      <!-- A disclosure whose body leaves the DOM when collapsed. -->
      <tm-card
        heading="Notes"
        [collapsible]="true"
        [(expanded)]="notesOpen"
        data-testid="card-collapsible"
      >
        <p class="body">Free-text notes on the document.</p>
      </tm-card>

      <!-- A disclosure that keeps its fields while collapsed, with the error dot. -->
      <div class="row">
        <button
          tmButton
          type="button"
          data-testid="toggle-invalid"
          (click)="invalid.set(!invalid())"
        >
          {{ invalid() ? 'Clear the error' : 'Flag an error' }}
        </button>
      </div>
      <tm-card
        heading="Address"
        [headingLevel]="3"
        [collapsible]="true"
        [preserveContent]="true"
        [invalid]="invalid()"
        data-testid="card-invalid"
      >
        <label class="body">City <input data-testid="card-city" /></label>
      </tm-card>

      <!-- Flush, with a footer. -->
      <tm-card heading="Recent documents" [flush]="true" data-testid="card-flush">
        <ul class="flush-list">
          <li>JV-0042</li>
          <li>JV-0043</li>
        </ul>
        <p tmCardFooter class="body">Showing the last 30 days.</p>
      </tm-card>

      <!-- No header at all. -->
      <tm-card data-testid="card-plain"><p class="body">A plain framed block.</p></tm-card>
    </div>
  `,
  styles: `
    .stack {
      display: flex;
      flex-direction: column;
      gap: 16px;
      max-inline-size: 560px;
    }
    .row {
      display: flex;
      gap: 8px;
    }
    .body {
      margin: 0;
    }
    .flush-list {
      margin: 0;
      padding: 8px 32px;
    }
  `,
})
export class CardStory {
  /** Whether the Notes card is open. */
  protected readonly notesOpen = signal(true);

  /** Whether the Address card holds an error. */
  protected readonly invalid = signal(false);
}
