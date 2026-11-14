// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, input, type Signal } from '@angular/core';
import { ChevronLeft, ChevronRight, CircleAlert } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { TmSpinner } from '@tellma/core-ui/spinner';

/**
 * What the status bar reads from the grid it sits under: the tallies, the
 * transient notice and the error navigation. Declared here by shape, so this
 * entry point never imports the grid it serves.
 */
export interface ɵTmGridStatusSource {
  /** The number of cells currently in error. */
  readonly errorCount: Signal<number>;
  /** The number of cells whose pasted labels are still resolving. */
  readonly pendingCount: Signal<number>;
  /** The transient clipboard-failure notice, or `null`. */
  readonly transientNotice: Signal<string | null>;
  /** Jumps to the next (+1) or previous (−1) errored cell. */
  gotoError(direction: 1 | -1): void;
}

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
 * the shared view in editable mode only, inside a slot of the bar's fixed
 * height that the view draws before this code arrives, so neither the
 * load nor errors appearing or clearing ever shift the grid's layout.
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
  readonly core = input.required<ɵTmGridStatusSource>();

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
