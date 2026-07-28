// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  type ComponentRef,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  type OnDestroy,
  untracked,
  ViewContainerRef,
} from '@angular/core';
import { AriaDescriber } from '@angular/cdk/a11y';

import { ɵtmObserveLongPress } from '@tellma/core-ui/menu';
import { tmPushEscapeDismissal } from '@tellma/core-ui/private';

import { ɵTmTooltipPanel } from './internal/tm-tooltip-panel';

/** Hides the currently shown tooltip — at most one is visible at a time. */
let hideActiveTooltip: (() => void) | null = null;

/** Grace period for the pointer to travel from the host onto the surface. */
const HIDE_GRACE_MS = 100;

/** Parses a CSS time value ('500ms', '0.5s') to milliseconds. */
function parseCssTime(value: string, fallback: number): number {
  const trimmed = value.trim();
  const numeric = Number.parseFloat(trimmed);
  if (!Number.isFinite(numeric)) {
    return fallback;
  }
  return trimmed.endsWith('ms') || !trimmed.endsWith('s') ? numeric : numeric * 1000;
}

/**
 * A plain-text tooltip on any element. Interactive or rich content belongs
 * in `tm-popover` — a tooltip must never contain focusables.
 *
 * Shows on pointer hover (after the `--tooltip-delay` token delay) and on
 * `:focus-visible` (immediately); hides on pointer leave, blur, and Escape
 * (WCAG 1.4.13 dismissable). The surface itself is hoverable — moving the
 * pointer onto it does not dismiss (1.4.13 hoverable/persistent). On touch,
 * a long-press shows it until the next tap. At most one tooltip is visible
 * at a time. The text is registered as the host's accessible description
 * (CDK `AriaDescriber`), so assistive technology reads it whether or not
 * the tooltip has ever opened — tooltips stay a progressive enhancement,
 * and content required for operation must not live only in a tooltip.
 *
 * Known limitation: natively `disabled` controls fire no pointer events in
 * most engines, so a tooltip on a disabled button never shows on hover.
 * Use suppressed-activation states instead (what `tmButton`'s `pending`
 * does), or place the tooltip on a wrapper element.
 *
 * @tmGroup overlay
 * @tmA11yNotes The panel is `role="tooltip"` and `aria-hidden` (the CDK
 *   AriaDescriber description carries the text to AT); never
 *   focus-stealing; Escape dismisses without side effects.
 */
@Directive({
  selector: '[tmTooltip]',
  host: {
    // A bound [tmTooltip] input leaves no DOM attribute — the class is the
    // stable hook harnesses and styles can rely on.
    class: 'tm-tooltip-host',
    '(pointerenter)': 'onPointerEnter($event)',
    '(pointerleave)': 'onPointerLeave($event)',
    '(focus)': 'onFocus()',
    '(blur)': 'onBlur()',
  },
})
export class TmTooltip implements OnDestroy {
  /** The tooltip text (plain text only). */
  readonly text = input.required<string>({ alias: 'tmTooltip' });

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly viewContainer = inject(ViewContainerRef);
  private readonly describer = inject(AriaDescriber);

  private panelRef: ComponentRef<ɵTmTooltipPanel> | null = null;
  private describedText: string | null = null;
  private showTimer: ReturnType<typeof setTimeout> | undefined;
  private hideTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly disposeLongPress: () => void;
  /** This instance's stable hide handle — its identity keys the coordinator. */
  private readonly hide = (): void => this.hideNow();

  /**
   * The shared Escape-dismissal registration while visible — dismisses
   * this layer only (a visible tooltip over an open menu takes the first
   * Escape; the menu takes the second).
   */
  private releaseEscape: (() => void) | null = null;

  /** Armed by a long-press show: the next tap anywhere dismisses. */
  private readonly onDocumentPointerDownCapture = (): void => {
    this.hideNow();
  };

  constructor() {
    // The description is available to AT from the start — no opening
    // needed. Re-registered whenever the text input changes.
    effect(() => {
      const text = this.text();
      untracked(() => {
        if (this.describedText !== null) {
          this.describer.removeDescription(this.host, this.describedText);
        }
        this.describer.describe(this.host, text);
        this.describedText = text;
        this.panelRef?.instance.text.set(text);
      });
    });

    this.disposeLongPress = ɵtmObserveLongPress(this.host, () => {
      this.showNow();
      document.addEventListener('pointerdown', this.onDocumentPointerDownCapture, true);
    });
  }

  ngOnDestroy(): void {
    this.hideNow();
    clearTimeout(this.showTimer);
    this.disposeLongPress();
    if (this.describedText !== null) {
      this.describer.removeDescription(this.host, this.describedText);
    }
  }

  // ---- show/hide paths ----

  /** Hover shows after the token-driven delay; touch waits for long-press. */
  protected onPointerEnter(event: PointerEvent): void {
    // A touch tap must not hover-show (long-press owns touch); anything
    // else — mouse, pen, or a synthetic event without a pointerType — is a
    // genuine hover.
    if (event.pointerType === 'touch') {
      return;
    }
    clearTimeout(this.hideTimer);
    clearTimeout(this.showTimer);
    this.showTimer = setTimeout(() => this.showNow(), this.showDelayMs());
  }

  protected onPointerLeave(event: PointerEvent): void {
    // Non-hover devices fire pointerleave right after the finger lifts —
    // hiding there would dismiss a long-press tooltip ~100ms after
    // release. Touch dismissal belongs to the next tap (armed on show).
    if (event.pointerType === 'touch') {
      return;
    }
    clearTimeout(this.showTimer);
    this.scheduleHide();
  }

  /** Keyboard focus shows immediately; programmatic/pointer focus does not. */
  protected onFocus(): void {
    if (this.host.matches(':focus-visible')) {
      this.showNow();
    }
  }

  protected onBlur(): void {
    this.hideNow();
  }

  // ---- internals ----

  private showDelayMs(): number {
    return parseCssTime(getComputedStyle(this.host).getPropertyValue('--tooltip-delay'), 500);
  }

  private showNow(): void {
    clearTimeout(this.showTimer);
    clearTimeout(this.hideTimer);
    if (hideActiveTooltip !== this.hide) {
      hideActiveTooltip?.();
      hideActiveTooltip = this.hide;
    }
    if (this.panelRef === null) {
      // Lazily created next to the host; the panel's overlay template
      // renders into the top layer.
      this.panelRef = this.viewContainer.createComponent(ɵTmTooltipPanel);
      const panel = this.panelRef.instance;
      panel.origin.set(this.host);
      panel.text.set(untracked(this.text));
      panel.pointerEnter = () => clearTimeout(this.hideTimer);
      panel.pointerLeave = () => this.scheduleHide();
    }
    if (!untracked(this.panelRef.instance.expanded)) {
      this.panelRef.instance.expanded.set(true);
      this.releaseEscape ??= tmPushEscapeDismissal(() => this.hideNow());
    }
  }

  /** Grace-period hide, cancelled when the pointer reaches the surface. */
  private scheduleHide(): void {
    clearTimeout(this.hideTimer);
    this.hideTimer = setTimeout(() => this.hideNow(), HIDE_GRACE_MS);
  }

  private hideNow(): void {
    clearTimeout(this.showTimer);
    clearTimeout(this.hideTimer);
    this.releaseEscape?.();
    this.releaseEscape = null;
    document.removeEventListener('pointerdown', this.onDocumentPointerDownCapture, true);
    if (hideActiveTooltip === this.hide) {
      hideActiveTooltip = null;
    }
    if (this.panelRef !== null && untracked(this.panelRef.instance.expanded)) {
      this.panelRef.instance.expanded.set(false);
    }
  }
}
