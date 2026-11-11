// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, input } from '@angular/core';
import { ChevronLeft, ChevronRight, CircleAlert } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { TmSpinner } from '@tellma/core-ui/spinner';

import type { ɵTmGridViewCore } from './grid-core';

// The tally's glyphs. The chevrons point along the reading direction
// (previous toward its start, next toward its end), so they mirror in
// right-to-left text; the error mark is symmetric.
const PREVIOUS_GLYPH: TmIconSource = { node: ChevronLeft, mirrorInRtl: true };
const NEXT_GLYPH: TmIconSource = { node: ChevronRight, mirrorInRtl: true };

/**
 * The editable grid's fixed-height status bar: the error tally chip
 * (clicking it jumps to the next errored cell) flanked by previous/next
 * buttons, the pending-resolution spinner while async paste resolutions
 * are in flight, and the transient clipboard-failure notice. Rendered by
 * the shared view in editable mode only; the fixed height means errors
 * appearing or clearing never shift the grid's layout.
 */
@Component({
  selector: 'tm-grid-status-bar',
  imports: [TmIcon, TmSpinner],
  template: `
    <div class="tm-grid__status-live" aria-live="polite">
      @if (core().errorCount() > 0) {
        <button
          type="button"
          class="tm-grid__status-nav"
          data-tm-status-prev
          [attr.aria-label]="prevLabel()"
          (click)="core().gotoError(-1)"
        >
          <tm-icon [svg]="previousGlyph" />
        </button>
        <button
          type="button"
          class="tm-grid__status-chip"
          data-tm-status-chip
          (click)="core().gotoError(1)"
        >
          <tm-icon [svg]="errorGlyph" />
          {{ tallyText() }}
        </button>
        <button
          type="button"
          class="tm-grid__status-nav"
          data-tm-status-next
          [attr.aria-label]="nextLabel()"
          (click)="core().gotoError(1)"
        >
          <tm-icon [svg]="nextGlyph" />
        </button>
      }
      @if (core().pendingCount() > 0) {
        <tm-spinner class="tm-grid__status-spinner" />
        <span class="tm-grid__status-pending">{{ pendingText() }}</span>
      }
    </div>
    <!-- Outside the live block: the failure was already announced through
         the LiveAnnouncer; re-announcing here would double-speak it. -->
    @if (core().transientNotice(); as notice) {
      <span class="tm-grid__status-notice" data-tm-status-notice>{{ notice }}</span>
    }
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

  /** The previous-error glyph, mirrored in right-to-left text. */
  protected readonly previousGlyph = PREVIOUS_GLYPH;
  /** The error tally's mark. */
  protected readonly errorGlyph = CircleAlert;
  /** The next-error glyph, mirrored in right-to-left text. */
  protected readonly nextGlyph = NEXT_GLYPH;
}
