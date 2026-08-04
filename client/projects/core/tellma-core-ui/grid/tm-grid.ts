// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';

import { ɵTmGridBase } from './internal/grid-base';
import { ɵTmGridView } from './internal/grid-view';

/**
 * Spreadsheet-grade data grid: virtualized rendering (100k-row readonly
 * data), Excel-style range selection and keyboard navigation, spreadsheet
 * clipboard interop (TSV + HTML flavors), column resizing, and state
 * memory (column widths per grid, scroll/selection per content) across
 * remounts. Bind `data` for readonly rows or a Signal Forms `field` over
 * the rows array for editable screens, declare `tm-grid-column` children
 * in display order, and always provide `gridId` and `rowId`.
 *
 * ```html
 * <tm-grid gridId="invoice-lines" [data]="lines()" [rowId]="lineId">
 *   <tm-grid-column key="description" header="Description" [flex]="2" />
 *   <tm-grid-column key="quantity" type="number" header="Qty" [width]="90" />
 *   <tm-grid-column key="isPosted" type="boolean" header="Posted" />
 * </tm-grid>
 * ```
 *
 * @tmGroup grid
 * @tmA11yNotes The container is `role="grid"` with full `aria-rowcount`/
 *   `aria-colcount` over the virtualized model; the active cell holds real
 *   focus via a roving tabindex, and the active row is always rendered so
 *   scrolling never drops focus. A readonly grid is a single tab stop;
 *   Escape parks focus on the container so Tab exits mid-grid, and any
 *   arrow re-enters at the active cell. Selection, clipboard, loading,
 *   checked-count, and find-counter changes are announced through the live
 *   region in the active locale. With `selectable`, the labelled row
 *   checkboxes toggle via Space (select-all via Ctrl+Shift+Space) and
 *   checked rows carry row-level `aria-selected`; the find bar's field and
 *   buttons are labelled from library strings.
 */
@Component({
  selector: 'tm-grid',
  imports: [ɵTmGridView],
  template: `<tm-grid-view [core]="core" />`,
  styleUrl: './tm-grid.css',
  host: {
    class: 'tm-grid',
    '[class.tm-grid--readonly]': '!core.editable()',
    '[class.tm-grid--sm]': 'size() === "sm"',
    '[class.tm-grid--lg]': 'size() === "lg"',
    '[style.--grid-template]': 'core.gridTemplate()',
  },
})
export class TmGrid<T> extends ɵTmGridBase<T> {}
