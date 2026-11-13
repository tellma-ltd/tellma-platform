// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  booleanAttribute,
  Component,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  isDevMode,
  untracked,
} from '@angular/core';
import { AriaDescriber } from '@angular/cdk/a11y';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

import { ɵTM_GRID_HEADER_ACTIVITY } from './internal/header-activity';

/**
 * A column header's action button — a filter or a column editor — placed in
 * a `tmGridHeaderEnd` or `tmGridRowEndHeader` template: a 24 × 24 px icon
 * target inside the header height. `active` marks the state the action
 * controls as on (a filter applied, a popover open) with the accent color
 * and a hidden "Active" description, which the header holding the button
 * carries as well, since keyboard focus rests on the header.
 *
 * The button never takes a tab stop of its own: the grid's header navigation
 * reaches it, where Enter, Space or Alt+ArrowDown click it, and focus on it
 * counts as focus on its header, so a popover closing back onto the button
 * continues the header navigation.
 *
 * @example
 * ```html
 * <ng-template tmGridHeaderEnd>
 *   <button tmGridHeaderAction [active]="nameFiltered()" [tmPopoverTriggerFor]="nameFilter"
 *           aria-label="Filter by name"><tm-icon [svg]="funnel" /></button>
 * </ng-template>
 * ```
 *
 * @tmGroup grid
 * @tmA11yNotes An icon-only native button: give it an `aria-label` (a
 *   dev-mode warning fires otherwise). While `active`, its accessible
 *   description is "Active"; the description is merged into
 *   `aria-describedby`, so bind none of your own on the button. The header
 *   cell, where keyboard focus rests, is described as "Active" too.
 */
@Component({
  // An attribute on a native button, so the button keeps its own semantics;
  // a component rather than a directive only to carry its styles, which a
  // consumer's projected button could not otherwise receive.
  // eslint-disable-next-line @angular-eslint/component-selector -- see above
  selector: 'button[tmGridHeaderAction]',
  template: '<ng-content />',
  styleUrl: './tm-grid-header-action.css',
  host: {
    class: 'tm-grid-header-action',
    // Out of the tab order: the header navigation is the keyboard route.
    tabindex: '-1',
    '[class.tm-grid-header-action--active]': 'active()',
  },
})
export class TmGridHeaderAction {
  /** Whether the state the action controls is on: accent styling and an "Active" description. */
  readonly active = input(false, { transform: booleanAttribute });

  /** The button element. */
  private readonly element = inject<ElementRef<HTMLButtonElement>>(ElementRef).nativeElement;

  constructor() {
    // A button inside a form submits by default; an authored type wins.
    if (!this.element.hasAttribute('type')) {
      this.element.setAttribute('type', 'button');
    }

    // The "Active" description, through the CDK describer so it merges with
    // any description the button already carries and is read whether or not
    // the text is visible.
    const describer = inject(AriaDescriber);
    const activeText = inject(TM_UI_TRANSLATE)('grid.headerActionActive');
    let described: string | null = null;
    const clear = (): void => {
      if (described !== null) {
        describer.removeDescription(this.element, described);
        described = null;
      }
    };
    effect(() => {
      const text = this.active() ? activeText() : null;
      untracked(() => {
        clear();
        if (text !== null) {
          describer.describe(this.element, text);
          described = text;
        }
      });
    });
    inject(DestroyRef).onDestroy(clear);

    // The header holding the button is described too: header navigation
    // focuses the header cell, never the button, and a screen reader reads
    // only the focused element's description.
    const activity = inject(ɵTM_GRID_HEADER_ACTIVITY, { optional: true });
    if (activity !== null) {
      effect(() => activity.setHeaderActionActive(this.element, this.active()));
      inject(DestroyRef).onDestroy(() => activity.setHeaderActionActive(this.element, false));
    }

    if (isDevMode()) {
      // An icon-only button needs a name of its own.
      afterNextRender(() => {
        const hasText = (this.element.textContent ?? '').trim() !== '';
        const hasName =
          this.element.hasAttribute('aria-label') || this.element.hasAttribute('aria-labelledby');
        if (!hasText && !hasName) {
          console.warn(
            'tmGridHeaderAction: icon-only button without an accessible name — add aria-label ' +
              'or aria-labelledby.',
            this.element,
          );
        }
      });
    }
  }
}
