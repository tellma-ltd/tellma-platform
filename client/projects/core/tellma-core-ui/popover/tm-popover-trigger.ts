// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, effect, ElementRef, inject, input } from '@angular/core';

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

  constructor() {
    // Announcing this button to the popover (rather than leaning on it
    // being the current anchor) is what keeps a click here from counting
    // as an outside click while the panel sits at a DIFFERENT trigger: the
    // CDK detects outside clicks at document capture, before this
    // directive's own click handler, so an unregistered trigger would
    // close the popover and then have `toggle()` re-open it — never
    // collapsing it. Re-runs if the bound popover ever changes.
    effect((onCleanup) => {
      onCleanup(this.popover().ɵregisterTrigger(this.element.nativeElement));
    });
  }

  /** Click toggles: open anchored to this button, or close if open. */
  protected toggle(): void {
    const popover = this.popover();
    if (popover.isOpen()) {
      popover.close();
    } else {
      popover.open(this.element.nativeElement);
    }
  }

  /** Escape on the trigger closes its popover (focus may sit here, not inside). */
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !event.defaultPrevented && this.popover().isOpen()) {
      event.preventDefault();
      this.popover().close();
    }
  }
}
