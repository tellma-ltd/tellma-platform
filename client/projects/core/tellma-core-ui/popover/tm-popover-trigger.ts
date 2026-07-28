// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, ElementRef, inject, input } from '@angular/core';

import { TmPopover } from './tm-popover';

/**
 * Wires a button to a {@link TmPopover}: click toggles, the popover anchors
 * to the button, and the button carries the `aria-expanded` /
 * `aria-haspopup="dialog"` trigger semantics.
 *
 * ```html
 * <button tmButton [tmPopoverTriggerFor]="filters">Filters</button>
 * <tm-popover #filters aria-label="Filters">
 *   <ng-template tmPopoverContent>…</ng-template>
 * </tm-popover>
 * ```
 *
 * @tmGroup overlay
 * @tmA11yNotes Applies `aria-haspopup="dialog"` and live `aria-expanded`;
 *   Escape pressed while the trigger holds focus closes the open popover.
 */
@Directive({
  selector: 'button[tmPopoverTriggerFor]',
  host: {
    'aria-haspopup': 'dialog',
    '[attr.aria-expanded]': 'popover().isOpen()',
    '(click)': 'toggle()',
    '(keydown)': 'onKeydown($event)',
  },
})
export class TmPopoverTrigger {
  /** The popover this button opens. */
  readonly popover = input.required<TmPopover>({ alias: 'tmPopoverTriggerFor' });

  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  protected toggle(): void {
    const popover = this.popover();
    if (popover.isOpen()) {
      popover.close();
    } else {
      popover.open(this.element.nativeElement);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !event.defaultPrevented && this.popover().isOpen()) {
      event.preventDefault();
      this.popover().close();
    }
  }
}
