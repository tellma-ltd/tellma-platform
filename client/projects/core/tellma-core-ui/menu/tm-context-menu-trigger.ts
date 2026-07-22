// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { booleanAttribute, Directive, DestroyRef, ElementRef, inject, input } from '@angular/core';

import { tmObserveLongPress } from './internal/long-press';
import type { TmMenu } from './tm-menu';

/**
 * Opens a `tm-menu` as this element's context menu: at the pointer on
 * right-click, at the element for keyboard invocations (the Menu key and
 * Shift+F10 both arrive as a keyboard-sourced `contextmenu` event), and at
 * the press point on touch long-press. Focus returns to this element when
 * the menu closes via keyboard.
 *
 * @tmGroup overlay
 * @tmA11yNotes Binds aria-haspopup="menu"; the browser's native context
 *   menu is suppressed while the trigger is enabled.
 */
@Directive({
  selector: '[tmContextMenuTrigger]',
  host: {
    '[attr.aria-haspopup]': '"menu"',
    '(contextmenu)': 'onContextMenu($event)',
  },
})
export class TmContextMenuTrigger {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;

  /** The menu this trigger opens. */
  readonly tmContextMenuTrigger = input.required<TmMenu>();
  /** Disables the trigger (the native context menu comes back). */
  readonly tmContextMenuTriggerDisabled = input(false, { transform: booleanAttribute });

  constructor() {
    // Gate the long-press observer on the disabled state: a disabled trigger
    // starts no press timer and arms no suppression, so the platform's native
    // long-press context menu comes back and the follow-up tap is untouched.
    const stop = tmObserveLongPress(
      this.element,
      (point) => this.tmContextMenuTrigger().open(point, { restoreFocus: this.element }),
      { enabled: () => !this.tmContextMenuTriggerDisabled() },
    );
    inject(DestroyRef).onDestroy(stop);
  }

  /** Right-click opens at the pointer; keyboard invocations open at the element. */
  protected onContextMenu(event: MouseEvent): void {
    if (this.tmContextMenuTriggerDisabled()) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    // Keyboard-sourced contextmenu events (Menu key, Shift+F10) carry no
    // useful pointer position — some engines report (0,0), Firefox reports
    // the element. Anchor those at the element instead of the pointer.
    const keyboardInvoked = event.button !== 2 && event.detail === 0;
    const anchor =
      keyboardInvoked || (event.clientX === 0 && event.clientY === 0)
        ? this.element
        : { x: event.clientX, y: event.clientY };
    this.tmContextMenuTrigger().open(anchor, { restoreFocus: this.element });
  }
}
