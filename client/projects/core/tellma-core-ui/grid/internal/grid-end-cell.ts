// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, input } from '@angular/core';

import type { ɵTmGridRowVm, ɵTmGridViewCore } from './grid-core';

/**
 * A cell of the row-end column: its header when no row is given, otherwise
 * that row's body cell. Sticky at the inline end, it paints its row's band
 * over an opaque surface (with the range fill while its whole row is
 * selected), draws a hairline on its inline-start edge while content scrolls
 * under it, and projects the consumer's row-end template.
 *
 * A component of its own, like the find bar and the touch handles, so the
 * column's sticky layering, band repainting, roles, indexes and tab stop
 * live together in one place, and its stylesheet stands apart from the grid
 * view's.
 */
@Component({
  selector: 'tm-grid-end-cell',
  imports: [NgTemplateOutlet],
  // The header projects the header template; a body cell, the row template
  // with the row's context. A placeholder (no template) renders empty.
  template: `@if (row(); as row) {
      @if (row.endCtx; as ctx) {
        <ng-container [ngTemplateOutlet]="core().rowEndDef()!.template" [ngTemplateOutletContext]="ctx" />
      }
    } @else if (core().rowEndHeaderDef(); as def) {
      <ng-container [ngTemplateOutlet]="def.template" />
    }`,
  styleUrl: './grid-end-cell.css',
  host: {
    '[class.tm-grid__endhdr]': '!row()',
    '[class.tm-grid__endcell]': '!!row()',
    '[class.tm-grid__endcell--active]': '!!row()?.endActive',
    '[class.tm-grid__endcell--selected]': '!!row()?.endSelected',
    '[attr.data-tm-endhdr]': 'row() ? null : ""',
    '[attr.data-tm-endcell]': 'row() ? "" : null',
    '[attr.data-row]': 'row()?.viewIndex ?? null',
    '[attr.role]': 'row() ? (live() ? "gridcell" : null) : "columnheader"',
    '[attr.aria-hidden]': 'live() ? null : "true"',
    '[attr.aria-colindex]': 'live() ? core().rowEndColIndex() : null',
    '[attr.data-tm-hslot]': 'live() && !row() ? core().columnModel().length : null',
    '[attr.aria-describedby]': 'live() && !row() ? core().headerActionHintId : null',
    '[attr.tabindex]': 'tabIndex()',
  },
})
export class ɵTmGridEndCell {
  /** The composition root the cell reads its state from. */
  readonly core = input.required<ɵTmGridViewCore>();

  /** The body row this cell ends; absent for the header cell. */
  readonly row = input<ɵTmGridRowVm>();

  /**
   * Whether the cell carries a template: a header with a header template, or
   * a body cell with a row template. A cell without one is an aria-hidden
   * placeholder that only keeps the column's track.
   */
  protected readonly live = computed(() => {
    const row = this.row();
    return row ? row.endCtx !== null : this.core().rowEndHeaderDef() !== undefined;
  });

  /**
   * The roving tab index: the header follows the header row's tab stop; a
   * body cell is the body's tab stop while it is the active cell. Neither
   * placeholder takes focus.
   */
  protected readonly tabIndex = computed(() => {
    const row = this.row();
    const core = this.core();
    if (!row) {
      return core.headerTabIndex(core.columnModel().length);
    }
    if (row.endCtx === null) {
      return null;
    }
    return row.endActive && core.bodyTabStop() ? 0 : -1;
  });
}
