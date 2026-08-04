// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';

/**
 * tmButton demo host — variants × sizes, the pending state (with a click
 * counter proving activation suppression), icon usage, and the in-form
 * type-default behavior. Drives the Playwright battery.
 */
@Component({
  imports: [TmButton],
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
      <div class="row">
        <button tmButton size="sm">Small</button>
        <button tmButton>Medium</button>
        <button tmButton size="lg">Large</button>
        <button tmButton disabled>Disabled</button>
      </div>
    </section>

    <section>
      <h3>Icons</h3>
      <div class="row">
        <button tmButton variant="primary">
          <svg viewBox="0 0 16 16" fill="none" aria-hidden="true" width="16" height="16">
            <path d="M8 3v10M3 8h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
          </svg>
          New record
        </button>
        <button tmButton aria-label="Search" data-testid="icon-only">
          <svg viewBox="0 0 16 16" fill="none" aria-hidden="true" width="16" height="16">
            <circle cx="7" cy="7" r="4.5" stroke="currentColor" />
            <path d="m10.5 10.5 3 3" stroke="currentColor" stroke-linecap="round" />
          </svg>
        </button>
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
          <svg viewBox="0 0 16 16" fill="none" aria-hidden="true" width="16" height="16">
            <path d="M3 8h10" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" />
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

  onSubmit(event: Event): void {
    event.preventDefault();
    this.submits.set(this.submits() + 1);
  }
}
