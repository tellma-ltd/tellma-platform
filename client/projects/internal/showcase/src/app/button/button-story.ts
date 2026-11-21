// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmTooltip } from '@tellma/core-ui/tooltip';

/**
 * tmButton demo host — variants × sizes, the pending state (with a click
 * counter proving activation suppression), icon usage (inline SVG and
 * `tm-icon` children at every size), link buttons, buttons disabled with a
 * reason, and the in-form type-default behavior. Drives the Playwright
 * battery.
 */
@Component({
  imports: [TmButton, TmIcon, TmTooltip],
  template: `
    <h2>Button</h2>

    <section>
      <h3>Variants × sizes</h3>
      <div class="row" data-testid="variants">
        <button tmButton variant="primary">Primary</button>
        <button tmButton>Secondary</button>
        <button tmButton variant="ghost">Ghost</button>
        <button tmButton variant="danger">Danger</button>
      </div>
      <!-- Every step is stated: the workspace default is 'sm', so an
           unsized button would render identically to the small one and the
           ladder would look like it had two rungs. -->
      <div class="row">
        <button tmButton size="sm">Small</button>
        <button tmButton size="md">Medium</button>
        <button tmButton size="lg">Large</button>
        <button tmButton disabled>Disabled</button>
      </div>
    </section>

    <section>
      <h3>Icons</h3>
      <div class="row">
        <button tmButton variant="primary">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75"
               stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path d="M5 12h14" />
            <path d="M12 5v14" />
          </svg>
          New record
        </button>
        <button tmButton aria-label="Search" data-testid="icon-only">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75"
               stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <circle cx="11" cy="11" r="8" />
            <path d="m21 21-4.3-4.3" />
          </svg>
        </button>
      </div>
    </section>

    <section>
      <h3>tm-icon children</h3>
      <!-- One row per size, so the geometry test can compare each icon box
           with the size's icon token; the last button toggles pending. -->
      <div class="row" data-testid="tm-icon-row">
        <button tmButton size="sm" data-testid="tm-icon-sm"><tm-icon name="plus" /> Small</button>
        <button tmButton size="md" data-testid="tm-icon-md"><tm-icon name="plus" /> Medium</button>
        <button tmButton size="lg" data-testid="tm-icon-lg"><tm-icon name="plus" /> Large</button>
        <button
          tmButton
          size="md"
          aria-label="Search"
          data-testid="tm-icon-pending"
          [pending]="iconPending()"
        >
          <tm-icon name="search" />
        </button>
        <button tmButton data-testid="tm-icon-pending-toggle" (click)="iconPending.set(!iconPending())">
          Toggle icon pending
        </button>
      </div>
    </section>

    <section>
      <h3>Links</h3>
      <div class="row">
        <a tmButton variant="primary" href="#new-record" data-testid="link-primary">New record</a>
        <a tmButton href="#records" size="sm" data-testid="link-secondary">All records</a>
      </div>
    </section>

    <section>
      <h3>Disabled with a reason</h3>
      <!-- A reason keeps the button focusable and explains it on hover,
           keyboard focus and tap; native disabled stays for transient
           states. -->
      <div class="row">
        <button
          tmButton
          variant="primary"
          data-testid="reason-btn"
          [disabledReason]="reason()"
          (click)="reasonClicks.set(reasonClicks() + 1)"
        >
          Edit
        </button>
        <button
          tmButton
          variant="ghost"
          aria-label="Delete"
          tmTooltip="Delete"
          data-testid="reason-icon-btn"
          [disabledReason]="reason()"
        >
          <tm-icon name="x" />
        </button>
        <button tmButton data-testid="reason-toggle" (click)="toggleReason()">Toggle reason</button>
        <output data-testid="reason-click-count">{{ reasonClicks() }}</output>
      </div>
    </section>

    <section>
      <h3>Pending</h3>
      <div class="row">
        <button
          tmButton
          variant="primary"
          data-testid="pending-btn"
          [pending]="pending()"
          (click)="clicks.set(clicks() + 1)"
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75"
               stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path d="M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z" />
            <path d="M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7" />
            <path d="M7 3v4a1 1 0 0 0 1 1h7" />
          </svg>
          Save changes
        </button>
        <button tmButton data-testid="pending-toggle" (click)="pending.set(!pending())">
          Toggle pending
        </button>
        <output data-testid="click-count">{{ clicks() }}</output>
      </div>
    </section>

    <section>
      <h3>In a form</h3>
      <form (submit)="onSubmit($event)">
        <div class="row">
          <button tmButton data-testid="untyped-in-form">Untyped (never submits)</button>
          <button tmButton type="submit" variant="primary" data-testid="submit-in-form">
            Submit
          </button>
          <output data-testid="submit-count">{{ submits() }}</output>
        </div>
      </form>
    </section>
  `,
  styles: `
    section {
      margin-block-end: 24px;
    }
    .row {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-block-end: 12px;
      flex-wrap: wrap;
    }
  `,
})
export class ButtonStory {
  readonly pending = signal(false);
  readonly clicks = signal(0);
  readonly submits = signal(0);
  readonly iconPending = signal(false);
  readonly reason = signal<string | null>('Needs the “Customers: Create and edit” permission.');
  readonly reasonClicks = signal(0);

  toggleReason(): void {
    this.reason.set(
      this.reason() === null ? 'Needs the “Customers: Create and edit” permission.' : null,
    );
  }

  onSubmit(event: Event): void {
    event.preventDefault();
    this.submits.set(this.submits() + 1);
  }
}
