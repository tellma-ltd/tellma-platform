// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  contentChild,
  Directive,
  DestroyRef,
  type ElementRef,
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
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';

import {
  tmCreateAnchoredOverlay,
  tmLogicalPositions,
  type TmOverlayAlign,
  type TmOverlaySide,
} from '@tellma/core-ui/private';

/** Where a popover opens: an element or a rectangle. */
export type TmPopoverAnchor = Element | DOMRect;

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
      (overlayKeydown)="onOverlayKeydown($event)"
    >
      <div
        #panel
        class="tm-popover__panel"
        role="dialog"
        tabindex="-1"
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

  private readonly overlay = viewChild(CdkConnectedOverlay);
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private focusedOnOpen = false;
  private destroyed = false;

  /**
   * The shared anchored-overlay wiring. Outside clicks landing on the
   * current anchor element are left to the trigger's own toggle — closing
   * here too would close-then-reopen on every trigger click.
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.overlayOrigin(),
    positions: () => tmLogicalPositions(this.position(), this.align()),
    remeasure: 'macrotask',
    onAttach: () => this.opened.emit(),
    onDetach: () => this.onOverlayDetach(),
    onOutsideClick: (event) => {
      const anchor = untracked(this.overlayOrigin);
      if (anchor instanceof Element && event.target instanceof Node && anchor.contains(event.target)) {
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

    // Re-anchor rather than (re)open whenever the overlay is still live (a
    // genuine open-while-open): the CDK `open` binding never toggles, so
    // the overlay neither re-attaches nor re-applies its object-form
    // origin — drive the re-measure explicitly.
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

  // ---- internals ----

  /** Escape closes (unless an inner overlay already consumed it). */
  protected onPanelKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !event.defaultPrevented) {
      event.preventDefault();
      this.close();
    }
  }

  /**
   * The Escape fallback for keys pressed OUTSIDE the panel while the
   * popover is open (focus resting on a programmatic anchor): the CDK
   * dispatcher routes document keydowns to the topmost overlay with
   * observers — without this handler the event would die here unhandled,
   * starving outer layers (a modal) of their Escape too. The
   * `defaultPrevented` guard keeps the panel-keydown path from double
   * handling the same event.
   */
  protected onOverlayKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !event.defaultPrevented) {
      event.preventDefault();
      this.close();
    }
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
    if (untracked(this.expanded)) {
      this.expanded.set(false);
    }
    this.closed.emit();
  }
}
