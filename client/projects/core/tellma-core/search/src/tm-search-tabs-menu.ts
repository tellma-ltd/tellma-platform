// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  output,
  viewChild,
  type ElementRef,
  type InputSignal,
  type OutputEmitterRef,
} from '@angular/core';
import ChevronDown from 'lucide/dist/esm/icons/chevron-down.mjs';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmMenu, type TmMenuEntry } from '@tellma/core-ui/menu';

import type { TmSearchTabView } from './tm-search-filters-host';

// The tabs in their menu-button form, at `sm` and `md`: a button as wide as
// the widest tab's label that opens a menu of the tabs, the shown one
// checked. It loads in a chunk of its own when the page first takes that
// form, so a page shown only wider or narrower never downloads it.

/**
 * The search page's tabs as a menu button. Internal to the search page,
 * which renders it on the title's row at `sm` and `md`.
 */
@Component({
  selector: 'tm-search-tabs-menu',
  imports: [TmButton, TmIcon, TmMenu],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The button: ArrowDown and ArrowUp open the menu on its first and last row, as a menu button's do. -->
    <button
      #button
      tmButton
      type="button"
      variant="ghost"
      data-tm-tabs
      aria-haspopup="menu"
      [attr.aria-expanded]="open() ? 'true' : 'false'"
      (click)="toggle(button, 'first')"
      (keydown)="key($event, button)"
    >
      <!-- Every label in one cell, the current one visible, so the button is as wide as the widest. -->
      <span class="tm-search-tabs-menu__labels">
        @for (tab of tabs(); track tab.key) {
          <span [class.tm-search-tabs-menu__label--current]="tab.key === tabKey()">{{ tab.label }}</span>
        }
      </span>
      <tm-icon [svg]="chevron" />
    </button>
    <tm-menu #menu [items]="items()" [aria-label]="label()" />
  `,
  styles: `
    :host {
      display: inline-flex;
    }

    /* Every label shares one grid cell and only the current one shows, so the
       button is as wide as the widest label and never resizes on a switch. */
    .tm-search-tabs-menu__labels {
      display: inline-grid;
    }

    .tm-search-tabs-menu__labels > span {
      grid-area: 1 / 1;
      visibility: hidden;
      white-space: nowrap;
    }

    .tm-search-tabs-menu__labels > .tm-search-tabs-menu__label--current {
      visibility: visible;
    }
  `,
})
export class ɵTmSearchTabsMenu {
  /** The declared tabs, each with its label in the active language. */
  readonly tabs: InputSignal<readonly TmSearchTabView[]> = input.required<readonly TmSearchTabView[]>();

  /** The shown tab's key. */
  readonly tabKey: InputSignal<string | null> = input.required<string | null>();

  /** The menu's accessible name. */
  readonly label: InputSignal<string> = input.required<string>();

  /** Emits the key of the tab picked from the menu. */
  readonly tabSelect: OutputEmitterRef<string> = output<string>();

  /** The chevron the button ends with. */
  protected readonly chevron = ChevronDown;

  /** The menu. */
  private readonly menu = viewChild.required(TmMenu);

  /** The button that opens the menu. */
  private readonly button = viewChild.required<ElementRef<HTMLButtonElement>>('button');

  /** The menu's items: one radio group of the tabs, the shown one checked. */
  protected readonly items = computed<readonly TmMenuEntry[]>(() => {
    const shown = this.tabKey();
    return [
      {
        group: true,
        items: this.tabs().map((tab) => ({ id: tab.key, label: tab.label, role: 'radio', checked: tab.key === shown, action: () => this.tabSelect.emit(tab.key) })),
      },
    ];
  });

  /** Whether the menu is open at its button. */
  protected readonly open = computed(() => this.menu().isOpen() && this.menu().ɵisOpenFor(this.button().nativeElement));

  /** Makes the menu know its button, so a press on it never counts as an outside press. */
  constructor() {
    effect((onCleanup) => onCleanup(this.menu().ɵregisterTrigger(this.button().nativeElement)));
  }

  /**
   * Opens or closes the menu from its button.
   *
   * @param button - The button.
   * @param focus - The row that takes focus as the menu opens.
   */
  protected toggle(button: HTMLButtonElement, focus: 'first' | 'last'): void {
    const menu = this.menu();
    if (menu.ɵisOpenFor(button)) {
      menu.close();
    } else {
      menu.open(button, { restoreFocus: button, initialFocus: focus });
    }
  }

  /**
   * ArrowDown opens the menu on its first row and ArrowUp on its last.
   *
   * @param event - The key.
   * @param button - The button.
   */
  protected key(event: KeyboardEvent, button: HTMLButtonElement): void {
    if ((event.key !== 'ArrowDown' && event.key !== 'ArrowUp') || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) {
      return;
    }
    // The arrow would otherwise scroll the page behind the menu.
    event.preventDefault();
    if (!this.open()) {
      this.toggle(button, event.key === 'ArrowDown' ? 'first' : 'last');
    }
  }
}
