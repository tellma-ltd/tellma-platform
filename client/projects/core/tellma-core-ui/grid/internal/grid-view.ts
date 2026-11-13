// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgComponentOutlet, NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  signal,
  untracked,
  ViewContainerRef,
  viewChild,
} from '@angular/core';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import { Directionality } from '@angular/cdk/bidi';
import { CdkScrollable } from '@angular/cdk/scrolling';
import type { ConnectedOverlayPositionChange, ConnectedPosition } from '@angular/cdk/overlay';
import { ArrowDown, ArrowUp, ChevronDown, ChevronRight } from 'lucide';

import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { TmMenu } from '@tellma/core-ui/menu';
import { tmCreateAnchoredOverlay } from '@tellma/core-ui/overlay';
import { ɵTmErrorPopover } from '@tellma/core-ui/private';
import { TmSpinner } from '@tellma/core-ui/spinner';

import { ɵTmGridFindBar } from './find-bar';
import type { ɵTmGridViewCore } from './grid-core';
import { ɵTmGridEndCell } from './grid-end-cell';
import { ɵTmGridTouchHandles } from './touch-handles';

// The tree expander's two glyphs, shared by every grid so tm-icon validates
// each once. Expanded points down; collapsed points along the reading
// direction, so it mirrors in right-to-left text (the down chevron is
// symmetric and never needs to).
const EXPANDED_GLYPH: TmIconSource = ChevronDown;
const COLLAPSED_GLYPH: TmIconSource = { node: ChevronRight, mirrorInRtl: true };

/** The ascending sort indicator: a vertical arrow, which never mirrors. */
const SORT_ASC_GLYPH: TmIconSource = ArrowUp;
/** The descending sort indicator: a vertical arrow, which never mirrors. */
const SORT_DESC_GLYPH: TmIconSource = ArrowDown;

/**
 * The grid's one and only template: scroller, sticky header, virtualized
 * row window, the row-checkbox chrome column, the loading/empty overlays,
 * the editing-cell editor outlet, the editable-mode status-bar slot, the
 * context menu, the find bar, the coarse-pointer selection handles, and
 * the active-cell error overlay. `tm-grid` and `tm-tree-grid` are thin
 * shells around this component so the large template compiles exactly
 * once. All state and behavior live in the `core` it renders from; the
 * template only binds signals and routes DOM events back into it.
 */
@Component({
  selector: 'tm-grid-view',
  imports: [
    CdkScrollable,
    NgComponentOutlet,
    NgTemplateOutlet,
    OverlayModule,
    TmIcon,
    TmMenu,
    TmSpinner,
    ɵTmErrorPopover,
    ɵTmGridEndCell,
    ɵTmGridFindBar,
    ɵTmGridTouchHandles,
  ],
  // The template lives in its own file, indented from column 0, and carries
  // no license header: the published bundle ships its text verbatim twice
  // (the component definition and its class metadata), and gzip folds the
  // second copy only while both copies, with the styles between them, fit
  // in its 32 KB window.
  templateUrl: './grid-view.html',
  styleUrls: ['./grid-view.css', './grid-view-header-nav.css'],
  host: { class: 'tm-grid-view' },
})
export class ɵTmGridView {
  /** The composition root this view renders from (built by the grid shell). */
  readonly core = input.required<ɵTmGridViewCore>();

  // draftNote: the draft is a plain input that takes the typing until the
  // editing code arrives and the editor mounts with its text. The core sets
  // its value and focuses it. It is never in the tab order: a Tab that
  // commits the draft and leaves the grid must not land on it before it
  // unrenders.

  /** The tree expander's glyph on an expanded row: a chevron pointing down. */
  protected readonly expandedGlyph = EXPANDED_GLYPH;
  /** The tree expander's glyph on a collapsed row, mirrored in right-to-left text. */
  protected readonly collapsedGlyph = COLLAPSED_GLYPH;
  /** The ascending sort indicator: an up arrow. */
  protected readonly sortAscGlyph = SORT_ASC_GLYPH;
  /** The descending sort indicator: a down arrow. */
  protected readonly sortDescGlyph = SORT_DESC_GLYPH;

  /**
   * Whether content scrolls under the row-end column — there is more of it
   * beyond the inline end — which draws the column's hairline.
   */
  protected readonly endOverlap = signal(false);

  /** The select-all checkbox's `aria-checked` value (`mixed` is tri-state). */
  protected readonly checkAllAria = computed(() => {
    const state = this.core().checkAllState();
    return state === 'mixed' ? 'mixed' : state === 'all' ? 'true' : 'false';
  });

  /** Error-overlay placement: below the cell, flipping above. */
  protected readonly errorPositions: ConnectedPosition[] = [
    { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
    { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
  ];

  /** The bubble takes a list; a cell reports one message at a time. */
  protected readonly errorMessages = computed(() => [this.core().errorMessage()]);

  /**
   * Whether the errored cell is still inside the scroller's viewport. The
   * bubble is a top-layer overlay, so nothing clips it: scrolled away from
   * its cell it would keep hanging outside the grid, pointing at a row that
   * is no longer there.
   */
  private readonly errorAnchorInView = signal(true);

  /**
   * The bubble shows for an anchored cell that is still on screen.
   *
   * The active-cell error message is a top-layer overlay, so errors
   * appearing or clearing never shift the grid's (or the page's) layout. It
   * is the same bubble tm-form-field uses, so a cell error and a field error
   * read as the same object. Unlike the field's, this one is NOT decoration:
   * the cell has no live region of its own, so the bubble carries the
   * tooltip role and the active cell's aria-describedby points at it. That
   * binding is gated on this signal together with the overlay: detached, the
   * bubble (the id's only carrier) leaves the DOM, and the still-rendered
   * cell must not describe itself by a dangling id.
   */
  protected readonly showErrorBubble = computed(
    () => this.core().errorAnchor() !== null && this.errorAnchorInView(),
  );

  /**
   * Re-checks the anchor against the scroller's viewport. The sticky header
   * covers the top band, so a cell hidden BEHIND it counts as gone too.
   */
  private measureErrorAnchorInView(): void {
    const anchor = this.core().errorAnchor();
    const scroller = this.scroller()?.nativeElement;
    if (anchor === null || scroller === undefined) {
      return;
    }
    const cell = (anchor as Element).getBoundingClientRect();
    const view = scroller.getBoundingClientRect();
    const header = scroller.querySelector('.tm-grid__header')?.getBoundingClientRect();
    const top = header === undefined ? view.top : header.bottom;
    this.errorAnchorInView.set(
      cell.bottom > top &&
        cell.top < view.bottom &&
        cell.right > view.left &&
        cell.left < view.right,
    );
  }

  /**
   * The scroller's own scroll: the core drives virtualization from it, and
   * the bubble re-checks whether the cell it points at is still on screen.
   */
  protected onScrollerScroll(event: Event): void {
    this.core().onScroll(event);
    this.measureErrorAnchorInView();
    this.measureEndOverlap();
  }

  /**
   * Re-measures {@link endOverlap} from the header row: content runs under
   * the row-end column while the last data column's inline end lies past the
   * row-end header's inline start. Geometry rather than scroll metrics,
   * because the reachable scroll range is not always `scrollWidth` less
   * `clientWidth` (a hidden scrollbar keeps its gutter in the client box but
   * not in the scrollport). Only a grid with a row-end column measures.
   */
  private measureEndOverlap(): void {
    const end = untracked(this.core().rowEnd)
      ? this.scroller()?.nativeElement.querySelector('[data-tm-endhdr]')
      : null;
    const last = end?.previousElementSibling;
    if (!end || !last?.hasAttribute('data-tm-colhdr')) {
      this.endOverlap.set(false);
      return;
    }
    const lastBox = last.getBoundingClientRect();
    const endBox = end.getBoundingClientRect();
    this.endOverlap.set(
      this.directionality.value === 'rtl'
        ? lastBox.left < endBox.right - 1
        : lastBox.right > endBox.left + 1,
    );
  }

  /** Set when the overlay flips above the cell, to turn the arrow over. */
  protected readonly errorPopoverAbove = signal(false);

  /**
   * The scrollbars' thickness. The loading / empty layer is a sibling of the
   * scroller covering the whole grid box; left flush against the edge it
   * paints over a classic scrollbar, and the grid looks like it has drawn on
   * its own chrome. Zero on overlay-scrollbar platforms.
   *
   * The layer is a sibling of the scroller, not inside its scrolling flow,
   * so it covers the visible viewport whatever the scroll position: inside
   * the scroller it would flow after the full-height row spacer and render
   * off-screen while rows are bound.
   */
  protected readonly scrollbarInline = signal(0);
  protected readonly scrollbarBlock = signal(0);

  /**
   * The overlay reports where it actually landed. A bubble that flipped
   * above the cell must turn its arrow over, or it points at the row below
   * the one it belongs to.
   */
  protected onErrorPositionChange(change: ConnectedOverlayPositionChange): void {
    this.errorPopoverAbove.set(change.connectionPair.overlayY === 'bottom');
  }

  /**
   * The error overlay's shared anchored-overlay wiring. The popover host
   * attaches to THIS component's element, not inline at the origin: inline
   * insertion would place it inside the `role="row"` element, where a
   * tooltip is not an allowed child (axe aria-required-children); the view
   * host keeps token/direction inheritance and the top layer positions it
   * all the same. The message box is statically sized, so no post-attach
   * re-measure is needed.
   */
  private readonly errorOverlay = viewChild(CdkConnectedOverlay);

  /**
   * The view's host element — the box the loading/empty overlay is inset
   * against, and the error popover's top-layer host.
   */
  private readonly hostElement = inject(ElementRef).nativeElement as HTMLElement;
  /** The reading direction, which decides which edge of a header is its inline end. */
  private readonly directionality = inject(Directionality);

  protected readonly errorAnchored = tmCreateAnchoredOverlay({
    overlay: () => this.errorOverlay(),
    origin: () => this.core().errorAnchor(),
    positions: this.errorPositions,
    popoverHost: { type: 'parent', element: this.hostElement },
    // The anchor is written from an afterRender effect and only reaches
    // CDK on the NEXT change detection, so the re-measure has to cross a
    // pass boundary — measuring in the same pass reads the old origin.
    remeasure: 'afterNextRender',
  });

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly editorOutlet = viewChild('editorOutlet', { read: ViewContainerRef });
  private readonly menu = viewChild(TmMenu);

  /**
   * Measures the loading/empty overlay's trailing insets, only while a
   * layer is actually up. offsetWidth - clientWidth is BOTH borders plus
   * the scrollbar, and the overlay's leading edges already sit INSIDE the
   * border (grid-view.css) — so the trailing inset keeps ONE border-width,
   * not two. The block inset is measured against the HOST's bottom edge:
   * on an editable grid the status bar sits between the scroller and that
   * edge, and the overlay must stop above the scrollbar, not partway into
   * the status bar.
   */
  private measureOverlayInsets(): void {
    const core = this.core();
    const scroller = this.scroller()?.nativeElement;
    if (scroller === undefined || (!core.loading() && !core.showEmpty())) {
      return;
    }
    const border = Number.parseFloat(getComputedStyle(scroller).borderTopWidth) || 0;
    this.scrollbarInline.set(scroller.offsetWidth - scroller.clientWidth - border);
    this.scrollbarBlock.set(
      this.hostElement.getBoundingClientRect().bottom -
        scroller.getBoundingClientRect().top -
        border -
        scroller.clientHeight,
    );
  }

  constructor() {
    // The error anchor moves from cell to cell WHILE the overlay stays
    // attached (arrowing between two invalid cells). CDK re-applies a
    // position only when its dedicated `origin` INPUT changes; an
    // object-form config updates the strategy's origin without
    // re-applying it, so the box would keep the first cell's screen
    // position while only its text swapped. The helper's
    // `afterNextRender` strategy defers the measure past the change
    // detection that hands CDK the new origin.
    afterRenderEffect(() => {
      const anchor = this.core().errorAnchor();
      untracked(() => {
        if (anchor !== null) {
          // A fresh anchor is on screen by construction (the grid scrolled
          // it into view to activate it); measure anyway rather than assume.
          this.measureErrorAnchorInView();
          this.errorAnchored.reanchor();
        }
      });
    });

    afterRenderEffect(() => {
      const core = this.core();
      const scroller = this.scroller();
      if (scroller !== undefined) {
        untracked(() => core.attachScroller(scroller.nativeElement));
      }
    });
    // The editing cell's outlet exists only while a session renders; the
    // core forces that render synchronously and mounts right after (see
    // ɵTmGridCore.openEditor) — this effect only keeps the reference fresh.
    afterRenderEffect(() => {
      const core = this.core();
      const outlet = this.editorOutlet();
      untracked(() => core.attachEditorOutlet(outlet ?? null));
    });
    afterRenderEffect(() => {
      const core = this.core();
      const menu = this.menu();
      untracked(() => core.attachMenu(menu ?? null));
    });
    // Measured after render so the scroller has its final box, re-running
    // when a layer goes up or the scroller arrives.
    afterRenderEffect(() => {
      const core = this.core();
      core.loading();
      core.showEmpty();
      this.scroller();
      untracked(() => this.measureOverlayInsets());
    });
    // Re-measured whenever the scroller's box changes: a window resize can
    // toggle a scrollbar while a layer is up without any signal changing,
    // and content-box observation sees both outer resizes and a scrollbar
    // appearing or disappearing.
    // The header row is observed too: its box grows and shrinks with the
    // column tracks (a resize, a column added), which changes how much of
    // the content hides past the row-end column without moving the scroller.
    effect((onCleanup) => {
      const scroller = this.scroller()?.nativeElement;
      if (scroller === undefined) {
        return;
      }
      const observer = new ResizeObserver(() => {
        this.measureOverlayInsets();
        this.measureEndOverlap();
      });
      observer.observe(scroller);
      const header = scroller.querySelector('.tm-grid__header');
      if (header !== null) {
        observer.observe(header);
      }
      onCleanup(() => observer.disconnect());
    });
    // The row-end column arriving or leaving re-measures its hairline.
    afterRenderEffect(() => {
      this.core().rowEnd();
      this.core().columnModel();
      untracked(() => this.measureEndOverlap());
    });
    // A live direction flip re-lays the grid, but an already-open overlay
    // keeps the inline offset the CDK measured against the OLD side, so the
    // cell's error bubble stays on the edge it opened against.
    const directionChange = this.directionality.change.subscribe(() =>
      this.errorAnchored.reanchor(),
    );
    inject(DestroyRef).onDestroy(() => directionChange.unsubscribe());
  }
}
