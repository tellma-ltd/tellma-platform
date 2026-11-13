// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, ViewEncapsulation } from '@angular/core';
import { ChevronRight } from 'lucide';

import { TmIcon } from '@tellma/core-ui/icon';

/**
 * A page's header: a title row and a toolbar row, each at least the field
 * height, then a band below them. It has no inputs; the page fills it
 * through attribute slots:
 *
 * - Title row: `[tmPageBack]` (the crumb link back to the list, followed by
 *   a chevron separator), `[tmPageTitle]` (the `h1`, 16 px semibold, one
 *   line with an ellipsis), `[tmPageTitleExtra]` (badges beside the title),
 *   then flexible space and `[tmPageHeaderEnd]` at the row's end.
 * - Toolbar row: `[tmPageToolbar]` at its start and `[tmPageToolbarEnd]` at
 *   its end. One line that clips; the row is absent while both slots are
 *   empty.
 * - `[tmPageHeaderBand]`: full width below the rows, wrapping onto as many
 *   lines as its content needs, so a parameter bar is never clipped. Absent
 *   while empty.
 *
 * The end slots pack toward the inline end, so hiding a slot's first item
 * moves nothing after it. The padding is 12×14 px, or 10 px while the
 * header is narrower than the `sm` breakpoint (576 px).
 *
 * ```html
 * <tm-page-header>
 *   <a tmPageBack href="/centers">Centers</a>
 *   <h1 tmPageTitle>Jeddah sales</h1>
 *   <tm-badge tmPageTitleExtra tone="success">Active</tm-badge>
 *   <button tmButton tmPageToolbar>Edit</button>
 * </tm-page-header>
 * ```
 *
 * @tmGroup layout
 * @tmA11yNotes Imposes no roles: the page supplies the `h1`, the crumb link
 *   and the controls, which keep their own semantics and their reading
 *   order. The chevron after the crumb link is hidden from assistive
 *   technology. No `role="toolbar"` is imposed on the toolbar row.
 */
@Component({
  selector: 'tm-page-header',
  imports: [TmIcon],
  // The stylesheet styles the projected slot content (the title, the crumb
  // link), so it is not encapsulated; every selector is namespaced under
  // .tm-page-header.
  encapsulation: ViewEncapsulation.None,
  template: `
    <!-- One padded column inside the host, which is the container the
         narrow padding queries. -->
    <div class="tm-page-header__inner">
      <!-- Row 1: crumb link and separator, title, extras, then the end slot. -->
      <div class="tm-page-header__row tm-page-header__title-row">
        <span class="tm-page-header__back">
          <ng-content select="[tmPageBack]" />
          <tm-icon class="tm-page-header__separator" [svg]="separator" />
        </span>
        <ng-content select="[tmPageTitle]" />
        <span class="tm-page-header__extra"><ng-content select="[tmPageTitleExtra]" /></span>
        <span class="tm-page-header__end"><ng-content select="[tmPageHeaderEnd]" /></span>
      </div>
      <!-- Row 2: the toolbar's start and end slots, on one clipped line. -->
      <div class="tm-page-header__row tm-page-header__toolbar">
        <div class="tm-page-header__toolbar-start"><ng-content select="[tmPageToolbar]" /></div>
        <div class="tm-page-header__end"><ng-content select="[tmPageToolbarEnd]" /></div>
      </div>
      <!-- The band: full width, wrapping. -->
      <div class="tm-page-header__band"><ng-content select="[tmPageHeaderBand]" /></div>
    </div>
  `,
  styleUrl: './tm-page-header.css',
  host: { class: 'tm-page-header' },
})
export class TmPageHeader {
  /** The separator after the crumb link, pointing along the reading direction. */
  protected readonly separator = { node: ChevronRight, mirrorInRtl: true };
}
