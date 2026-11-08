// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, input, signal } from '@angular/core';

/**
 * A latency gate for a story's demo data source. Normally a response waits
 * out the story's configured delay; while the gate HOLDS, it waits for an
 * explicit release instead, however long that takes.
 *
 * It exists for the browser tests. A state that lives only while a request
 * is in flight (a spinner, a pending cell, a panel measured while loading)
 * can only be asserted reliably while it is held open: with a fixed delay
 * the assertion races the clock, and a starved runner loses that race.
 */
export class HeldResponses {
  /** Whether responses are currently held until {@link release}. */
  readonly holding = signal(false);
  /** How many responses are waiting on the gate right now. */
  readonly count = signal(0);

  /** The resume callbacks of the responses waiting on the gate. */
  private readonly waiting = new Set<() => void>();

  /**
   * Waits `delayMs`, or, while the gate holds, until {@link release}.
   * Rejects when `signal` aborts first, as a real request would.
   */
  wait(delayMs: number, signal?: AbortSignal): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      if (!this.holding()) {
        // Unheld: the plain configured latency.
        const timer = setTimeout(resolve, delayMs);
        signal?.addEventListener('abort', () => {
          clearTimeout(timer);
          reject(new Error('aborted'));
        });
        return;
      }
      // Held: park the resume callback until release (or abort).
      const resume = (): void => {
        this.waiting.delete(resume);
        this.count.set(this.waiting.size);
        resolve();
      };
      this.waiting.add(resume);
      this.count.set(this.waiting.size);
      signal?.addEventListener('abort', () => {
        if (this.waiting.delete(resume)) {
          this.count.set(this.waiting.size);
          reject(new Error('aborted'));
        }
      });
    });
  }

  /** Lets every held response through, in the order they arrived. */
  release(): void {
    for (const resume of [...this.waiting]) {
      resume();
    }
  }

  /** Turns holding on or off; turning it off releases whatever is held. */
  setHolding(holding: boolean): void {
    this.holding.set(holding);
    if (!holding) {
      this.release();
    }
  }
}

/**
 * The toolbar controls for one {@link HeldResponses} gate: a "hold"
 * checkbox, and a release button that shows how many responses are held.
 * Test ids derive from `name`: `hold-<name>`, `release-<name>`, and
 * `held-<name>` (the count).
 *
 * Besides a click, the release button answers a custom `release` DOM event,
 * which is what the browser tests dispatch. A click, even a synthetic one,
 * is an outside press to whatever overlay is open (an open dropdown closes
 * and drops the very response being released); the custom event is
 * nothing to the rest of the page.
 */
@Component({
  selector: 'app-held-responses',
  template: `
    <label>
      <input
        type="checkbox"
        [attr.data-testid]="'hold-' + name()"
        [checked]="gate().holding()"
        (change)="onHoldChange($event)"
      />
      Hold {{ name() }} responses
    </label>
    <button
      type="button"
      [attr.data-testid]="'release-' + name()"
      (click)="gate().release()"
      (release)="gate().release()"
    >
      Release held {{ name() }} responses
      (<span [attr.data-testid]="'held-' + name()">{{ gate().count() }}</span>)
    </button>
  `,
  styles: `
    :host {
      display: contents;
    }
  `,
})
export class HeldResponsesControls {
  /** The gate these controls drive. */
  readonly gate = input.required<HeldResponses>();
  /** The data source's name, used in the labels and the test ids. */
  readonly name = input.required<string>();

  /** Mirrors the checkbox onto the gate. */
  protected onHoldChange(event: Event): void {
    this.gate().setHolding((event.target as HTMLInputElement).checked);
  }
}
