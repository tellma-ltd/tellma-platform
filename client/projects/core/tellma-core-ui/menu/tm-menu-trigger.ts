// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, effect, ElementRef, inject, input } from '@angular/core';

import type { TmMenu } from './tm-menu';

/**
 * Makes a button open a `tm-menu` anchored at itself: a menu button.
 *
 * ```html
 * <button tmButton [tmMenuTriggerFor]="menu">Options</button>
 * <tm-menu #menu [items]="items()" aria-label="Options" />
 * ```
 *
 * A click toggles the menu. Enter, Space and ArrowDown open it with focus
 * on the first row; ArrowUp opens it on the last. Focus returns to the
 * button when the menu closes on Escape or after an activation.
 *
 * @tmGroup overlay
 * @tmA11yNotes Applies aria-haspopup="menu", a live aria-expanded, and
 *   aria-controls naming the menu while it is open. A button that is
 *   aria-disabled does not open its menu.
 */
@Directive({
  selector: 'button[tmMenuTriggerFor]',
  host: {
    class: 'tm-menu-trigger',
    'aria-haspopup': 'menu',
    '[attr.aria-expanded]': 'expanded()',
    '[attr.aria-controls]': 'controls()',
    '(click)': 'toggle()',
    '(keydown)': 'onKeydown($event)',
  },
})
export class TmMenuTrigger {
  private readonly element = inject<ElementRef<HTMLButtonElement>>(ElementRef).nativeElement;

  /** The menu this button opens. */
  readonly tmMenuTriggerFor = input.required<TmMenu>();

  constructor() {
    // Announcing this button to the menu keeps a press on it from counting
    // as an outside press: the menu dismisses on pointerdown, ahead of this
    // directive's click handler, so an unregistered button would close the
    // menu and then have `toggle()` re-open it, never collapsing it.
    // Re-runs if the bound menu ever changes.
    effect((onCleanup) => {
      onCleanup(this.tmMenuTriggerFor().ɵregisterTrigger(this.element));
    });
  }

  /** Whether the menu is open from this button. */
  protected expanded(): boolean {
    return this.tmMenuTriggerFor().ɵisOpenFor(this.element);
  }

  /** The id of the open menu's list, which this button controls; `null` while closed. */
  protected controls(): string | null {
    return this.expanded() ? this.tmMenuTriggerFor().ɵlistId : null;
  }

  /**
   * A click closes the menu this button opened, and otherwise opens it
   * here. Enter and Space arrive as clicks, as on any button.
   */
  protected toggle(): void {
    if (this.expanded()) {
      this.tmMenuTriggerFor().close();
    } else {
      this.open('first');
    }
  }

  /** ArrowDown opens on the first row and ArrowUp on the last. */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') {
      return;
    }
    if (event.ctrlKey || event.metaKey || event.altKey || event.shiftKey || this.isDisabled()) {
      return;
    }
    // The arrow would otherwise scroll the page behind the menu.
    event.preventDefault();
    this.open(event.key === 'ArrowDown' ? 'first' : 'last');
  }

  /** Opens the menu at this button, unless the button is disabled. */
  private open(initialFocus: 'first' | 'last'): void {
    if (!this.isDisabled()) {
      this.tmMenuTriggerFor().open(this.element, { restoreFocus: this.element, initialFocus });
    }
  }

  /**
   * Whether the button is disabled. A button disabled with a reason stays
   * focusable and reports aria-disabled instead of the native attribute;
   * it must not open its menu either.
   */
  private isDisabled(): boolean {
    return this.element.disabled || this.element.getAttribute('aria-disabled') === 'true';
  }
}
