// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterRenderEffect,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  untracked,
  viewChild,
} from '@angular/core';
import { ChevronDown, ChevronUp, X } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmIcon } from '@tellma/core-ui/icon';
import { tmKeysMatch } from '@tellma/core-ui/kbd';

import type { ɵTmGridViewCore } from './grid-core';
import { TM_GRID_FIND_KEYS } from './grid-keymap';

/**
 * The find bar (`searchable` grids): a floating strip at the grid's top
 * inline-end corner — a labelled text field, the match counter, previous/
 * next buttons, and a close button. Rendered by the shared view only while
 * the core's `findOpen` is on; all find state and the chunked scan live in
 * the core — this component binds signals and routes input events back.
 *
 * Keyboard contract (the input's keydown): Enter / Shift+Enter cycle the
 * matches (activating each match's cell while focus STAYS here), Esc
 * clears and closes returning focus to the grid at the current match, and
 * Mod+F re-selects the query so the browser's find stays shadowed.
 */
@Component({
  selector: 'tm-grid-find-bar',
  imports: [TmIcon],
  template: `
    <input
      #findInput
      type="text"
      class="tm-grid__find-input"
      data-tm-find-input
      [attr.aria-label]="label()"
      [value]="core().findQuery()"
      (input)="onInput($event)"
      (keydown)="onKeydown($event)"
    />
    <span class="tm-grid__find-counter" data-tm-find-counter>{{ core().findCounterText() }}</span>
    <button
      type="button"
      class="tm-grid__find-nav"
      data-tm-find-prev
      [attr.aria-label]="prevLabel()"
      [disabled]="core().findMatchCount() === 0"
      (click)="core().findStep(-1)"
    >
      <tm-icon [svg]="previousGlyph" />
    </button>
    <button
      type="button"
      class="tm-grid__find-nav"
      data-tm-find-next
      [attr.aria-label]="nextLabel()"
      [disabled]="core().findMatchCount() === 0"
      (click)="core().findStep(1)"
    >
      <tm-icon [svg]="nextGlyph" />
    </button>
    <button
      type="button"
      class="tm-grid__find-close"
      data-tm-find-close
      [attr.aria-label]="closeLabel()"
      (click)="core().closeFind()"
    >
      <tm-icon [svg]="closeGlyph" />
    </button>
  `,
  styleUrl: './find-bar.css',
  host: { class: 'tm-grid-find-bar' },
})
export class ɵTmGridFindBar {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly findInput = viewChild.required<ElementRef<HTMLInputElement>>('findInput');

  /** The composition root the bar reads its find state from. */
  readonly core = input.required<ɵTmGridViewCore>();

  /** The field's accessible name. */
  protected readonly label = this.translate('grid.find.label');
  /** The previous-match button's accessible name. */
  protected readonly prevLabel = this.translate('grid.find.previous');
  /** The next-match button's accessible name. */
  protected readonly nextLabel = this.translate('grid.find.next');
  /** The close button's accessible name. */
  protected readonly closeLabel = this.translate('grid.find.close');

  // The buttons' glyphs: vertical chevrons and a cross, none directional.
  /** The previous-match glyph. */
  protected readonly previousGlyph = ChevronUp;
  /** The next-match glyph. */
  protected readonly nextGlyph = ChevronDown;
  /** The close glyph. */
  protected readonly closeGlyph = X;

  constructor() {
    // Registers the input with the core (Mod+F focuses it, also while the
    // bar is already open); the registration clears with the bar.
    afterRenderEffect(() => {
      const core = this.core();
      const element = this.findInput().nativeElement;
      untracked(() => core.attachFindInput(element));
    });
    inject(DestroyRef).onDestroy(() => untracked(this.core).attachFindInput(null));
  }

  /** Routes typing into the core's debounced scan. */
  protected onInput(event: Event): void {
    this.core().setFindQuery((event.target as HTMLInputElement).value);
  }

  /** The find-bar keyboard contract (see the class TSDoc). */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      event.stopPropagation();
      this.core().findStep(event.shiftKey ? -1 : 1);
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.core().closeFind();
      return;
    }
    if (tmKeysMatch(event, TM_GRID_FIND_KEYS)) {
      event.preventDefault();
      event.stopPropagation();
      this.findInput().nativeElement.select();
    }
  }
}
