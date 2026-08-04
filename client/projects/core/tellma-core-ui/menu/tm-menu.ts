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
  isDevMode,
  output,
  signal,
  type Signal,
  type TemplateRef,
  untracked,
  viewChild,
} from '@angular/core';
import { Menu, MenuItem } from '@angular/aria/menu';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import type { ConnectedPosition } from '@angular/cdk/overlay';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { tmCreateAnchoredOverlay, tmPushEscapeDismissal } from '@tellma/core-ui/private';

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
  /**
   * Keyboard-shortcut hint shown trailing the label in the muted hint color
   * (e.g. `⌘C` / `Ctrl+C`). Display only — the caller pre-formats it for the
   * platform; it never binds the accelerator.
   */
  readonly shortcut?: string;
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
 * Open menus, most-recently-opened last — the front-most gate for the
 * outside-POINTERDOWN dismissal (Escape rides the shared cross-component
 * dismissal stack in `@tellma/core-ui/private` instead, so a tooltip
 * showing over an open menu never makes one Escape close both).
 */
const openMenuStack: TmMenu[] = [];

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
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="anchored.handleAttach()"
      (detach)="anchored.handleDetach()"
      (overlayOutsideClick)="anchored.handleOutsideClick($event)"
    >
      <div
        class="tm-menu__panel"
        (keydown)="onPanelKeydown($event)"
        (contextmenu)="$event.preventDefault()"
      >
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
                @if (entry.shortcut; as shortcut) {
                  <span class="tm-menu__shortcut" aria-hidden="true">{{ shortcut }}</span>
                }
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
  protected readonly overlayOrigin = signal<TmMenuAnchor | null>(null);

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
  private focusedOnOpen = false;

  /**
   * The shared anchored-overlay wiring. The re-measure runs after the NEXT
   * render so flexible positioning re-flips at the new anchor: the origin
   * reaches the CDK overlay directive only when change detection flushes
   * the config binding, so measuring before that (a bare timer racing CD)
   * would re-measure against the OLD origin — intermittently, on slower
   * machines.
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.overlayOrigin(),
    positions: this.positions,
    remeasure: 'afterNextRender',
    onAttach: () => this.opened.emit(),
    onDetach: () => this.onOverlayDetach(),
    onOutsideClick: () => this.close({ restoreFocus: false }),
  });
  /** Per-item label signals, cached by item reference. */
  private readonly labelCache = new WeakMap<TmMenuItem, Signal<string>>();

  /**
   * The shared Escape-dismissal registration while open (document capture
   * — the aria menu registers its own Escape handler, a no-op for a
   * parentless menu, that consumes the event before it could bubble to
   * the panel, so the close key must be caught ahead of it). The SHARED
   * stack coordinates with the other capture-phase dismissers (tooltips):
   * one Escape, one layer.
   */
  private releaseEscape: (() => void) | null = null;

  /**
   * Dismiss on an outside POINTERDOWN, not the trailing click: Excel/Sheets
   * (and native menus) drop the menu the instant you press elsewhere, so a
   * range drag begins on a clean surface instead of dragging for seconds with
   * the menu still up. A press inside the panel is left alone so an item
   * activates on its click. The event is NOT consumed — the same press still
   * reaches its target (e.g. the grid's drag start). Front-most menu only.
   */
  private readonly onDocumentPointerDownCapture = (event: PointerEvent): void => {
    if (openMenuStack[openMenuStack.length - 1] !== this) {
      return;
    }
    const target = event.target;
    const panel = this.overlay()?.overlayRef?.overlayElement ?? null;
    if (target instanceof Node && panel !== null && panel.contains(target)) {
      return;
    }
    // A press on a MODAL SCRIM must dismiss only the menu: the press's
    // un-consumed trailing click would also fire the modal's backdrop
    // dismissal — one gesture, two layers. Swallow that single click at
    // document capture (backdrop targets only, one-shot, time-boxed).
    if (target instanceof Element && target.classList.contains('cdk-overlay-backdrop')) {
      // The trailing click arrives on RELEASE, which the user can defer
      // indefinitely — so the disarm window opens at pointerup (or
      // pointercancel, which produces no click at all), never at press.
      let timer: ReturnType<typeof setTimeout> | undefined;
      const disarm = (): void => {
        clearTimeout(timer);
        document.removeEventListener('click', swallow, true);
        document.removeEventListener('pointerup', armDisarmWindow, true);
        document.removeEventListener('pointercancel', disarm, true);
      };
      const swallow = (click: Event): void => {
        if (click.target === target) {
          click.preventDefault();
          click.stopPropagation();
        }
        disarm();
      };
      const armDisarmWindow = (): void => {
        clearTimeout(timer);
        timer = setTimeout(disarm, 600); // the trailing click never came
      };
      document.addEventListener('click', swallow, true);
      document.addEventListener('pointerup', armDisarmWindow, true);
      document.addEventListener('pointercancel', disarm, true);
    }
    this.close({ restoreFocus: false });
  };

  constructor() {
    this.isOpen = this.expanded.asReadonly();
    inject(DestroyRef).onDestroy(() => {
      this.disarmDismissal();
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
   * already open, the menu re-anchors to the new position. Ignored (with a
   * dev-mode warning) when there is nothing actionable to show.
   */
  open(anchor: TmMenuAnchor, options?: TmMenuOpenOptions): void {
    if (!this.hasActionableItems()) {
      if (isDevMode()) {
        console.warn('tm-menu: open() ignored — the menu has no actionable items.');
      }
      return;
    }
    this.restoreFocusTarget = options?.restoreFocus ?? null;
    this.overlayOrigin.set(anchor);
    this.focusedOnOpen = false;
    this.armDismissal();

    // Re-anchor rather than (re)open whenever the overlay is still live: a
    // genuine open-while-open, or a same-tick close+reopen where an outside-
    // click dispatcher caught the reinvoking right-click at the capture phase
    // and flipped `expanded` false before it bubbled to the trigger. With no
    // change detection between the two, the CDK `open` binding never toggles,
    // so the overlay neither re-attaches nor re-applies its object-form origin
    // — drive the re-measure explicitly.
    if (untracked(this.expanded) || (this.overlay()?.overlayRef?.hasAttached() ?? false)) {
      this.expanded.set(true);
      this.anchored.reanchor();
      return;
    }
    this.expanded.set(true);
  }

  /** Closes the menu; `restoreFocus: false` skips returning focus (outside clicks). */
  close(options?: { restoreFocus?: boolean }): void {
    if (!untracked(this.expanded)) {
      return;
    }
    this.disarmDismissal();
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

  /** Keeps `expanded` honest when the overlay detaches out-of-band. */
  private onOverlayDetach(): void {
    if (untracked(this.expanded)) {
      this.disarmDismissal();
      this.expanded.set(false);
    }
    this.closed.emit();
  }

  /** Whether the menu has at least one non-separator entry to show. */
  private hasActionableItems(): boolean {
    return untracked(this.items).some((entry) => !this.isSeparator(entry));
  }

  /**
   * Registers this menu as the front-most dismissal target: the Escape and
   * outside-pointerdown handlers listen at the document capture phase and the
   * menu joins the open-menu stack. Idempotent, so a re-anchor never
   * double-registers (the listeners de-dupe by reference).
   */
  private armDismissal(): void {
    this.releaseEscape ??= tmPushEscapeDismissal(() => this.close());
    document.addEventListener('pointerdown', this.onDocumentPointerDownCapture, true);
    if (!openMenuStack.includes(this)) {
      openMenuStack.push(this);
    }
  }

  /** Stops the document dismissal handlers and leaves the open-menu stack. */
  private disarmDismissal(): void {
    this.releaseEscape?.();
    this.releaseEscape = null;
    document.removeEventListener('pointerdown', this.onDocumentPointerDownCapture, true);
    const index = openMenuStack.indexOf(this);
    if (index !== -1) {
      openMenuStack.splice(index, 1);
    }
  }
}
