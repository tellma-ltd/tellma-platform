// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, contentChildren, input, model } from '@angular/core';
import { Tab, TabContent, TabList, TabPanel, Tabs } from '@angular/aria/tabs';

import { TmTab } from './tm-tab';

/**
 * How arrowing through the tab strip selects: `follow` (the APG automatic-
 * activation model — selection follows focus) or `explicit` (arrows move
 * focus only; Enter/Space/click select — the choice for expensive panels,
 * since keyboard scanning then instantiates nothing).
 */
export type TmTabSelectionMode = 'follow' | 'explicit';

/**
 * Tabbed container over projected `tm-tab` definitions. The group renders
 * the real aria tab strip and panels itself (aria directives cannot be
 * content-projected across DI boundaries); the aria pattern owns roles,
 * roving focus, direction-aware arrow navigation, `Home`/`End`, and
 * selection state.
 *
 * Only the active tab's content is in the DOM: a panel's content
 * instantiates on first activation and is destroyed on deactivation —
 * unless the tab opts into `preserveContent`, which keeps the activated
 * panel's DOM alive (hidden + inert). The tab strip scrolls on overflow
 * (momentum scroll, no pagination buttons); the group is a pure UI
 * container (router-driven tabs are out of scope).
 *
 * `selectedId` defaults to the first enabled tab at display level — the
 * model is never mutated by the defaulting, so a bound id pointing at a
 * removed tab falls back visibly without clobbering consumer state.
 *
 * @tmGroup layout
 * @tmA11yNotes role="tablist"/"tab"/"tabpanel" with roving tabindex from
 *   the aria pattern; arrows are direction-mapped; panels are focusable
 *   (tabindex 0); inactive preserved panels are inert.
 */
@Component({
  selector: 'tm-tab-group',
  imports: [NgTemplateOutlet, Tab, TabContent, TabList, TabPanel, Tabs],
  template: `
    <div ngTabs class="tm-tab-group__tabs">
      <div
        ngTabList
        class="tm-tab-group__list"
        [orientation]="orientation()"
        [selectionMode]="selectionMode()"
        [selectedTab]="effectiveSelectedId()"
        (selectedTabChange)="onSelectedTabChange($event)"
      >
        @for (tab of tabs(); track tab.id()) {
          <button
            type="button"
            ngTab
            class="tm-tab-group__tab"
            [value]="tab.id()"
            [disabled]="tab.disabled()"
          >
            @if (tab.labelTemplate(); as labelTemplate) {
              <ng-container [ngTemplateOutlet]="labelTemplate.template" />
            } @else {
              {{ tab.label() }}
            }
          </button>
        }
      </div>
      @for (tab of tabs(); track tab.id()) {
        <div
          ngTabPanel
          class="tm-tab-group__panel"
          [value]="tab.id()"
          [preserveContent]="tab.preserveContent()"
        >
          <ng-template ngTabContent>
            @if (tab.content(); as content) {
              <ng-container [ngTemplateOutlet]="content.template" />
            }
          </ng-template>
        </div>
      }
    </div>
  `,
  styleUrl: './tm-tab-group.css',
  host: { class: 'tm-tab-group' },
})
export class TmTabGroup {
  /**
   * The selected tab's id (two-way). While unset — or while it names a
   * missing or disabled tab — the first enabled tab displays selected,
   * without the model being rewritten.
   */
  readonly selectedId = model<string | undefined>(undefined);
  /** The activation model. Default `follow` (selection follows focus). */
  readonly selectionMode = input<TmTabSelectionMode>('follow');
  /** The strip's orientation. Default `horizontal`. */
  readonly orientation = input<'horizontal' | 'vertical'>('horizontal');

  /** The projected tab definitions, in display order. */
  protected readonly tabs = contentChildren(TmTab);

  /** The id the aria list shows selected: the model's when valid, else the first enabled. */
  protected readonly effectiveSelectedId = computed(() => {
    const tabs = this.tabs();
    const wanted = this.selectedId();
    const target = tabs.find((tab) => tab.id() === wanted && !tab.disabled());
    return (target ?? tabs.find((tab) => !tab.disabled()))?.id();
  });

  /** User selections write back into the model. */
  protected onSelectedTabChange(id: string | undefined): void {
    if (id !== undefined && id !== this.selectedId()) {
      this.selectedId.set(id);
    }
  }
}
