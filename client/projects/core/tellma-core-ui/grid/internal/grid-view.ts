// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  ElementRef,
  input,
  untracked,
  ViewContainerRef,
  viewChild,
} from '@angular/core';
import { OverlayModule } from '@angular/cdk/overlay';
import type { ConnectedPosition } from '@angular/cdk/overlay';

import { TmMenu } from '@tellma/core-ui/menu';
import { TmSpinner } from '@tellma/core-ui/spinner';

import type { ɵTmGridViewCore } from './grid-core';
import { ɵTmGridIcons } from './icons';
import { ɵTmGridStatusBar } from './status-bar';

/**
 * The grid's one and only template: scroller, sticky header, virtualized
 * row window, the loading/empty overlays, the editing-cell editor outlet,
 * the editable-mode status bar, the context menu, and the active-cell
 * error overlay. `tm-grid` (and, later, `tm-tree-grid`) are thin shells
 * around this component so the large template compiles exactly once. All
 * state and behavior live in the `core` it renders from; the template only
 * binds signals and routes DOM events back into it.
 */
@Component({
  selector: 'tm-grid-view',
  imports: [NgTemplateOutlet, OverlayModule, TmMenu, TmSpinner, ɵTmGridIcons, ɵTmGridStatusBar],
  template: `
    <div
      #scroller
      class="tm-grid__scroller"
      role="grid"
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
              [class.tm-grid__row--zebra]="row.zebra"
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
                [class.tm-grid__rowhdr--hit]="row.headerHit"
              >
                {{ row.rowHeaderText }}
              </div>
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
                  [attr.aria-describedby]="cell.active && cell.invalid ? core().errorMsgId : null"
                  [tabindex]="cell.active && !core().escaped() ? 0 : -1"
                  [class.tm-grid__cell--active]="cell.active"
                  [class.tm-grid__cell--selected]="cell.selected"
                  [class.tm-grid__cell--error]="cell.invalid"
                  [class.tm-grid__cell--readonly]="cell.readonly"
                  [class.tm-grid__cell--editing]="cell.editing"
                  [class.tm-grid__cell--cut]="cell.inCutRange"
                  [style.text-align]="cell.align"
                >
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
      </div>

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
    </div>

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
         or clearing never shift the grid's (or the page's) layout. -->
    <ng-template
      [cdkConnectedOverlay]="{
        origin: core().errorAnchor()!,
        usePopover: 'inline',
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

  /** Error-overlay placement: below the cell, flipping above. */
  protected readonly errorPositions: ConnectedPosition[] = [
    { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
    { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
  ];

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
