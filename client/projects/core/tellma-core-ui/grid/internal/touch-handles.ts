// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  afterRenderEffect,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';

import type { ɵTmGridViewCore } from './grid-core';

/** One handle's position in the spacer's content space, in px. */
interface HandlePoint {
  readonly x: number;
  readonly y: number;
}

/** Both handles' positions; a `null` member is hidden (corner off-window). */
interface HandlePositions {
  readonly start: HandlePoint | null;
  readonly end: HandlePoint | null;
}

function samePoint(a: HandlePoint | null, b: HandlePoint | null): boolean {
  return a === b || (a !== null && b !== null && a.x === b.x && a.y === b.y);
}

/**
 * The coarse-pointer range-selection handles (the Sheets/Excel-mobile
 * pattern): two round drag grips at the active range's start and end
 * corners. Rendered inside the grid's spacer, so positions live in content
 * space and scroll with the rows for free. The positions are MEASURED from
 * the corner cells' rects after each render — proportional column widths
 * resolve in CSS, and measured geometry is direction-safe by construction;
 * a corner whose cell left the rendered window hides its handle. Dragging
 * hands off to the core's shared drag pipeline (`beginHandleDrag`:
 * pointer capture on the handle, elementFromPoint tracking, edge
 * auto-scroll); an open editor hides the handles through the core signal.
 */
@Component({
  selector: 'tm-grid-touch-handles',
  template: `
    @if (positions(); as pos) {
      @if (pos.start; as start) {
        <div
          class="tm-grid__handle"
          data-tm-handle="start"
          [style.left.px]="start.x"
          [style.top.px]="start.y"
          (pointerdown)="core().beginHandleDrag($event, 'start')"
        ></div>
      }
      @if (pos.end; as end) {
        <div
          class="tm-grid__handle"
          data-tm-handle="end"
          [style.left.px]="end.x"
          [style.top.px]="end.y"
          (pointerdown)="core().beginHandleDrag($event, 'end')"
        ></div>
      }
    }
  `,
  styleUrl: './touch-handles.css',
  host: { class: 'tm-grid-touch-handles', 'aria-hidden': 'true' },
})
export class ɵTmGridTouchHandles {
  private readonly host = inject(ElementRef).nativeElement as HTMLElement;
  private readonly destroyRef = inject(DestroyRef);

  /** The composition root the handles read the active range from. */
  readonly core = input.required<ɵTmGridViewCore>();

  /** The measured handle positions, or `null` while nothing shows. */
  protected readonly positions = signal<HandlePositions | null>(null);

  constructor() {
    // Re-measure whenever the selection or the rendered window changed —
    // both are render-driving signals, so measuring after render sees the
    // final geometry of the corner cells.
    afterRenderEffect(() => {
      const core = this.core();
      const anchors = core.selectionHandles();
      core.renderRows();
      untracked(() => this.measure(anchors));
    });

    // A column resize (or any width change — an added/removed column, the
    // checkbox chrome toggling) rewrites the grid's `--grid-template` custom
    // property but moves none of the signals the effect above tracks, so the
    // corner cells shift under handles that never re-measure. That property
    // lives as an inline style on the grid host, and it is the only style
    // written there; observe its mutations and re-measure against the new
    // column geometry. (Density changes flow through `renderRows` already.)
    afterNextRender(() => {
      const templateHost = this.host.closest('tm-grid, tm-tree-grid');
      if (templateHost === null) {
        return;
      }
      const observer = new MutationObserver(() =>
        untracked(() => this.measure(this.core().selectionHandles())),
      );
      observer.observe(templateHost, { attributeFilter: ['style'] });
      this.destroyRef.onDestroy(() => observer.disconnect());
    });
  }

  /** Resolves both corners' cells to content-space points (or hides them). */
  private measure(
    anchors: { readonly start: { row: number; col: number }; readonly end: { row: number; col: number } } | null,
  ): void {
    let next: HandlePositions | null = null;
    if (anchors !== null) {
      const scroller = this.host.closest('.tm-grid__scroller');
      if (scroller !== null) {
        const hostRect = this.host.getBoundingClientRect();
        const rtl = getComputedStyle(this.host).direction === 'rtl';
        const cellRect = (corner: { row: number; col: number }): DOMRect | null =>
          scroller
            .querySelector(`[data-tm-cell][data-row="${corner.row}"][data-col="${corner.col}"]`)
            ?.getBoundingClientRect() ?? null;
        const startRect = cellRect(anchors.start);
        const endRect = cellRect(anchors.end);
        // start = the top-start cell's top-start corner; end = the
        // bottom-end cell's bottom-end corner (inline edges flip in RTL).
        const start: HandlePoint | null =
          startRect === null
            ? null
            : {
                x: (rtl ? startRect.right : startRect.left) - hostRect.left,
                y: startRect.top - hostRect.top,
              };
        const end: HandlePoint | null =
          endRect === null
            ? null
            : {
                x: (rtl ? endRect.left : endRect.right) - hostRect.left,
                y: endRect.bottom - hostRect.top,
              };
        if (start !== null || end !== null) {
          next = { start, end };
        }
      }
    }
    const current = untracked(this.positions);
    const unchanged =
      current === next ||
      (current !== null &&
        next !== null &&
        samePoint(current.start, next.start) &&
        samePoint(current.end, next.end));
    if (!unchanged) {
      this.positions.set(next);
    }
  }
}
