// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component, computed, input, output, type InputSignal, type OutputEmitterRef } from '@angular/core';
import ChevronDown from 'lucide/dist/esm/icons/chevron-down.mjs';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon } from '@tellma/core-ui/icon';
import { TmMenu, TmMenuTrigger, type TmMenuEntry } from '@tellma/core-ui/menu';

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
  imports: [TmButton, TmIcon, TmMenu, TmMenuTrigger],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- The button opens the menu as a menu button does: a click, Enter, Space, or ArrowDown on its first row and
         ArrowUp on its last; it names the menu while it is open. -->
    <button tmButton type="button" variant="ghost" data-tm-tabs [tmMenuTriggerFor]="menu">
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
}
