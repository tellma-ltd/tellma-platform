// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';

import { TmBusyOverlay } from '@tellma/core-ui/busy-overlay';
import { TmButton } from '@tellma/core-ui/button';

/** Work that settles after `ms` milliseconds. */
function work(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/**
 * TmBusyOverlay: a save whose button shows it busy in the click's frame
 * (the scrim follows at 200 ms), a fast save that never shows the scrim, a
 * sign-out with no visible trigger (`immediate`), nested runs whose label
 * changes as the work goes on, and a ten-second import that keeps the
 * scrim up long enough to inspect it. A counter button and a text field
 * show that the pointer and the keyboard are held while the page is busy.
 */
@Component({
  imports: [TmButton],
  template: `
    <h2>Busy overlay</h2>

    <!-- The triggers. -->
    <section class="row">
      <button
        tmButton
        variant="primary"
        type="button"
        data-testid="save"
        [pending]="saving()"
        (click)="save(1000)"
      >
        Save (1 s)
      </button>
      <button tmButton type="button" data-testid="save-fast" (click)="save(100)">
        Save (0.1 s)
      </button>
      <button tmButton type="button" data-testid="sign-out" (click)="signOut()">
        Sign out (immediate)
      </button>
      <button tmButton type="button" data-testid="nested" (click)="nested()">Nested runs</button>
      <button tmButton type="button" data-testid="import" (click)="import()">Import (10 s)</button>
    </section>

    <!-- What must not react while the page is busy. -->
    <section class="row">
      <button tmButton type="button" data-testid="counter" (click)="clicks.set(clicks() + 1)">
        Clicked {{ clicks() }} times
      </button>
      <input aria-label="Notes" data-testid="notes" />
    </section>
    <p class="log" data-testid="busy-log">{{ events().join(', ') }}</p>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 12px;
      margin-block-end: 16px;
    }
    .log {
      min-block-size: 1.5em;
    }
  `,
})
export class BusyOverlayStory {
  private readonly busy = inject(TmBusyOverlay);

  /** The save button shows it busy for as long as its save runs. */
  protected readonly saving = signal(false);

  /** How many times the counter button was clicked. */
  protected readonly clicks = signal(0);

  /** What finished, in order. */
  protected readonly events = signal<readonly string[]>([]);

  /** A save the user waits for; its trigger shows it busy at once. */
  protected async save(ms: number): Promise<void> {
    this.saving.set(true);
    try {
      await this.busy.run(work(ms), { label: 'Saving…' });
      this.log(`saved after ${ms} ms`);
    } finally {
      this.saving.set(false);
    }
  }

  /** A sign-out: nothing on the page shows it busy, so the scrim shows at once. */
  protected async signOut(): Promise<void> {
    await this.busy.run(work(1000), { immediate: true, label: 'Signing out…' });
    this.log('signed out');
  }

  /** An outer run whose label moves on, with a nested run inside it. */
  protected async nested(): Promise<void> {
    const label = signal('Finishing uploads…');
    const outer = this.busy.run(
      work(600).then(async () => {
        label.set('Saving…');
        await this.busy.run(work(600), { label: 'Posting…' });
      }),
      { label },
    );
    await outer;
    this.log('nested done');
  }

  /** A long import: the scrim stays up long enough to inspect it. */
  protected async import(): Promise<void> {
    await this.busy.run(work(10_000), { label: 'Importing…' });
    this.log('imported');
  }

  /** Records an event for the log line. */
  private log(event: string): void {
    this.events.set([...this.events(), event]);
  }
}
