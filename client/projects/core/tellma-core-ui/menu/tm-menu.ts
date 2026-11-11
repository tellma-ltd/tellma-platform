// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  contentChild,
  DestroyRef,
  Directive,
  inject,
  input,
  isDevMode,
  output,
  signal,
  type Signal,
  TemplateRef,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { Menu } from '@angular/aria/menu';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import type { ConnectedPosition } from '@angular/cdk/overlay';

import { tmCreateAnchoredOverlay, tmPushEscapeDismissal } from '@tellma/core-ui/overlay';

import {
  ɵtmIsMenuGroup,
  ɵtmIsMenuLink,
  ɵtmIsMenuSeparator,
  ɵTmMenuHost,
  ɵTmMenuList,
  type ɵTmMenuRow,
  ɵTmMenuRows,
} from './internal/tm-menu-rows';
import type {
  TmMenuAnchor,
  TmMenuEntry,
  TmMenuItem,
  TmMenuLink,
  TmMenuOpenOptions,
} from './tm-menu-entries';

/**
 * A block shown above the menu's rows, such as who is signed in at the top
 * of a session menu. It sits outside the list of rows and is read as the
 * menu's description when the menu opens.
 *
 * ```html
 * <tm-menu #menu [items]="items()">
 *   <ng-template tmMenuHeader>Signed in as {{ userName() }}</ng-template>
 * </tm-menu>
 * ```
 *
 * The block is for text and images only. Focus moves through the rows and
 * never enters it, so a button or a link placed here could not be reached
 * from the keyboard; in development the menu warns when it finds one.
 *
 * @tmGroup overlay
 * @tmA11yNotes Rendered outside role="menu" and referenced by the menu's
 *   aria-describedby, so it is announced once as the menu opens.
 */
@Directive({ selector: 'ng-template[tmMenuHeader]' })
export class TmMenuHeaderDef {
  /** The header template. */
  readonly template = inject<TemplateRef<void>>(TemplateRef);
}

/** Counts menus, so each one's list and header ids are unique in the document. */
let nextMenuId = 0;

/**
 * Open menus, most-recently-opened last — the front-most gate for the
 * outside-POINTERDOWN dismissal (Escape rides the shared cross-component
 * dismissal stack in `@tellma/core-ui/overlay` instead, so a tooltip
 * showing over an open menu never makes one Escape close both).
 */
const openMenuStack: TmMenu[] = [];

/**
 * A general-purpose, data-driven menu. Its `items` are plain objects:
 * items (optionally checkable, or disabled with a reason), links, submenus
 * one level deep, labelled groups and separators. It opens from a
 * `button[tmMenuTriggerFor]`, as a context menu (`tmContextMenuTrigger`),
 * or programmatically at an element, rectangle, or pointer position.
 *
 * The `@angular/aria` menu directives own the roles, roving focus, arrow/
 * Home/End navigation, submenu keys and typeahead; `tm-menu` owns the
 * overlays, open/close semantics (Esc, Tab, outside press), focus restore,
 * and label resolution.
 *
 * Rows are 30px tall, and 44px on viewports narrower than 576px, where
 * they are pressed with a finger.
 *
 * @tmGroup overlay
 * @tmA11yNotes role="menu" with menuitem, menuitemcheckbox and
 *   menuitemradio rows on a roving tabindex; focus lands on the first row
 *   (or the last, on request) as the menu opens. Esc closes one level per
 *   press and restores focus to the invoking element; Tab closes and lets
 *   focus continue past the invoker. Disabled rows stay navigable but never
 *   activate, and a row disabled with a reason exposes that reason as its
 *   description. Links are real anchors. Typeahead matches labels only;
 *   shortcut hints are hidden from assistive technology.
 */
@Component({
  selector: 'tm-menu',
  imports: [Menu, NgTemplateOutlet, OverlayModule, ɵTmMenuList, ɵTmMenuRows],
  // Section notes (kept out of the markup, which ships in the bundle):
  // - The panel is the overlay surface. It holds the optional header block,
  //   then the aria menu (the list) whose rows ɵTmMenuRows renders.
  // - Submenu overlays are inserted next to their parent rows, so their
  //   events bubble through this panel too.
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
        (contextmenu)="onPanelContextMenu($event)"
      >
        @if (header(); as header) {
          <div class="tm-menu__header" [id]="headerId">
            <ng-container [ngTemplateOutlet]="header.template" />
          </div>
        }
        <div
          ngMenu
          tmMenuList
          class="tm-menu__list"
          [id]="listId"
          [attr.aria-label]="ariaLabel()"
          [attr.aria-describedby]="header() ? headerId : null"
          (itemSelected)="onItemSelected($event)"
        >
          <tm-menu-rows [entries]="items()" />
        </div>
      </div>
    </ng-template>
  `,
  styleUrl: './tm-menu.css',
  // The rows and the submenu panels are rendered by internal components, so
  // the one stylesheet must reach markup this component did not stamp; every
  // selector in it is tm-menu scoped.
  encapsulation: ViewEncapsulation.None,
  // Rows on every level hand their activations to this menu.
  providers: [
    {
      provide: ɵTmMenuHost,
      useFactory: (): ɵTmMenuHost => {
        const menu = inject(TmMenu);
        return { activate: (entry) => menu.ɵactivate(entry) };
      },
    },
  ],
  host: {
    class: 'tm-menu',
    // The accessible name lives on the role="menu" list; strip it from the
    // role-less host so a static aria-label can't violate ARIA.
    '[attr.aria-label]': 'null',
  },
})
export class TmMenu {
  /** The menu's entries, in display order. */
  readonly items = input.required<readonly TmMenuEntry[]>();
  /** Accessible name of the menu (falls back to the invoker's context). */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });

  /** Emits when the menu opens. */
  readonly opened = output<void>();
  /** Emits when the menu closes. */
  readonly closed = output<void>();
  /**
   * Emits the activated item (its `action()` has already run). Links and
   * submenu parents never emit.
   */
  readonly itemSelected = output<TmMenuItem>();

  /** Whether the menu is open. */
  readonly isOpen: Signal<boolean>;

  /** The id of the element that carries `role="menu"`. */
  protected readonly listId = `tm-menu-${nextMenuId++}`;
  /** The id of the header block, which describes the menu. */
  protected readonly headerId = `${this.listId}-header`;
  /** The projected header block, if any. */
  protected readonly header = contentChild(TmMenuHeaderDef);

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
  private readonly menu = viewChild<Menu<ɵTmMenuRow>>(Menu);
  /** The element that invoked the open menu and takes focus back from it. */
  private readonly invoker = signal<HTMLElement | null>(null);
  /** The row that should take focus once the open menu has rendered. */
  private readonly pendingFocus = signal<'first' | 'last' | null>(null);
  /** Trigger buttons: a press on one is the trigger's to handle, not an outside press. */
  private readonly triggers = new Set<Element>();

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
    onOutsideClick: (event) => this.onOutsideClick(event),
  });
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
   * Whether a pointer press has begun since the menu last opened. Until
   * one has, a click reported outside the panel can only be the tail of
   * the gesture that opened the menu.
   */
  private pressedSinceOpen = false;

  /**
   * Dismiss on an outside POINTERDOWN, not the trailing click: Excel/Sheets
   * (and native menus) drop the menu the instant you press elsewhere, so a
   * range drag begins on a clean surface instead of dragging for seconds with
   * the menu still up. A press inside the panel is left alone so an item
   * activates on its click. The event is NOT consumed — the same press still
   * reaches its target (e.g. the grid's drag start). Front-most menu only.
   */
  private readonly onDocumentPointerDownCapture = (event: PointerEvent): void => {
    this.pressedSinceOpen = true;
    if (openMenuStack[openMenuStack.length - 1] !== this) {
      return;
    }
    const target = event.target;
    const panel = this.overlay()?.overlayRef?.overlayElement ?? null;
    if (target instanceof Node && panel !== null && panel.contains(target)) {
      return;
    }
    // A press on a trigger button toggles the menu from the button's own
    // click; closing here first would make that click re-open it.
    if (target instanceof Node) {
      for (const trigger of this.triggers) {
        if (trigger.contains(target)) {
          return;
        }
      }
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

    // Move focus onto the first (or last) row once the rows have rendered:
    // the overlay content lands one render pass after attach. The request
    // is cleared as it is served, so later re-renders never steal focus.
    afterRenderEffect(() => {
      const menu = this.menu();
      const target = this.pendingFocus();
      if (menu === undefined || target === null || !this.expanded() || menu._items().length === 0) {
        return;
      }
      untracked(() => {
        this.pendingFocus.set(null);
        if (target === 'last') {
          menu._pattern.last();
        } else {
          menu._pattern.first();
        }
        // Never leave focus outside an open menu, whose keys would be dead.
        if (!menu.element.contains(document.activeElement)) {
          menu.element.focus();
        }
      });
    });

    // The header block is read, never operated: focus cannot reach into it.
    if (isDevMode()) {
      afterRenderEffect(() => {
        if (this.header() === undefined || !this.expanded()) {
          return;
        }
        const block = document.getElementById(this.headerId);
        if (block?.querySelector('a[href], button, input, select, textarea, [tabindex]') != null) {
          console.warn(
            'tm-menu: tmMenuHeader holds interactive content, which the keyboard cannot reach. ' +
              'Keep it to text and images.',
          );
        }
      });
    }
  }

  /**
   * Opens the menu at an element, rectangle, or pointer position. While
   * already open, the menu re-anchors to the new position. Focus moves to
   * the first row, or the last with `initialFocus: 'last'`. Ignored (with a
   * dev-mode warning) when there is nothing actionable to show.
   */
  open(anchor: TmMenuAnchor, options?: TmMenuOpenOptions): void {
    if (!this.hasActionableItems()) {
      if (isDevMode()) {
        console.warn('tm-menu: open() ignored — the menu has no actionable items.');
      }
      return;
    }
    this.invoker.set(options?.restoreFocus ?? null);
    this.overlayOrigin.set(anchor);
    this.pendingFocus.set(options?.initialFocus ?? 'first');
    this.armDismissal();
    this.pressedSinceOpen = false;

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
      untracked(this.invoker)?.focus();
    }
    this.invoker.set(null);
  }

  /**
   * Carries out an activation reported by a row on any level: runs the
   * entry's action, emits `itemSelected` for an item, and closes the whole
   * menu unless the item keeps it open.
   * @internal
   */
  ɵactivate(entry: TmMenuItem | TmMenuLink): void {
    if (ɵtmIsMenuLink(entry)) {
      // A link with no action is being followed by the browser as we close.
      entry.action?.();
      this.close();
      return;
    }
    entry.action();
    this.itemSelected.emit(entry);
    if (entry.keepOpen !== true) {
      this.close();
    }
  }

  /**
   * The id of the element that carries `role="menu"`, for a trigger's
   * `aria-controls`.
   * @internal
   */
  get ɵlistId(): string {
    return this.listId;
  }

  /**
   * Records a trigger button so a press on it is never treated as an
   * outside press, and returns the callback that releases it again.
   * Called by `TmMenuTrigger` for that directive's lifetime.
   * @internal
   */
  ɵregisterTrigger(element: Element): () => void {
    this.triggers.add(element);
    return (): void => {
      this.triggers.delete(element);
    };
  }

  /**
   * Whether the menu is open and was opened from `element`, which is what
   * that element's `aria-expanded` reports.
   * @internal
   */
  ɵisOpenFor(element: Element): boolean {
    return this.expanded() && this.invoker() === element;
  }

  // ---- internals ----

  /** An activation the aria menu reports, from a row on any level. */
  protected onItemSelected(entry: TmMenuItem | TmMenuLink): void {
    this.ɵactivate(entry);
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

  /**
   * The browser's own context menu is suppressed over the menu, except on
   * a link, where it is how the user copies the address or opens a new tab.
   */
  protected onPanelContextMenu(event: MouseEvent): void {
    if (!(event.target instanceof Element && event.target.closest('a[href]') !== null)) {
      event.preventDefault();
    }
  }

  /**
   * A click outside the panel, reported by the overlay. It closes the menu
   * without returning focus, unless it is the release of the gesture that
   * opened the menu.
   */
  private onOutsideClick(event: MouseEvent): void {
    // Where the engine raises `contextmenu` on the press (Firefox, WebKit,
    // and Blink outside Windows), the menu opens with the button still
    // down, and the release then produces an `auxclick` on the invoking
    // element, outside the panel. Safari likewise follows the `contextmenu`
    // of a Ctrl+click with a `click`. Neither is a click away from the
    // menu. The CDK's own context-menu trigger skips the same two events;
    // it knows the event that opened it, where `open()` is called with
    // none, so the test here is that no press has begun since the open. A
    // real click away starts with a press, which has already closed the
    // menu by the time its click arrives.
    if (!this.pressedSinceOpen && (event.type === 'auxclick' || event.ctrlKey)) {
      return;
    }
    this.close({ restoreFocus: false });
  }

  /**
   * Escape closes one level per press: an open submenu first, returning
   * focus to its parent row, then the menu. Which of the two is decided
   * from what is expanded as the key arrives, so a key pressed in the same
   * moment a submenu opens or closes still reaches the right level.
   */
  private onEscape(): void {
    const rows = untracked(() => this.menu()?._items()) ?? [];
    const parent = rows.find((row) => untracked(row.expanded) === true);
    if (parent !== undefined) {
      parent._pattern.close({ refocus: true });
    } else {
      this.close();
    }
  }

  /** Keeps `expanded` honest when the overlay detaches out-of-band. */
  private onOverlayDetach(): void {
    if (untracked(this.expanded)) {
      this.disarmDismissal();
      this.expanded.set(false);
      this.invoker.set(null);
    }
    this.closed.emit();
  }

  /** Whether the menu has at least one row to show, on its own or in a group. */
  private hasActionableItems(): boolean {
    return untracked(this.items).some((entry) =>
      ɵtmIsMenuGroup(entry) ? entry.items.length > 0 : !ɵtmIsMenuSeparator(entry),
    );
  }

  /**
   * Registers this menu as the front-most dismissal target: the Escape and
   * outside-pointerdown handlers listen at the document capture phase and the
   * menu joins the open-menu stack. Idempotent, so a re-anchor never
   * double-registers (the listeners de-dupe by reference).
   */
  private armDismissal(): void {
    this.releaseEscape ??= tmPushEscapeDismissal(() => this.onEscape());
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
