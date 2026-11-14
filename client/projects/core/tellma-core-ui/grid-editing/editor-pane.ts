// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// A popup column's editor pane (spec 0010 §C.15): the editor session mounts
// it in the editing cell's outlet and the editor inside it, so the editor
// renders in an anchored top-layer pane over the cell instead of inside the
// cell box, and an editor taller than a row spills past the grid's edge
// while every row keeps its height. The pane is hosted by the cell's editor
// box (`popoverHost: parent`), so it stays a DOM descendant of the gridcell:
// focus inside it is focus inside the grid, and commit-on-blur keeps its
// meaning. The grid's core owns the keys and the session; this component
// owns the pane's geometry, its visibility while the cell is scrolled away,
// and its ARIA.

import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import { CdkConnectedOverlay } from '@angular/cdk/overlay';
import { ScrollDispatcher } from '@angular/cdk/scrolling';
import type { ConnectedPosition } from '@angular/cdk/overlay';

import { tmCreateAnchoredOverlay } from '@tellma/core-ui/overlay';

import type { ɵTmGridEditorPopupVm } from './editor-session';

/**
 * The pane's placements, in preference order: over the cell's own box,
 * growing toward the block end; else from the cell's bottom edge, growing
 * upward. Both align to the cell's inline start, which the CDK resolves
 * against the direction.
 */
const PANE_POSITIONS: readonly ConnectedPosition[] = [
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'top' },
  { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'bottom' },
];

/** The form controls a pane can hold; two or more make it a labelled group. */
const CONTROL_SELECTOR =
  'input:not([type="hidden"]), select, textarea, [contenteditable]:not([contenteditable="false"]), ' +
  '[role="combobox"], [role="listbox"], [role="radiogroup"], [role="spinbutton"], ' +
  '[role="slider"], [role="switch"], [role="checkbox"], [role="textbox"]';

/**
 * The editing cell's popup pane. The editor session creates it in the
 * cell's editor outlet while a popup column's editor is open (or its
 * read-only view) and mounts the editor into its `outlet`.
 */
@Component({
  selector: 'tm-grid-editor-pane',
  imports: [CdkConnectedOverlay],
  template: `
    <!-- The pane: a top-layer overlay over the cell. Escape and the
         outside press belong to the grid's core. -->
    <ng-template
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="true"
      (attach)="anchored.handleAttach()"
      (detach)="anchored.handleDetach()"
    >
      <div
        #body
        class="tm-grid__popup"
        data-tm-editor
        data-tm-popup
        [attr.data-tm-readonly]="readonly() ? '' : null"
        [class.tm-grid__popup--hidden]="hidden()"
        [attr.role]="group() ? 'group' : null"
        [attr.aria-label]="group() ? popup().header() : null"
        [style.inline-size.px]="inlineSize()"
        [style.min-block-size.px]="cellHeight()"
        [style.max-block-size]="maxBlockSize()"
      >
        <ng-container #outlet />
      </div>
    </ng-template>
  `,
  styleUrl: './editor-pane.css',
})
export class ɵTmGridEditorPane {
  /** The column's pane: its least width, its greatest height and its label. */
  readonly popup = input.required<ɵTmGridEditorPopupVm>();
  /** Whether the pane shows its cell read-only (no session: nothing commits). */
  readonly readonly = input(false);
  /** Where the session mounts the editor, inside the pane; set once the pane renders. */
  readonly outlet = viewChild('outlet', { read: ViewContainerRef });

  private readonly host: HTMLElement = inject(ElementRef).nativeElement;

  /**
   * The cell's editor box, which fills the cell: the pane covers it and is
   * hosted by it. Read on use (the host is not yet in it while constructed).
   */
  private get cell(): HTMLElement {
    return this.host.parentElement!;
  }

  /** The cell's border-box width, measured at open and on every resize. */
  private readonly cellWidth = signal(0);
  /** The cell's border-box height: the pane covers at least the cell. */
  protected readonly cellHeight = signal(0);
  /** The pane's width: the cell's, or the column's least width when wider. */
  protected readonly inlineSize = computed(() =>
    Math.max(this.cellWidth(), this.popup().minWidth),
  );
  /**
   * The pane's greatest height: the column's, capped by the viewport less
   * its margin on each side. The content scrolls inside beyond it.
   */
  protected readonly maxBlockSize = computed(
    () => `min(${this.popup().maxHeight}px, calc(100vh - 2 * var(--popover-viewport-margin)))`,
  );
  /** Whether the cell is out of the grid's view, which hides the pane (its state kept). */
  protected readonly hidden = signal(false);
  /** Whether the pane holds more than one control, which makes it a labelled group. */
  protected readonly group = signal(false);

  private readonly overlay = viewChild(CdkConnectedOverlay);
  private readonly body = viewChild<ElementRef<HTMLElement>>('body');

  /**
   * The anchored-overlay wiring: the cell is both the origin and the
   * popover's parent, and the pane is pushed back inside the viewport when
   * neither placement fits. The pane re-measures itself whenever it or the
   * cell resizes (see the constructor), so no post-attach strategy is set.
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.cell,
    positions: PANE_POSITIONS,
    keepOnScreen: true,
    viewportMargin: 8,
    popoverHost: () => ({ type: 'parent', element: this.cell }),
  });

  constructor() {
    const scrolls = inject(ScrollDispatcher);

    effect((onCleanup) => {
      const body = this.body()?.nativeElement;
      if (body === undefined) {
        return;
      }
      // Size and placement: the cell's box (a column resize) sets the
      // pane's width and least height, and the pane's own box (the editor
      // mounting after the attach, a sub-form growing) re-places it, so the
      // flip and the push always see the real size.
      this.measureCell();
      this.measureInView();
      const resize = new ResizeObserver(() => {
        this.measureCell();
        this.overlay()?.overlayRef?.updatePosition();
      });
      resize.observe(this.cell);
      resize.observe(body);
      // ARIA: counted again whenever the content changes, so a sub-form
      // that reveals a second field becomes a group.
      const mutations = new MutationObserver(() => this.countControls(body));
      mutations.observe(body, { childList: true, subtree: true });
      this.countControls(body);
      // Visibility: the CDK keeps the pane on its cell while the grid or
      // the page scrolls; once the cell is out of the grid's view, the
      // pane hides.
      const scrolled = scrolls.ancestorScrolled(this.cell, 0).subscribe(() => this.measureInView());
      onCleanup(() => {
        resize.disconnect();
        mutations.disconnect();
        scrolled.unsubscribe();
      });
    });
  }

  /** Reads the cell's box into the pane's width and least height. */
  private measureCell(): void {
    const box = this.cell.getBoundingClientRect();
    this.cellWidth.set(box.width);
    this.cellHeight.set(box.height);
  }

  /**
   * Re-checks the cell against the grid's scroller. The sticky header
   * covers the top band, so a cell behind it counts as out of view too.
   */
  private measureInView(): void {
    const scroller = this.cell.closest('.tm-grid__scroller');
    if (scroller === null) {
      return;
    }
    const cell = this.cell.getBoundingClientRect();
    const view = scroller.getBoundingClientRect();
    const header = scroller.querySelector('.tm-grid__header')?.getBoundingClientRect();
    const top = header === undefined ? view.top : header.bottom;
    this.hidden.set(
      !(cell.bottom > top && cell.top < view.bottom && cell.right > view.left && cell.left < view.right),
    );
  }

  /**
   * Counts the pane's controls: the outermost of nested matches only, and
   * none inside an overlay nested in the pane (an open option list).
   */
  private countControls(body: HTMLElement): void {
    let count = 0;
    for (const control of body.querySelectorAll(CONTROL_SELECTOR)) {
      const outer = control.parentElement?.closest(CONTROL_SELECTOR) ?? null;
      if (
        (outer === null || !body.contains(outer)) &&
        control.closest('.cdk-overlay-pane, [data-tm-popup]') === body
      ) {
        count++;
      }
    }
    this.group.set(count > 1);
  }
}
