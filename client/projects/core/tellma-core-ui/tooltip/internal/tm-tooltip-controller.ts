// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type ComponentRef, untracked, type ViewContainerRef } from '@angular/core';
import type { AriaDescriber } from '@angular/cdk/a11y';

import { ɵtmObserveLongPress } from '@tellma/core-ui/menu';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';

import { ɵTmTooltipPanel } from './tm-tooltip-panel';

/** Hides the currently shown tooltip — at most one is visible at a time. */
let hideActiveTooltip: (() => void) | null = null;

/** Grace period for the pointer to travel from the host onto the surface. */
const HIDE_GRACE_MS = 100;

/** How far a finger may travel between press and lift and still be a tap. */
const TAP_SLOP_PX = 8;

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
 * The behaviour of one tooltip on one host element: the accessible
 * description, the hover, focus, long-press and (optionally) tap paths,
 * the shared Escape stack, and the lazily created floating surface.
 *
 * `TmTooltip` drives one from its inputs; `tmButton` drives its own for a
 * disabled reason when the button carries no `tmTooltip`. The listeners are
 * attached here rather than as host bindings so a host that only sometimes
 * needs a tooltip (a button while it has a reason) pays for none of it the
 * rest of the time.
 *
 * @internal Shared by `TmTooltip` and `TmButton`; never use directly.
 */
export class ɵTmTooltipController {
  /** Whether a touch tap on the host shows the tooltip (long-press always does). */
  showOnTap = false;

  private text = '';
  private describedText: string | null = null;
  private panelRef: ComponentRef<ɵTmTooltipPanel> | null = null;
  private showTimer: ReturnType<typeof setTimeout> | undefined;
  private hideTimer: ReturnType<typeof setTimeout> | undefined;
  /** Where the current touch press started, for telling a tap from a drag. */
  private press: { readonly id: number; readonly x: number; readonly y: number } | null = null;
  private readonly disposeLongPress: () => void;
  /** This instance's stable hide handle — its identity keys the coordinator. */
  private readonly hide = (): void => this.hideNow();
  /**
   * The shared Escape-dismissal registration while visible — dismisses this
   * layer only (a visible tooltip over an open menu takes the first Escape;
   * the menu takes the second).
   */
  private releaseEscape: (() => void) | null = null;

  /**
   * @param host The element the tooltip describes and anchors to.
   * @param viewContainer Where the surface is created, resolved on first show.
   * @param describer The CDK describer that registers the text as the host's description.
   */
  constructor(
    private readonly host: HTMLElement,
    private readonly viewContainer: () => ViewContainerRef,
    private readonly describer: AriaDescriber,
  ) {
    host.addEventListener('pointerenter', this.onPointerEnter);
    host.addEventListener('pointerleave', this.onPointerLeave);
    host.addEventListener('pointerdown', this.onPointerDown);
    host.addEventListener('pointerup', this.onPointerUp);
    host.addEventListener('focus', this.onFocus);
    host.addEventListener('blur', this.onBlur);
    this.disposeLongPress = ɵtmObserveLongPress(host, () => this.showUntilNextTap());
  }

  /**
   * Sets the tooltip text: re-registers the host's description (available
   * to assistive technology whether or not the tooltip ever opens) and
   * updates a visible surface in place.
   */
  setText(text: string): void {
    if (this.describedText !== null) {
      this.describer.removeDescription(this.host, this.describedText);
    }
    this.describer.describe(this.host, text);
    this.describedText = text;
    this.text = text;
    this.panelRef?.instance.text.set(text);
  }

  /** Tears down the surface, timers, listeners and the description. */
  destroy(): void {
    this.hideNow();
    clearTimeout(this.showTimer);
    this.disposeLongPress();
    this.host.removeEventListener('pointerenter', this.onPointerEnter);
    this.host.removeEventListener('pointerleave', this.onPointerLeave);
    this.host.removeEventListener('pointerdown', this.onPointerDown);
    this.host.removeEventListener('pointerup', this.onPointerUp);
    this.host.removeEventListener('focus', this.onFocus);
    this.host.removeEventListener('blur', this.onBlur);
    if (this.describedText !== null) {
      this.describer.removeDescription(this.host, this.describedText);
      this.describedText = null;
    }
    this.panelRef?.destroy();
    this.panelRef = null;
  }

  // ---- show/hide paths ----

  /** Hover shows after the token-driven delay; touch waits for long-press or a tap. */
  private readonly onPointerEnter = (event: PointerEvent): void => {
    // A touch tap must not hover-show (long-press and tap own touch);
    // anything else — mouse, pen, or a synthetic event without a
    // pointerType — is a genuine hover.
    if (event.pointerType === 'touch') {
      return;
    }
    clearTimeout(this.hideTimer);
    clearTimeout(this.showTimer);
    this.showTimer = setTimeout(() => this.showNow(), this.showDelayMs());
  };

  /** Hover leaving schedules the hide (the grace period covers the crossing). */
  private readonly onPointerLeave = (event: PointerEvent): void => {
    // Non-hover devices fire pointerleave right after the finger lifts —
    // hiding there would dismiss a touch-shown tooltip ~100ms after
    // release. Touch dismissal belongs to the next tap (armed on show).
    if (event.pointerType === 'touch') {
      return;
    }
    clearTimeout(this.showTimer);
    this.scheduleHide();
  };

  /** Records where a touch press starts, for the tap path. */
  private readonly onPointerDown = (event: PointerEvent): void => {
    this.press =
      event.pointerType === 'touch' && event.isPrimary
        ? { id: event.pointerId, x: event.clientX, y: event.clientY }
        : null;
  };

  /**
   * A touch tap (a press lifted where it started) shows the tooltip when
   * `showOnTap` is set — for a host whose tap does nothing else, such as a
   * button disabled with a reason. It hides on the next tap elsewhere.
   */
  private readonly onPointerUp = (event: PointerEvent): void => {
    const press = this.press;
    this.press = null;
    if (
      !this.showOnTap ||
      press === null ||
      press.id !== event.pointerId ||
      Math.abs(event.clientX - press.x) > TAP_SLOP_PX ||
      Math.abs(event.clientY - press.y) > TAP_SLOP_PX
    ) {
      return;
    }
    this.showUntilNextTap();
  };

  /** Keyboard focus shows immediately; programmatic or pointer focus does not. */
  private readonly onFocus = (): void => {
    if (this.host.matches(':focus-visible')) {
      this.showNow();
    }
  };

  /** Losing focus hides immediately (WCAG 1.4.13 dismissable). */
  private readonly onBlur = (): void => {
    this.hideNow();
  };

  /** Armed by a touch show: the next press anywhere dismisses. */
  private readonly onDocumentPointerDownCapture = (): void => {
    this.hideNow();
  };

  // ---- internals ----

  /** Shows the tooltip and arms the next-tap dismissal. */
  private showUntilNextTap(): void {
    this.showNow();
    document.addEventListener('pointerdown', this.onDocumentPointerDownCapture, true);
  }

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
      this.panelRef = this.viewContainer().createComponent(ɵTmTooltipPanel);
      const panel = this.panelRef.instance;
      panel.origin.set(this.host);
      panel.text.set(this.text);
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
