// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  contentChild,
  Directive,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  isDevMode,
  output,
  signal,
  TemplateRef,
  type Signal,
  untracked,
  viewChild,
} from '@angular/core';
import { Directionality } from '@angular/cdk/bidi';
import {
  CdkConnectedOverlay,
  type ConnectedOverlayPositionChange,
  type ConnectionPositionPair,
  OverlayModule,
} from '@angular/cdk/overlay';

import {
  tmCreateAnchoredOverlay,
  tmLogicalPositions,
  type TmOverlayAlign,
  type TmOverlaySide,
} from '@tellma/core-ui/overlay';

import { tmPopoverBlockRoom, tmPopoverInlineShift, tmTokenPx } from './internal/fit';

/** Where a popover opens: an element or a rectangle. */
export type TmPopoverAnchor = Element | DOMRect;

/**
 * The width of a popover panel. `'auto'` takes the content's width, up to
 * `--popover-max-inline-size`. The fixed sizes (`--popover-width-sm|md|lg`)
 * never change while the panel is open, whatever its content loads, so the
 * panels of one kind read alike. Every size is capped at the viewport width
 * less `--popover-viewport-margin` on each side.
 */
export type TmPopoverSize = 'auto' | 'sm' | 'md' | 'lg';

/**
 * Marks the `ng-template` holding a popover's content. A template (rather
 * than projected elements) keeps the content lazy: it instantiates on first
 * open and is destroyed on close — nothing floats until it is needed.
 *
 * ```html
 * <tm-popover #filters>
 *   <ng-template tmPopoverContent>…</ng-template>
 * </tm-popover>
 * ```
 */
@Directive({ selector: 'ng-template[tmPopoverContent]' })
export class TmPopoverContent {
  /** The content template this marker sits on. */
  readonly template = inject<TemplateRef<void>>(TemplateRef);
}

/** Matches the elements a popover moves initial focus to. */
const FOCUSABLE =
  'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
  'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * An anchored, non-modal `role="dialog"` panel for rich interactive
 * content — the step between a tooltip (plain text, never focusable) and a
 * modal (blocking). Filter panels, column pickers, inline help with links.
 *
 * Open it from a `button[tmPopoverTriggerFor]` trigger (click toggles) or
 * programmatically via {@link open} against any element or rectangle — the
 * same anchor flexibility `tm-menu` exposes. The panel renders in the
 * native top layer (never clipped by scroll containers), positioned on the
 * logical `position`/`align` side with automatic flip; RTL mirrors with no
 * extra wiring.
 *
 * On open, focus moves to the first tabbable element inside (else the
 * panel itself). Escape, an outside click, and tabbing past the content
 * close it; focus returns to the anchor only when it was inside the panel.
 *
 * @tmGroup overlay
 * @tmA11yNotes The panel is a non-modal `role="dialog"` (no focus trap);
 *   the trigger carries `aria-expanded` and `aria-haspopup="dialog"`. Give
 *   the dialog an accessible name via `aria-label`.
 */
@Component({
  selector: 'tm-popover',
  imports: [NgTemplateOutlet, OverlayModule],
  template: `
    <ng-template
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="anchored.handleAttach()"
      (detach)="anchored.handleDetach()"
      (overlayOutsideClick)="anchored.handleOutsideClick($event)"
      (positionChange)="onPositionChange($event)"
    >
      <!-- The size class fixes the width; panelClass is the consumer's hatch. -->
      <div
        #panel
        role="dialog"
        tabindex="-1"
        [class]="panelClasses()"
        [attr.aria-label]="ariaLabel()"
        (keydown)="onPanelKeydown($event)"
        (focusout)="onPanelFocusOut($event)"
      >
        @if (contentTemplate(); as content) {
          <ng-container [ngTemplateOutlet]="content.template" />
        }
      </div>
    </ng-template>
  `,
  styleUrl: './tm-popover.css',
  host: {
    class: 'tm-popover',
    // The accessible name belongs to the role="dialog" panel; strip it from
    // the role-less host so a static aria-label can't violate ARIA.
    '[attr.aria-label]': 'null',
  },
})
export class TmPopover {
  /** The side the panel prefers, flipping to the opposite on overflow. */
  readonly position = input<TmOverlaySide>('block-end');
  /** How the panel aligns along the chosen side's axis. */
  readonly align = input<TmOverlayAlign>('start');
  /**
   * The panel's width: `'auto'` (the default) for the content's width, or a
   * fixed `'sm'`, `'md'` or `'lg'` that never changes while open. The panel's
   * height is capped at the room the chosen side offers, and the content
   * scrolls inside.
   */
  readonly size = input<TmPopoverSize>('auto');
  /**
   * Extra CSS class on the panel — the escape hatch for the rare panel the
   * sizes do not fit. The panel renders in the top layer, outside this
   * component's styles, so the class must be reachable from a global
   * stylesheet.
   */
  readonly panelClass = input<string | undefined>(undefined);
  /** Accessible name of the dialog panel. */
  readonly ariaLabel = input<string | null>(null, { alias: 'aria-label' });

  /** Emits when the popover opens. */
  readonly opened = output<void>();
  /** Emits when the popover closes. */
  readonly closed = output<void>();

  /** Whether the popover is open. */
  readonly isOpen: Signal<boolean>;

  /**
   * The resolved content template marker. Content is template-based (see
   * {@link TmPopoverContent}) so it instantiates lazily on open and is
   * destroyed on close.
   */
  readonly contentTemplate = contentChild(TmPopoverContent);

  /** Whether the overlay is attached. */
  protected readonly expanded = signal(false);
  /** Where the overlay anchors. */
  protected readonly overlayOrigin = signal<TmPopoverAnchor | null>(null);

  /** The panel's classes: the base, the size, and the consumer's hatch. */
  protected readonly panelClasses = computed(() => {
    const size = this.size();
    const extra = this.panelClass();
    return `tm-popover__panel tm-popover__panel--${size} ${extra ?? ''}`;
  });

  /**
   * The physical inline offset that keeps a fixed-size panel inside the
   * viewport margins on a block side (see `fitInline`).
   */
  private readonly inlineShift = signal(0);
  /** The side the CDK last placed the panel on, for re-fitting the height. */
  private placedPair: ConnectionPositionPair | null = null;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly direction = inject(Directionality);

  private readonly overlay = viewChild(CdkConnectedOverlay);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private focusedOnOpen = false;
  private destroyed = false;
  /** Host elements of the triggers currently bound to this popover. */
  private readonly triggers = new Set<Element>();

  /** This instance's stable anchor handler — its identity keys the removal. */
  private readonly onAnchorKeydown = (event: Event): void => {
    if (event instanceof KeyboardEvent && event.key === 'Escape' && !event.defaultPrevented) {
      event.preventDefault();
      this.close();
    }
  };

  /**
   * The shared anchored-overlay wiring. Outside clicks landing on the
   * current anchor or on any registered trigger are left to that trigger's
   * own toggle — closing here too would close-then-reopen on a click.
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.overlayOrigin(),
    // The inline shift rides every position as its offset, so the CDK's own
    // fit and flip decisions already see the panel where it will sit.
    positions: () => {
      const offsetX = this.inlineShift();
      return tmLogicalPositions(this.position(), this.align()).map((pair) => ({
        ...pair,
        offsetX,
      }));
    },
    remeasure: 'macrotask',
    onAttach: () => this.opened.emit(),
    onDetach: () => this.onOverlayDetach(),
    onOutsideClick: (event) => {
      if (event.target instanceof Node && this.ownsClick(event.target)) {
        return;
      }
      this.close();
    },
  });

  constructor() {
    this.isOpen = this.expanded.asReadonly();
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
    });

    // Initial focus once the panel content has rendered: the first tabbable
    // element inside, else the panel itself. Guarded so later re-renders
    // never steal focus back.
    afterRenderEffect(() => {
      const panel = this.panel()?.nativeElement;
      if (panel === undefined || !this.expanded() || this.focusedOnOpen) {
        return;
      }
      this.focusedOnOpen = true;
      untracked(() => (panel.querySelector<HTMLElement>(FOCUSABLE) ?? panel).focus());
    });

    // The Escape fallback for keys pressed OUTSIDE the panel while open —
    // focus resting on a programmatic anchor, which neither the panel's
    // own keydown nor a trigger's covers. It is scoped to the anchor
    // element instead of routed through the CDK keyboard dispatcher: that
    // dispatcher delivers only to the TOPMOST overlay with a subscriber,
    // and every connected overlay subscribes, so any overlay attached
    // after this one — even a purely passive one that handles no keys —
    // would silently starve the fallback. Listening in the bubble phase on
    // the anchor keeps the innermost-first Escape precedence intact:
    // handlers nearer the event's target still consume it first.
    effect((onCleanup) => {
      const anchor = this.overlayOrigin();
      if (!this.expanded() || !(anchor instanceof Element)) {
        return;
      }
      anchor.addEventListener('keydown', this.onAnchorKeydown);
      onCleanup(() => anchor.removeEventListener('keydown', this.onAnchorKeydown));
    });
  }

  /**
   * Opens the popover at an element or rectangle. While already open, it
   * re-anchors to the new position. Ignored (with a dev-mode warning) when
   * no `tmPopoverContent` template was provided.
   */
  open(anchor: TmPopoverAnchor): void {
    if (untracked(this.contentTemplate) === undefined) {
      if (isDevMode()) {
        console.warn(
          'tm-popover: open() ignored — no content. Provide an ' +
            '<ng-template tmPopoverContent> child.',
        );
      }
      return;
    }
    this.overlayOrigin.set(anchor);
    this.focusedOnOpen = false;
    this.fitInline();

    // Re-anchor rather than (re)open whenever the overlay is still live (a
    // genuine open-while-open): the CDK `open` binding never toggles, so
    // the overlay neither re-attaches nor re-applies its object-form
    // origin — drive the re-measure explicitly. The re-measure is a
    // macrotask queued after the change detection the new origin and
    // offsets schedule, so it measures against them.
    if (untracked(this.expanded) || (this.overlay()?.overlayRef?.hasAttached() ?? false)) {
      this.expanded.set(true);
      this.anchored.reanchor();
      return;
    }
    this.expanded.set(true);
  }

  /**
   * Closes the popover. Focus returns to the anchor element only when it
   * currently sits inside the panel (Escape, a programmatic close mid-use)
   * — never yanked back after an outside interaction already moved it.
   */
  close(): void {
    if (!untracked(this.expanded)) {
      return;
    }
    const panel = this.panel()?.nativeElement;
    const anchor = untracked(this.overlayOrigin);
    const shouldRestore =
      panel !== undefined && panel.contains(document.activeElement) && anchor instanceof HTMLElement;
    this.expanded.set(false);
    if (shouldRestore) {
      anchor.focus();
    }
  }

  /**
   * Records a trigger's host element so a click on it is never treated as
   * an outside click, and returns the callback that releases it again.
   * Called by `TmPopoverTrigger` for that directive's lifetime.
   * @internal
   */
  ɵregisterTrigger(element: Element): () => void {
    this.triggers.add(element);
    return (): void => {
      this.triggers.delete(element);
    };
  }

  // ---- internals ----

  /**
   * The CDK placed the panel (on open, on a flip, or after the offsets
   * changed): cap its height to the room on that side.
   */
  protected onPositionChange(change: ConnectedOverlayPositionChange): void {
    this.placedPair = change.connectionPair;
    this.fitBlock();
  }

  /** The anchor's viewport rectangle, or null while there is none. */
  private anchorRect(): DOMRect | null {
    const anchor = untracked(this.overlayOrigin);
    return anchor instanceof Element ? anchor.getBoundingClientRect() : anchor;
  }

  /**
   * Keeps a fixed-size panel on a block side inside the viewport margins.
   * Its width is known before it renders (the size token, clamped to the
   * viewport as the stylesheet clamps it), so the offset is worked out
   * from the anchor alone and handed to the CDK with the positions: a `lg`
   * panel on a phone then sits full width with the margin on both sides,
   * instead of hanging off the edge its anchor is near. `'auto'` panels
   * and inline sides keep their anchor alignment.
   */
  private fitInline(): void {
    const size = untracked(this.size);
    const anchor = this.anchorRect();
    let shift = 0;
    if (size !== 'auto' && anchor !== null && !untracked(this.position).startsWith('inline')) {
      const margin = tmTokenPx(this.host, '--popover-viewport-margin');
      shift = tmPopoverInlineShift(
        anchor,
        // `100vw` in the stylesheet counts a classic scrollbar; the visible
        // width the panel must stay inside does not.
        Math.min(tmTokenPx(this.host, `--popover-width-${size}`), window.innerWidth - 2 * margin),
        untracked(this.align),
        this.direction.value === 'rtl',
        document.documentElement.clientWidth,
        margin,
      );
    }
    this.inlineShift.set(shift);
  }

  /**
   * Caps the panel's height at the room the side it was placed on offers,
   * less the viewport margin, so a tall panel scrolls inside instead of
   * running off screen, and a panel whose content loads later grows only up
   * to that cap — it never flips while open. A panel the CDK placed at its
   * full height because no side had room for it keeps the edge it is
   * anchored by (its top below the anchor, its bottom above it) as it
   * shrinks to the cap.
   */
  private fitBlock(): void {
    const panel = this.panel()?.nativeElement;
    const anchor = this.anchorRect();
    if (panel === undefined || this.placedPair === null || anchor === null) {
      return;
    }
    const room = tmPopoverBlockRoom(
      anchor,
      this.placedPair,
      document.documentElement.clientHeight,
      tmTokenPx(this.host, '--popover-viewport-margin'),
    );
    panel.style.setProperty('--tm-popover-block-cap', `${room}px`);
  }

  /** Escape closes (unless an inner overlay already consumed it). */
  protected onPanelKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !event.defaultPrevented) {
      event.preventDefault();
      this.close();
    }
  }

  /**
   * Whether a click on `target` belongs to this popover's own toggling
   * surface: the current anchor, or ANY registered trigger.
   *
   * Every trigger counts, not just the one the panel currently sits at.
   * The CDK reports outside clicks from a document-level CAPTURE listener,
   * so it runs before the trigger's bubble-phase click handler; matching on
   * anchor identity alone would let a click on a second trigger close the
   * popover first, leaving `toggle()` to read an already-false `isOpen()`
   * and re-anchor instead of collapsing — a trigger showing
   * `aria-expanded="true"` would never close the panel.
   */
  private ownsClick(target: Node): boolean {
    const anchor = untracked(this.overlayOrigin);
    if (anchor instanceof Element && anchor.contains(target)) {
      return true;
    }
    for (const trigger of this.triggers) {
      if (trigger.contains(target)) {
        return true;
      }
    }
    return false;
  }

  /**
   * Non-modal Tab-out close: when focus leaves the panel for anywhere but
   * the anchor (whose own toggle owns that path), the popover closes.
   *
   * The destination comes from the event's `relatedTarget` — a deferred
   * `document.activeElement` check is unreliable here, because microtask
   * checkpoints run BETWEEN the focusout and focusin dispatches, when
   * focus transiently sits on `body`. Only a destination-less focusout
   * (focus genuinely dropped to body) defers to let focus settle.
   */
  protected onPanelFocusOut(event: FocusEvent): void {
    const next = event.relatedTarget;
    if (next instanceof Node) {
      this.closeIfFocusLeft(next);
      return;
    }
    queueMicrotask(() => {
      if (this.destroyed || !untracked(this.expanded)) {
        return;
      }
      this.closeIfFocusLeft(document.activeElement);
    });
  }

  /** Closes unless the focus destination is inside the panel or the anchor. */
  private closeIfFocusLeft(next: Node | null): void {
    const panel = this.panel()?.nativeElement;
    const anchor = untracked(this.overlayOrigin);
    if (
      panel !== undefined &&
      (next === null || !panel.contains(next)) &&
      !(anchor instanceof Element && next !== null && anchor.contains(next))
    ) {
      this.close();
    }
  }

  /** Keeps `expanded` honest when the overlay detaches out-of-band. */
  private onOverlayDetach(): void {
    this.placedPair = null;
    if (untracked(this.expanded)) {
      this.expanded.set(false);
    }
    this.closed.emit();
  }
}
