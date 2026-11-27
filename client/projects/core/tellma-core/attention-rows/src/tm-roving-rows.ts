// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterEveryRender, Directive, ElementRef, inject, input, output } from '@angular/core';
import { Directionality } from '@angular/cdk/bidi';
import { tmKeysMatch } from '@tellma/core-ui/keys';

// The attention lists' keyboard model (spec 0028 §6.3): each list is one tab
// stop, a `ul` whose rows are `li[data-tm-row]` and whose row controls are
// the row's `[data-tm-row-control]` elements in reading order, the first
// being the row's own link. One control of the list is tabbable at a time:
// the one focus last rested on, else the first row's link. Up and Down move
// between rows, Home and End jump to the ends, and Left and Right move among
// a row's controls in the reading direction, so they swap in Arabic. Delete
// on a row asks the owner to remove it. When the control that held focus
// leaves a row that stays, such as a Stop that hides once asked, focus moves
// to the row's link; when the row itself leaves the list, to the row now in
// its place, else the previous one, else to whatever the owner names.

/** A row control: focusable, tabbable only while it is the list's stop. */
const CONTROL = '[data-tm-row-control]';

/**
 * Makes a list of attention rows one tab stop with a roving tabindex.
 * Internal: the attention surfaces share it.
 *
 * @example
 * ```html
 * <ul tmRovingRows (rowDelete)="clear($event)" [fallback]="heading">
 *   <li data-tm-row [attr.data-tm-id]="row.id"><a data-tm-row-control href="…">…</a><button data-tm-row-control>Clear</button></li>
 * </ul>
 * ```
 */
@Directive({
  selector: '[tmRovingRows]',
  host: {
    '(keydown)': 'keydown($event)',
    '(focusin)': 'focused($event)',
  },
})
export class TmRovingRows {
  readonly #list = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  readonly #direction = inject(Directionality);

  /** The control focus last rested on, which is the list's tab stop while it is in the list. */
  #stop: HTMLElement | null = null;

  /** The control and the row that held focus, and the row's place, to recover focus when either leaves. */
  #focus: { readonly control: HTMLElement; readonly row: HTMLElement; readonly index: number } | null = null;

  /** Where focus goes when the last row leaves while focused, such as the panel's heading. */
  readonly fallback = input<HTMLElement | null>(null);

  /** A row's Delete key: the row's element, whose owner removes it. */
  readonly rowDelete = output<HTMLElement>();

  /** Creates the list, which re-applies its tab stop after every render. */
  constructor() {
    afterEveryRender(() => this.#sync());
  }

  /**
   * Moves focus by a list key.
   *
   * @param event - The key.
   */
  protected keydown(event: KeyboardEvent): void {
    const control = event.target instanceof HTMLElement ? event.target.closest<HTMLElement>(CONTROL) : null;
    const row = control?.closest<HTMLElement>('[data-tm-row]');
    if (!control || !row || !this.#list.contains(row)) {
      return;
    }
    const rows = this.#rows();
    const index = rows.indexOf(row);
    const controls = [...row.querySelectorAll<HTMLElement>(CONTROL)];
    const at = controls.indexOf(control);
    // In a right-to-left page, Left moves on to the next control.
    const rtl = this.#direction.value === 'rtl';
    let target: HTMLElement | undefined;
    if (tmKeysMatch(event, 'ArrowDown')) {
      target = this.#primary(rows[index + 1]);
    } else if (tmKeysMatch(event, 'ArrowUp')) {
      target = this.#primary(rows[index - 1]);
    } else if (tmKeysMatch(event, 'Home')) {
      target = this.#primary(rows[0]);
    } else if (tmKeysMatch(event, 'End')) {
      target = this.#primary(rows.at(-1));
    } else if (tmKeysMatch(event, 'ArrowRight') || tmKeysMatch(event, 'ArrowLeft')) {
      const forward = tmKeysMatch(event, 'ArrowRight') !== rtl;
      target = controls[at + (forward ? 1 : -1)];
    } else if (tmKeysMatch(event, 'Delete')) {
      event.preventDefault();
      this.rowDelete.emit(row);
      return;
    } else {
      return;
    }
    // A list key never scrolls the panel behind the rows; at an end it does nothing.
    event.preventDefault();
    target?.focus();
  }

  /**
   * Makes the control focus lands on the list's tab stop.
   *
   * @param event - The focus event.
   */
  protected focused(event: FocusEvent): void {
    const control = event.target instanceof HTMLElement ? event.target.closest<HTMLElement>(CONTROL) : null;
    const row = control?.closest<HTMLElement>('[data-tm-row]');
    if (control && row) {
      this.#stop = control;
      this.#focus = { control, row, index: this.#rows().indexOf(row) };
      this.#sync();
    }
  }

  // --- The tab stop ---

  /** The rows, in order. */
  #rows(): HTMLElement[] {
    return [...this.#list.querySelectorAll<HTMLElement>(':scope > [data-tm-row]')];
  }

  /** A row's own link, its first control. */
  #primary(row: HTMLElement | undefined): HTMLElement | undefined {
    return row?.querySelector<HTMLElement>(CONTROL) ?? undefined;
  }

  /** Re-applies the tab stop, and moves focus on when the focused row left the list. */
  #sync(): void {
    const rows = this.#rows();
    // --- A focused control that left: its row's link while the row stays; else the row in its place, the previous one, the fallback ---
    const focus = this.#focus;
    if (focus !== null && !focus.control.isConnected) {
      this.#focus = null;
      const active = this.#list.ownerDocument.activeElement;
      if (active === null || active === this.#list.ownerDocument.body || !active.isConnected) {
        const next = focus.row.isConnected ? this.#primary(focus.row) : this.#primary(rows[Math.min(focus.index, rows.length - 1)]);
        (next ?? this.fallback())?.focus();
        return;
      }
    }
    // --- One tabbable control: the last focused one while it is in the list, else the first row's link ---
    const stop = this.#stop !== null && this.#list.contains(this.#stop) ? this.#stop : (this.#primary(rows[0]) ?? null);
    this.#stop = stop;
    for (const control of this.#list.querySelectorAll<HTMLElement>(CONTROL)) {
      const tabIndex = control === stop ? 0 : -1;
      if (control.tabIndex !== tabIndex) {
        control.tabIndex = tabIndex;
      }
    }
  }
}
