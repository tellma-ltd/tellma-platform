// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, input } from '@angular/core';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmSpinner } from '@tellma/core-ui/spinner';

import type { ɵTmGridViewCore } from './grid-core';

/**
 * The editable grid's fixed-height status bar: the error tally chip
 * (clicking it jumps to the next errored cell) flanked by previous/next
 * buttons, and the pending-resolution spinner while async paste
 * resolutions are in flight. Rendered by the shared view in editable mode
 * only; the fixed height means errors appearing or clearing never shift
 * the grid's layout.
 */
@Component({
  selector: 'tm-grid-status-bar',
  imports: [TmSpinner],
  template: `
    <div class="tm-grid__status-live" aria-live="polite">
      @if (core().errorCount() > 0) {
        <button
          type="button"
          class="tm-grid__status-chip"
          data-tm-status-chip
          (click)="core().gotoError(1)"
        >
          <span class="tm-grid__status-warn" aria-hidden="true">⚠</span>
          {{ tallyText() }}
        </button>
        <button
          type="button"
          class="tm-grid__status-nav"
          data-tm-status-prev
          [attr.aria-label]="prevLabel()"
          (click)="core().gotoError(-1)"
        >
          <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
            <polyline points="10,4 6,8 10,12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
        </button>
        <button
          type="button"
          class="tm-grid__status-nav"
          data-tm-status-next
          [attr.aria-label]="nextLabel()"
          (click)="core().gotoError(1)"
        >
          <svg viewBox="0 0 16 16" fill="none" aria-hidden="true">
            <polyline points="6,4 10,8 6,12" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
          </svg>
        </button>
      }
      @if (core().pendingCount() > 0) {
        <tm-spinner class="tm-grid__status-spinner" />
        <span class="tm-grid__status-pending">{{ pendingText() }}</span>
      }
    </div>
  `,
  styleUrl: './status-bar.css',
  host: { class: 'tm-grid-status-bar' },
})
export class ɵTmGridStatusBar {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The composition root the bar reads its tallies from. */
  readonly core = input.required<ɵTmGridViewCore>();

  /** The localized tally text ("3 errors"). */
  protected readonly tallyText = computed(() =>
    this.translate('grid.cellErrors.tally', { count: this.core().errorCount() })(),
  );
  /** The localized pending text ("2 cells resolving"). */
  protected readonly pendingText = computed(() =>
    this.translate('grid.cellErrors.pending', { count: this.core().pendingCount() })(),
  );
  /** The previous-error button's accessible name. */
  protected readonly prevLabel = this.translate('grid.cellErrors.previous');
  /** The next-error button's accessible name. */
  protected readonly nextLabel = this.translate('grid.cellErrors.next');
}
