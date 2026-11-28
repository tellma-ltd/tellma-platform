// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, signal } from '@angular/core';

import { TmButton } from '@tellma/core-ui/button';
import { TmErrorDialog, type TmErrorDialogOptions } from '@tellma/core-ui/error-dialog';

/** How long the story's Reload pretends to work before it fails. */
const RELOAD_MS = 1500;

/**
 * TmErrorDialog: a server failure with a reference, a Details disclosure
 * (facts, a copy action, and an action that replaces the dialog in place)
 * and a Reload whose run stays pending, then fails; and a validation
 * failure listing thirteen lines (ten, then "and 3 more"). The last
 * outcome shows under the buttons.
 */
@Component({
  imports: [TmButton],
  template: `
    <h2>Error dialog</h2>

    <section class="row">
      <button tmButton type="button" data-testid="open-server" (click)="server()">
        Server failure
      </button>
      <button tmButton type="button" data-testid="open-validation" (click)="validation()">
        Validation failure
      </button>
    </section>
    <p data-testid="error-outcome">Outcome: {{ outcomes().join(', ') || 'none' }}</p>
  `,
  styles: `
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
    }
  `,
})
export class ErrorDialogStory {
  private readonly dialog = inject(TmErrorDialog);

  /** Every outcome so far, in order. */
  protected readonly outcomes = signal<readonly string[]>([]);

  /** A 5xx: Reload stays pending, then fails and returns to idle. */
  protected server(): void {
    this.show({
      title: 'Something went wrong',
      message: 'The server could not complete the request. Reloading usually helps.',
      reference: 'c0ffee00-1234-4bcd-9a7e-5f00d2a1b3c4',
      primary: {
        label: 'Reload',
        pendingLabel: 'Reloading the application',
        run: () => new Promise<boolean>((resolve) => setTimeout(() => resolve(false), RELOAD_MS)),
      },
      details: [
        { label: 'Status', value: '500' },
        { label: 'UI version', value: '2.14.0+8f2c41a' },
      ],
      actions: [
        { label: 'Copy diagnostics', run: () => this.record('copied') },
        { label: 'Show another error', run: () => this.validation() },
      ],
    });
  }

  /** A validation failure listing more lines than the dialog shows. */
  protected validation(): void {
    this.show({
      title: 'The document was not saved',
      message: 'These lines have errors:',
      items: Array.from({ length: 13 }, (_, index) => `Line ${index + 1}: the account is required`),
      secondary: { label: 'Go to the first line' },
    });
  }

  /** Shows a dialog and records how it ended. */
  private show(options: TmErrorDialogOptions): void {
    void this.dialog.show(options).then((outcome) => this.record(outcome));
  }

  /** Records an outcome for the log line. */
  private record(outcome: string): void {
    this.outcomes.set([...this.outcomes(), outcome]);
  }
}
