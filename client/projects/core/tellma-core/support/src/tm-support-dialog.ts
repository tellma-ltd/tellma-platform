// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { type TmApiError, TmLocalization, type ɵTmSupportSnapshot } from '@tellma/core';
import type { TmPageContextSnapshot } from '@tellma/core/page-snapshot';
import { TmButton } from '@tellma/core-ui/button';
import { TmModalFooter, TmModalRef } from '@tellma/core-ui/modal';

import { TmDiagnosticsPanel } from './tm-diagnostics-panel';

// The support dialog's content (spec 0026 §12.2), which the support host
// renders once this entry point lands. Sending a request needs the server's
// support stack, so for now the dialog offers what works without it: the
// Diagnostics panel over the snapshot taken in the press, whose Download
// builds the file from that snapshot, without page data, as the dialog's
// Download only state does. The compose, sending, sent and failed states
// replace it with the server stack.

/**
 * The support dialog's content: the Diagnostics panel over the snapshot the
 * press took, and Close.
 */
@Component({
  selector: 'tm-support-dialog',
  imports: [TmButton, TmDiagnosticsPanel, TmModalFooter],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The diagnostics over the snapshot; Report a problem is this dialog, so the panel leaves it out. -->
    <tm-diagnostics [snapshot]="snapshot()" [reportable]="false" />
    <div tmModalFooter>
      <button tmButton type="button" variant="primary" cdkFocusInitial (click)="close()">{{ closeLabel() }}</button>
    </div>
  `,
})
export class TmSupportDialog {
  readonly #ref = inject(TmModalRef);

  /** The snapshot the support facade took in the press. */
  readonly snapshot = input.required<ɵTmSupportSnapshot<TmPageContextSnapshot>>();

  /** The error the dialog opened from, such as the 5xx dialog's. */
  readonly error = input<TmApiError | undefined>(undefined);

  /** Close's label. */
  protected readonly closeLabel = inject(TmLocalization).ɵtext('tm.supportHost.close');

  /** Closes the dialog. */
  protected close(): void {
    this.#ref.close();
  }
}
