// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, input, type Signal } from '@angular/core';

import type { TmRowId } from '@tellma/core-ui/contracts';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';

/**
 * What a `tmGridDriver` input moves and opens: the active row of a list
 * (`tm-grid`, `tm-tree-grid`, or any other list that implements it). An
 * interface rather than the grid class, so a grid of any row type binds
 * without variance friction and a list that is not a grid carries no grid
 * code.
 */
export interface TmGridDriverTarget {
  /**
   * The active row's id, or `null` with no active row. It may trail a move by
   * a render (a grid writes its two-way model back then), so the driver's key
   * handling never reads it.
   */
  readonly activeRowId: Signal<TmRowId | null>;
  /** The id of the list's scroll container, which `aria-controls` names. */
  readonly controlsId: Signal<string>;
  /**
   * The active row element's id, which `aria-activedescendant` names; `null`
   * with no active row. Current as soon as `moveActiveRow` returns.
   */
  readonly activeDescendantId: Signal<string | null>;
  /** How many rows (lines, in a tiled list) a PageDown moves. */
  readonly pageStep: Signal<number>;
  /**
   * Moves the active row by a count, or to the first or last row: clamped,
   * never wrapping, keeping the active column.
   */
  moveActiveRow(by: number | 'first' | 'last'): void;
  /**
   * Opens a row (the active one when no id is given, as `moveActiveRow` left
   * it), as a keyboard activation. Returns `false` when there is no such row.
   */
  activateRow(rowId?: TmRowId, options?: { newTab?: boolean }): boolean;
}

/**
 * Lets a text input drive a list while focus stays in the input — a search
 * field above its results: ArrowDown and ArrowUp move the active row (from
 * none, ArrowDown starts at the first row), PageDown and PageUp move it a
 * page, and Enter opens it (with Mod, in a new tab). Every other key stays
 * with the input, and so does Enter while no row is active.
 *
 * ```html
 * <input tmInput type="search" [tmGridDriver]="grid" />
 * <tm-grid #grid … rowActivation="row" (rowActivate)="open($event)">…</tm-grid>
 * ```
 *
 * @tmGroup grid
 * @tmA11yNotes The input names the list through `aria-controls` and the
 *   active row through `aria-activedescendant`, so a screen reader follows
 *   the highlight while focus stays in the input. The active row is always
 *   rendered, so the reference never dangles.
 */
@Directive({
  selector: 'input[tmGridDriver]',
  host: {
    '[attr.aria-controls]': 'tmGridDriver().controlsId()',
    '[attr.aria-activedescendant]': 'tmGridDriver().activeDescendantId()',
    '(keydown)': 'onKeydown($event)',
  },
})
export class TmGridDriver {
  /** The list this input drives. */
  readonly tmGridDriver = input.required<TmGridDriverTarget>();

  /** Routes the driving keys to the list and leaves the rest to the input. */
  protected onKeydown(event: KeyboardEvent): void {
    // IME composition owns its keys; 229 is the composition sentinel on the
    // first composing keydown, where `isComposing` is still false.
    const imeKeyCode = (event as { readonly keyCode: number }).keyCode;
    if (event.defaultPrevented || event.isComposing || imeKeyCode === 229) {
      return;
    }
    // Mod (⌘ on Apple, Ctrl elsewhere) means a new tab on Enter; any other
    // modifier leaves the key to the input (word motion, selection, …).
    const apple = tmIsApplePlatform();
    const mod = apple ? event.metaKey : event.ctrlKey;
    const other = apple ? event.ctrlKey : event.metaKey;
    if (event.altKey || event.shiftKey || other || (mod && event.key !== 'Enter')) {
      return;
    }
    const target = this.tmGridDriver();
    switch (event.key) {
      case 'ArrowDown':
        target.moveActiveRow(1);
        break;
      case 'ArrowUp':
        target.moveActiveRow(-1);
        break;
      case 'PageDown':
        target.moveActiveRow(target.pageStep());
        break;
      case 'PageUp':
        target.moveActiveRow(-target.pageStep());
        break;
      case 'Enter':
        // With no active row, Enter stays the input's own (a search submit).
        // A held Enter opens the row once: its repeats are consumed. Both
        // checks read the list's own active row, never `activeRowId`: that
        // two-way model is written back on the next render, and keys queued
        // behind a busy page (an arrow, then Enter) arrive before it.
        if (
          event.repeat
            ? target.activeDescendantId() === null
            : !target.activateRow(undefined, { newTab: mod })
        ) {
          return;
        }
        break;
      default:
        return;
    }
    event.preventDefault();
  }
}
