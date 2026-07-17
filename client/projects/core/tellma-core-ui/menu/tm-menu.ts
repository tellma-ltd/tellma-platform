// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  DestroyRef,
  inject,
  input,
  output,
  signal,
  type Signal,
  type TemplateRef,
  untracked,
  viewChild,
} from '@angular/core';
import { Menu, MenuItem } from '@angular/aria/menu';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import type { ConnectedPosition, FlexibleConnectedPositionStrategyOrigin } from '@angular/cdk/overlay';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';

/** One actionable menu item. */
export interface TmMenuItem {
  /** Stable identity (also the activation key). */
  readonly id: string;
  /** Literal label. Provide exactly one of `label`/`labelKey`. */
  readonly label?: string;
  /** Label resolved through the UI translation seam. */
  readonly labelKey?: string;
  /** Leading inline-SVG icon template (rendered `aria-hidden`). */
  readonly icon?: TemplateRef<void>;
  /** Whether the item is disabled (kept navigable, never activates). */
  readonly disabled?: boolean;
  /** Runs on activation (click, Enter, Space), before the menu closes. */
  action(): void;
}

/** A visual separator between item groups. */
export interface TmMenuSeparator {
  /** Discriminates a separator entry. */
  readonly separator: true;
}

/** One entry of a menu: an item or a separator. */
export type TmMenuEntry = TmMenuItem | TmMenuSeparator;

/** Where a menu opens: an element, a rectangle, or a point (right-click). */
export type TmMenuAnchor = Element | DOMRect | { readonly x: number; readonly y: number };

/** Options of {@link TmMenu.open}. */
export interface TmMenuOpenOptions {
  /** The element focus returns to when the menu closes via keyboard. */
  readonly restoreFocus?: HTMLElement;
}

/**
 * A general-purpose menu: flat items + separators with icon and disabled
 * support, opened programmatically at an element, rectangle, or pointer
 * position (see `TmContextMenuTrigger` for the context-menu wiring). The
 * `@angular/aria` menu directives own the roles, roving focus, arrow/Home/
 * End navigation, and typeahead; `tm-menu` owns the overlay, open/close
 * semantics (Esc, Tab, outside click), focus restore, and label resolution.
 *
 * @tmGroup overlay
 * @tmA11yNotes role="menu"/"menuitem" with roving tabindex from the aria
 *   pattern; Esc closes and restores focus to the invoking element; Tab
 *   closes and lets focus continue past the invoker; disabled items stay
 *   navigable but never activate.
 */
@Component({
  selector: 'tm-menu',
  imports: [Menu, MenuItem, NgTemplateOutlet, OverlayModule],
  template: `
    <ng-template
      [cdkConnectedOverlay]="{
        origin: overlayOrigin()!,
        usePopover: 'inline',
        disableClose: true,
        positions: positions,
      }"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="onOverlayAttach()"
      (detach)="onOverlayDetach()"
      (overlayOutsideClick)="close({ restoreFocus: false })"
    >
      <div class="tm-menu__panel" (keydown)="onPanelKeydown($event)">
        <div
          ngMenu
          #m="ngMenu"
          class="tm-menu__list"
          [attr.aria-label]="ariaLabel()"
          (itemSelected)="onItemSelected($event)"
        >
          @for (entry of items(); track $index) {
            @if (isSeparator(entry)) {
              <div class="tm-menu__separator" role="separator"></div>
            } @else {
              <div
                ngMenuItem
                class="tm-menu__item"
                [value]="entry.id"
                [disabled]="entry.disabled ?? false"
                [searchTerm]="labelOf(entry)()"
              >
                <span class="tm-menu__icon" aria-hidden="true">
                  @if (entry.icon) {
                    <ng-container [ngTemplateOutlet]="entry.icon" />
                  }
                </span>
                <span class="tm-menu__label">{{ labelOf(entry)() }}</span>
              </div>
            }
          }
        </div>
      </div>
    </ng-template>
  `,
  styleUrl: './tm-menu.css',
  host: {
    class: 'tm-menu',
    // The accessible name lives on the role="menu" list; strip it from the
    // role-less host so a static aria-label can't violate ARIA.
    '[attr.aria-label]': 'null',
  },
})
export class TmMenu {
  private readonly translate = inject(TM_UI_TRANSLATE);

  /** The menu's entries, in display order. */
  readonly items = input.required<readonly TmMenuEntry[]>();
  /** Accessible name of the menu (falls back to the invoker's context). */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });

  /** Emits when the menu opens. */
  readonly opened = output<void>();
  /** Emits when the menu closes. */
  readonly closed = output<void>();
  /** Emits the activated item (its `action()` has already run). */
  readonly itemSelected = output<TmMenuItem>();

  /** Whether the menu is open. */
  readonly isOpen: Signal<boolean>;

  /** Whether the overlay is attached. */
  protected readonly expanded = signal(false);
  /** Where the overlay anchors (element, rect, or point). */
  protected readonly overlayOrigin = signal<FlexibleConnectedPositionStrategyOrigin | null>(null);

  /** Standard context-menu placement: below-start first, then flips. */
  protected readonly positions: ConnectedPosition[] = [
    { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
    { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
    { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top' },
    { originX: 'end', originY: 'top', overlayX: 'end', overlayY: 'bottom' },
  ];

  private readonly overlay = viewChild(CdkConnectedOverlay);
  private readonly menu = viewChild(Menu);
  private restoreFocusTarget: HTMLElement | null = null;
  private pendingRemeasure: ReturnType<typeof setTimeout> | undefined;
  private focusedOnOpen = false;
  /** Per-item label signals, cached by item reference. */
  private readonly labelCache = new WeakMap<TmMenuItem, Signal<string>>();

  /**
   * Capture-phase Escape interception while open: the aria menu registers
   * its own Escape handler (a no-op for a parentless menu) that consumes
   * the event before it could bubble to the panel, so the close key must
   * be caught ahead of it.
   */
  private readonly onDocumentKeydownCapture = (event: KeyboardEvent): void => {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation();
      this.close();
    }
  };

  constructor() {
    this.isOpen = this.expanded.asReadonly();
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.pendingRemeasure);
      document.removeEventListener('keydown', this.onDocumentKeydownCapture, true);
    });

    // Focus the menu once its items have rendered (the overlay content
    // lands one render pass after attach); aria's focusin handler then
    // activates the first item. Guarded so re-renders don't steal focus.
    afterRenderEffect(() => {
      const menu = this.menu();
      if (menu === undefined || !this.expanded() || menu._items().length === 0) {
        return;
      }
      if (!this.focusedOnOpen) {
        this.focusedOnOpen = true;
        untracked(() => menu.element.focus());
      }
    });
  }

  /**
   * Opens the menu at an element, rectangle, or pointer position. While
   * already open, the menu re-anchors to the new position.
   */
  open(anchor: TmMenuAnchor, options?: TmMenuOpenOptions): void {
    this.restoreFocusTarget = options?.restoreFocus ?? null;
    this.overlayOrigin.set(toOverlayOrigin(anchor));
    this.focusedOnOpen = false;
    if (untracked(this.expanded)) {
      // Re-anchoring while open: the origin signal changed; re-measure.
      this.onOverlayAttach();
      return;
    }
    document.addEventListener('keydown', this.onDocumentKeydownCapture, true);
    this.expanded.set(true);
  }

  /** Closes the menu; `restoreFocus: false` skips returning focus (outside clicks). */
  close(options?: { restoreFocus?: boolean }): void {
    if (!untracked(this.expanded)) {
      return;
    }
    document.removeEventListener('keydown', this.onDocumentKeydownCapture, true);
    this.expanded.set(false);
    if (options?.restoreFocus !== false) {
      this.restoreFocusTarget?.focus();
    }
    this.restoreFocusTarget = null;
  }

  // ---- internals ----

  /** Discriminates separator entries in the template. */
  protected isSeparator(entry: TmMenuEntry): entry is TmMenuSeparator {
    return 'separator' in entry && entry.separator === true;
  }

  /** The entry's label signal: literal, or resolved through the i18n seam. */
  protected labelOf(item: TmMenuItem): Signal<string> {
    let label = this.labelCache.get(item);
    if (label === undefined) {
      label =
        item.labelKey !== undefined
          ? this.translate(item.labelKey)
          : computed(() => item.label ?? '');
      this.labelCache.set(item, label);
    }
    return label;
  }

  /** Runs the activated item's action, reports it, and closes. */
  protected onItemSelected(id: string): void {
    const item = this.items().find(
      (entry): entry is TmMenuItem => !this.isSeparator(entry) && entry.id === id,
    );
    if (item === undefined || item.disabled === true) {
      return;
    }
    item.action();
    this.itemSelected.emit(item);
    this.close();
  }

  /**
   * Tab closes and restores focus WITHOUT consuming the key, so focus
   * continues past the invoking element (Escape is intercepted at the
   * document capture phase instead — the aria menu consumes it before it
   * could bubble here).
   */
  protected onPanelKeydown(event: KeyboardEvent): void {
    if (event.key === 'Tab') {
      this.close();
    }
  }

  /** Re-measures the overlay one macrotask after attach so flipping can work. */
  protected onOverlayAttach(): void {
    this.opened.emit();
    clearTimeout(this.pendingRemeasure);
    this.pendingRemeasure = setTimeout(() => this.overlay()?.overlayRef?.updatePosition());
  }

  /** Keeps `expanded` honest when the overlay detaches out-of-band. */
  protected onOverlayDetach(): void {
    if (untracked(this.expanded)) {
      document.removeEventListener('keydown', this.onDocumentKeydownCapture, true);
      this.expanded.set(false);
    }
    this.closed.emit();
  }
}

/** Maps the public anchor union onto the CDK origin union. */
function toOverlayOrigin(anchor: TmMenuAnchor): FlexibleConnectedPositionStrategyOrigin {
  if (anchor instanceof Element) {
    return anchor;
  }
  if (anchor instanceof DOMRect) {
    return { x: anchor.x, y: anchor.y, width: anchor.width, height: anchor.height };
  }
  return { x: anchor.x, y: anchor.y };
}
