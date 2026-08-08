// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  contentChildren,
  effect,
  ElementRef,
  input,
  model,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
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
        #strip
        [class.tm-tab-group__list--faded]="stripHasMore()"
        (selectedTabChange)="onSelectedTabChange($event)"
        (wheel)="onStripWheel($event)"
        (scroll)="measureStrip($event.target)"
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

  private readonly stripRef = viewChild<ElementRef<HTMLElement>>('strip');

  /** The id the aria list shows selected: the model's when valid, else the first enabled. */
  protected readonly effectiveSelectedId = computed(() => {
    const tabs = this.tabs();
    const wanted = this.selectedId();
    const target = tabs.find((tab) => tab.id() === wanted && !tab.disabled());
    return (target ?? tabs.find((tab) => !tab.disabled()))?.id();
  });

  constructor() {
    // The promise has to be right on the FIRST paint too: a strip that fits
    // must never render faded, and one that overflows must say so before
    // anybody has scrolled it. Re-runs when the tab set changes.
    afterRenderEffect(() => {
      this.tabs();
      this.orientation();
      const strip = this.stripRef()?.nativeElement;
      untracked(() => this.measureStrip(strip ?? null));
    });

    // The promise also goes stale when the strip RESIZES: narrowed, tabs
    // overflow with no fade; widened, the fade keeps dimming the last tab
    // for a scroll that would do nothing. Neither fires a scroll event
    // (the offset never leaves 0), and in a zoneless app a resize alone
    // re-runs nothing — so the strip's own box is observed.
    effect((onCleanup) => {
      const strip = this.stripRef()?.nativeElement;
      if (strip === undefined) {
        return;
      }
      const observer = new ResizeObserver(() => this.measureStrip(strip));
      observer.observe(strip);
      onCleanup(() => observer.disconnect());
    });
  }

  /** User selections write back into the model. */
  protected onSelectedTabChange(id: string | undefined): void {
    if (id !== undefined && id !== this.selectedId()) {
      this.selectedId.set(id);
    }
  }

  /**
   * Whether the strip has anything left to scroll to. The trailing fade is
   * a PROMISE that there is more that way; left on at the end of the strip
   * it dims the last tab for nothing and says to keep scrolling when
   * scrolling does nothing.
   */
  protected readonly stripHasMore = signal(false);

  /** Re-reads the promise from the strip's own scroll position. */
  protected measureStrip(strip: EventTarget | null): void {
    if (!(strip instanceof HTMLElement)) {
      return;
    }
    // abs(): under RTL the scroll offset runs 0 down to -max, so the
    // distance travelled is its magnitude either way.
    const travelled = Math.abs(strip.scrollLeft);
    // A pixel of slack: fractional layout leaves the offset a hair short of
    // the true end, and a fade that never quite comes off is the bug.
    this.stripHasMore.set(strip.scrollWidth - strip.clientWidth - travelled > 1);
  }

  /**
   * A vertical wheel over a HORIZONTAL strip scrolls it sideways. Touch and
   * trackpads already pan a horizontal scroller, and the strip hides its
   * scrollbar, so without this a mouse-wheel user has no way to reach a tab
   * that has scrolled past the fade: the keyboard would move the selection
   * rather than just the view.
   */
  protected onStripWheel(event: WheelEvent): void {
    if (this.orientation() !== 'horizontal' || event.deltaY === 0) {
      return;
    }
    const strip = event.currentTarget as HTMLElement;
    const limit = strip.scrollWidth - strip.clientWidth;
    if (limit <= 0) {
      return;
    }
    // Under RTL the scroll offset runs 0 down to -limit, and a wheel "down"
    // still means "further along the strip" — which is leftwards there.
    const rtl = getComputedStyle(strip).direction === 'rtl';
    const delta = rtl ? -event.deltaY : event.deltaY;
    const [low, high] = rtl ? [-limit, 0] : [0, limit];
    // Only while it has somewhere left to go: swallowing the wheel at either
    // end would trap the page's own scroll under the pointer.
    const clamped = Math.max(low, Math.min(high, strip.scrollLeft + delta));
    if (clamped === strip.scrollLeft) {
      return;
    }
    event.preventDefault();
    strip.scrollLeft = clamped;
    this.measureStrip(strip);
  }
}
