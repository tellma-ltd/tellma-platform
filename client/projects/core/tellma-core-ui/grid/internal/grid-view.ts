// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  untracked,
  ViewContainerRef,
  viewChild,
} from '@angular/core';
import { OverlayModule } from '@angular/cdk/overlay';
import type { ConnectedPosition, FlexibleOverlayPopoverLocation } from '@angular/cdk/overlay';

import { TmMenu } from '@tellma/core-ui/menu';
import { TmSpinner } from '@tellma/core-ui/spinner';

import { ɵTmGridFindBar } from './find-bar';
import type { ɵTmGridViewCore } from './grid-core';
import { ɵTmGridIcons } from './icons';
import { ɵTmGridStatusBar } from './status-bar';
import { ɵTmGridTouchHandles } from './touch-handles';

/**
 * The grid's one and only template: scroller, sticky header, virtualized
 * row window, the row-checkbox chrome column, the loading/empty overlays,
 * the editing-cell editor outlet, the editable-mode status bar, the
 * context menu, the find bar, the coarse-pointer selection handles, and
 * the active-cell error overlay. `tm-grid` and `tm-tree-grid` are thin
 * shells around this component so the large template compiles exactly
 * once. All state and behavior live in the `core` it renders from; the
 * template only binds signals and routes DOM events back into it.
 */
@Component({
  selector: 'tm-grid-view',
  imports: [
    NgTemplateOutlet,
    OverlayModule,
    TmMenu,
    TmSpinner,
    ɵTmGridFindBar,
    ɵTmGridIcons,
    ɵTmGridStatusBar,
    ɵTmGridTouchHandles,
  ],
  template: `
    <div
      #scroller
      class="tm-grid__scroller"
      [attr.role]="core().gridRole()"
      aria-multiselectable="true"
      [class.tm-grid__scroller--readonly]="!core().editable()"
      [attr.aria-rowcount]="core().ariaRowCount()"
      [attr.aria-colcount]="core().ariaColCount()"
      [attr.aria-busy]="core().loading() ? 'true' : null"
      [tabindex]="core().containerTabindex()"
      (keydown)="core().onKeydown($event)"
      (pointerdown)="core().onPointerDown($event)"
      (click)="core().onClick($event)"
      (dblclick)="core().onDblClick($event)"
      (contextmenu)="core().onContextMenu($event)"
      (focusout)="core().onFocusOut($event)"
      (copy)="core().onCopy($event)"
      (cut)="core().onCut($event)"
      (paste)="core().onPaste($event)"
      (scroll)="core().onScroll($event)"
    >
      <div class="tm-grid__header" role="row" aria-rowindex="1">
        <div class="tm-grid__corner" role="columnheader" aria-colindex="1" data-tm-corner></div>
        @if (core().checkboxColumn()) {
          <div class="tm-grid__checkhdr" role="columnheader" aria-colindex="2" data-tm-checkhdr>
            <div
              class="tm-grid__check"
              role="checkbox"
              tabindex="-1"
              data-tm-checkall
              [class.tm-grid__check--on]="core().checkAllState() === 'all'"
              [class.tm-grid__check--mixed]="core().checkAllState() === 'mixed'"
              [attr.aria-checked]="checkAllAria()"
              [attr.aria-label]="core().selectAllLabel()"
            ></div>
          </div>
        }
        @for (col of core().columnModel(); track col.id) {
          <div
            class="tm-grid__colhdr"
            role="columnheader"
            data-tm-colhdr
            [attr.data-col]="col.index"
            [attr.aria-colindex]="col.ariaColIndex"
            [class.tm-grid__colhdr--hit]="core().hitCols()[col.index]"
            [style.text-align]="col.align"
          >
            @if (col.headerTemplate; as headerTemplate) {
              <ng-container
                [ngTemplateOutlet]="headerTemplate"
                [ngTemplateOutletContext]="{ $implicit: col.header() }"
              />
            } @else {
              <span class="tm-grid__colhdr-label">{{ col.header() }}</span>
            }
            <div class="tm-grid__resize" data-tm-resize (pointerdown)="core().resize.start(col, $event)"></div>
          </div>
        }
      </div>

      <div class="tm-grid__spacer" role="presentation" [style.block-size.px]="core().totalHeight()">
        <div class="tm-grid__window" role="rowgroup" [style.transform]="core().windowTransform()">
          @for (row of core().renderRows(); track row.rowKey) {
            <div
              class="tm-grid__row"
              role="row"
              [attr.aria-rowindex]="row.ariaRowIndex"
              [attr.aria-level]="row.ariaLevel"
              [attr.aria-expanded]="row.ariaExpanded"
              [attr.aria-posinset]="row.ariaPosInSet"
              [attr.aria-setsize]="row.ariaSetSize"
              [attr.aria-selected]="row.checked ? 'true' : null"
              [class.tm-grid__row--zebra]="row.zebra"
              [class.tm-grid__row--checked]="row.checked"
              [class.tm-grid__row--placeholder]="row.isPlaceholder"
              [class.tm-grid__row--outlier]="row.outlier"
              [style.transform]="row.outlierTransform"
            >
              <div
                class="tm-grid__rowhdr"
                role="rowheader"
                aria-colindex="1"
                data-tm-rowhdr
                [attr.data-row]="row.viewIndex"
                [attr.aria-label]="row.isPlaceholder ? core().newRowLabel() : null"
                [class.tm-grid__rowhdr--hit]="row.headerHit"
              >
                {{ row.rowHeaderText }}
              </div>
              @if (core().checkboxColumn()) {
                <div
                  class="tm-grid__checkcell"
                  role="gridcell"
                  aria-colindex="2"
                  data-tm-checkcell
                  [attr.data-row]="row.viewIndex"
                >
                  <!-- The placeholder row has no checkbox; its chrome cell
                       stays to keep the grid tracks aligned. -->
                  @if (!row.isPlaceholder) {
                    <div
                      class="tm-grid__check"
                      role="checkbox"
                      tabindex="-1"
                      [class.tm-grid__check--on]="row.checked"
                      [attr.aria-checked]="row.checked ? 'true' : 'false'"
                      [attr.aria-label]="core().selectRowLabel()"
                    ></div>
                  }
                </div>
              }
              @for (cell of row.cells; track cell.colIndex) {
                <div
                  class="tm-grid__cell"
                  role="gridcell"
                  data-tm-cell
                  [attr.data-row]="row.viewIndex"
                  [attr.data-col]="cell.colIndex"
                  [attr.aria-colindex]="cell.ariaColIndex"
                  [attr.aria-selected]="cell.selected ? 'true' : null"
                  [attr.aria-invalid]="cell.invalid ? 'true' : null"
                  [attr.aria-readonly]="cell.readonly ? 'true' : null"
                  [attr.aria-describedby]="cell.active && cell.invalid && !cell.editing ? core().errorMsgId : null"
                  [tabindex]="cell.active && !core().escaped() ? 0 : -1"
                  [class.tm-grid__cell--active]="cell.active"
                  [class.tm-grid__cell--selected]="cell.selected"
                  [class.tm-grid__cell--error]="cell.invalid"
                  [class.tm-grid__cell--readonly]="cell.readonly"
                  [class.tm-grid__cell--editing]="cell.editing"
                  [class.tm-grid__cell--cut]="cell.inCutRange"
                  [class.tm-grid__cell--find]="cell.findMatch"
                  [class.tm-grid__cell--find-active]="cell.activeFindMatch"
                  [style.text-align]="cell.align"
                >
                  @if (cell.hierarchy) {
                    <!-- Tree affordance: level indent, the pointer-only
                         expander, and a RESERVED lazy-loading spinner slot,
                         so the spinner appearing shifts nothing. -->
                    <span
                      class="tm-grid__indent"
                      aria-hidden="true"
                      [style.--grid-level]="cell.level"
                    ></span>
                    <span class="tm-grid__twisty" aria-hidden="true">
                      @if (cell.expander !== null) {
                        <button
                          type="button"
                          class="tm-grid__expander"
                          data-tm-expander
                          tabindex="-1"
                          [class.tm-grid__expander--open]="cell.expander === 'expanded'"
                        ></button>
                      }
                    </span>
                    <span class="tm-grid__childspin" aria-hidden="true">
                      @if (cell.loadingChildren) {
                        <tm-spinner class="tm-grid__childspin-spinner" data-tm-childspin />
                      }
                    </span>
                  }
                  @if (cell.editing) {
                    <div class="tm-grid__editor" data-tm-editor>
                      <ng-container #editorOutlet />
                    </div>
                  } @else if (cell.displayTemplate; as displayTemplate) {
                    <ng-container
                      [ngTemplateOutlet]="displayTemplate"
                      [ngTemplateOutletContext]="cell.displayCtx ?? null"
                    />
                  } @else if (cell.glyphClass; as glyphClass) {
                    <span [class]="glyphClass" aria-hidden="true"></span>
                    <span class="tm-visually-hidden">{{ cell.text }}</span>
                  } @else {
                    <span class="tm-grid__text">{{ cell.text }}</span>
                  }
                  @if (cell.pending) {
                    <tm-spinner class="tm-grid__cell-spin" />
                  }
                </div>
              }
            </div>
          }
        </div>
        @if (core().coarsePointer()) {
          <!-- Range-selection drag handles (coarse pointers): inside the
               spacer so their measured positions scroll with the rows. -->
          <tm-grid-touch-handles [core]="core()" />
        }
      </div>
    </div>

    <!-- Loading / empty overlay: a sibling of the scroller (not inside its
         scrolling flow) so it covers the visible viewport regardless of the
         scroll position — inside the scroller it would flow after the
         full-height row spacer and render off-screen while rows are bound. -->
    @if (core().loading()) {
      <div class="tm-grid__overlay" data-tm-loading>
        @if (core().loadingDef(); as loadingDef) {
          <ng-container [ngTemplateOutlet]="loadingDef.template" />
        } @else {
          <tm-spinner class="tm-grid__overlay-spinner" />
          <span class="tm-grid__overlay-text">{{ core().loadingText() }}</span>
        }
      </div>
    } @else if (core().showEmpty()) {
      <div class="tm-grid__overlay" data-tm-empty>
        @if (core().emptyDef(); as emptyDef) {
          <ng-container [ngTemplateOutlet]="emptyDef.template" />
        } @else {
          <span class="tm-grid__overlay-text">{{ core().emptyText() }}</span>
        }
      </div>
    }

    @if (core().findOpen()) {
      <!-- Outside the scroller: the bar's keys never reach the grid's
           delegated handlers, and the roving-focus effect leaves focus
           alone while it rests here. -->
      <tm-grid-find-bar [core]="core()" />
    }

    @if (core().editable()) {
      <tm-grid-status-bar class="tm-grid__status" [core]="core()" />
    } @else {
      <!-- Readonly grids have no status bar; the transient clipboard-failure
           notice overlays the grid's block-end edge instead (zero layout shift). -->
      @if (core().transientNotice(); as notice) {
        <div class="tm-grid__notice" data-tm-notice>{{ notice }}</div>
      }
    }

    <tm-menu [items]="core().menuItems()" />
    <tm-grid-icons />

    <!-- Active-cell error message: a top-layer overlay so errors appearing
         or clearing never shift the grid's (or the page's) layout. The
         popover host attaches to THIS component's element, not inline at
         the origin: inline insertion would place it inside the role="row"
         element, where a tooltip is not an allowed child (axe
         aria-required-children); the view host keeps token/direction
         inheritance and the top layer positions it all the same. -->
    <ng-template
      [cdkConnectedOverlay]="{
        origin: core().errorAnchor()!,
        usePopover: errorPopoverLocation,
        disableClose: true,
        positions: errorPositions,
      }"
      [cdkConnectedOverlayOpen]="core().errorAnchor() !== null"
    >
      <div class="tm-grid__error-msg" [id]="core().errorMsgId" role="tooltip">
        {{ core().errorMessage() }}
      </div>
    </ng-template>
  `,
  styleUrl: './grid-view.css',
  host: { class: 'tm-grid-view' },
})
export class ɵTmGridView {
  /** The composition root this view renders from (built by the grid shell). */
  readonly core = input.required<ɵTmGridViewCore>();

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

  /** The error popover's DOM home: this host, outside any role="row". */
  protected readonly errorPopoverLocation: FlexibleOverlayPopoverLocation = {
    type: 'parent',
    element: inject(ElementRef).nativeElement as Element,
  };

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly editorOutlet = viewChild('editorOutlet', { read: ViewContainerRef });
  private readonly menu = viewChild(TmMenu);
  private readonly icons = viewChild(ɵTmGridIcons);

  constructor() {
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
      const icons = this.icons();
      untracked(() => core.attachMenu(menu ?? null, icons?.templates() ?? null));
    });
  }
}
