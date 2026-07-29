// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  afterNextRender,
  type AfterRenderRef,
  computed,
  DestroyRef,
  inject,
  Injector,
  type Signal,
} from '@angular/core';
import type {
  CdkConnectedOverlay,
  CdkConnectedOverlayConfig,
  ConnectedPosition,
  FlexibleOverlayPopoverLocation,
} from '@angular/cdk/overlay';

/**
 * Where an anchored overlay attaches: an element, a rectangle, or a point.
 * A `DOMRect` is normalized to the CDK's `{x, y, width, height}` origin
 * shape; elements and points pass through unchanged.
 */
export type TmAnchoredOverlayOrigin =
  | Element
  | DOMRect
  | { readonly x: number; readonly y: number; readonly width?: number; readonly height?: number };

/** The logical side an overlay prefers, resolved against the direction by the CDK. */
export type TmOverlaySide = 'block-end' | 'block-start' | 'inline-start' | 'inline-end';

/** How the overlay aligns along the chosen side's axis. */
export type TmOverlayAlign = 'start' | 'center' | 'end';

/**
 * Configuration of {@link tmCreateAnchoredOverlay}. The helper centralizes
 * the settled `cdkConnectedOverlay` wiring — object-form config, top-layer
 * popover hosting, and the post-attach re-measure discipline — while the
 * component keeps its own `<ng-template cdkConnectedOverlay>` and open
 * state (`disableClose` is always set: the consumer owns Esc).
 */
export interface TmAnchoredOverlayConfig {
  /**
   * Accessor for the template's `CdkConnectedOverlay` directive (a
   * `viewChild`) — needed for `updatePosition()` re-measures. May be
   * omitted when `remeasure` is `'none'` and `reanchor()` is never called.
   */
  readonly overlay?: () => CdkConnectedOverlay | undefined;
  /**
   * The anchor, read reactively — re-anchoring flows through the config
   * binding. While `null` (nothing to anchor to yet) the emitted config
   * carries no origin.
   */
  readonly origin: () => TmAnchoredOverlayOrigin | null;
  /**
   * Preference-ordered connected positions. The CDK resolves `start`/`end`
   * against the overlay's `Directionality`, so logical sets (e.g. from
   * {@link tmLogicalPositions}) mirror under RTL with no extra wiring.
   */
  readonly positions: readonly ConnectedPosition[] | (() => readonly ConnectedPosition[]);
  /**
   * Slide the surface back inside the viewport when none of `positions`
   * fits — for surfaces whose alignment is a preference rather than a
   * meaning (a centered tooltip), so one near the window edge stays
   * readable instead of being clipped. Leave off where the connection
   * point carries meaning and a shifted panel would mislead.
   */
  readonly keepOnScreen?: boolean;
  /** Gap to keep from the viewport edge when `keepOnScreen` pushes. */
  readonly viewportMargin?: number;
  /**
   * The top-layer host. `'inline'` (the default) inserts the popover next
   * to the origin — right for most anchors, and it keeps the panel inside
   * the component's DOM so emulated-encapsulation styles apply. Pass
   * `{type: 'parent', element}` where sibling insertion would violate ARIA
   * structure (e.g. an anchor inside `role="row"`); a function defers the
   * choice to open time.
   */
  readonly popoverHost?: FlexibleOverlayPopoverLocation | (() => FlexibleOverlayPopoverLocation);
  /** Match the panel's inline-size to the origin's. Default `false`. */
  readonly matchWidth?: boolean;
  /**
   * Post-attach re-measure strategy (default `'none'`):
   *
   * - `'macrotask'` — the panel content lands one render pass AFTER the CDK
   *   attaches and measures (aria `DeferredContent`), so flip-up would
   *   measure a zero-height panel; a macrotask re-measure sees the real one.
   * - `'afterNextRender'` — a signal-driven origin must flush through change
   *   detection before measuring; a bare timer races CD and re-measures
   *   against the OLD origin, intermittently on slow machines.
   * - `'none'` — statically-sized content that renders with the attach.
   */
  readonly remeasure?: 'macrotask' | 'afterNextRender' | 'none';
  /** Runs on overlay attach, before the re-measure strategy. */
  readonly onAttach?: () => void;
  /** Runs on overlay detach. */
  readonly onDetach?: () => void;
  /** Runs on an outside click reported by the CDK overlay. */
  readonly onOutsideClick?: (event: MouseEvent) => void;
}

/**
 * The wired overlay composition returned by {@link tmCreateAnchoredOverlay}.
 * Bind `overlayConfig()` to `[cdkConnectedOverlay]` and the three handlers
 * to `(attach)`, `(detach)` and `(overlayOutsideClick)`; the open state
 * stays a component-owned `[cdkConnectedOverlayOpen]` binding.
 */
export interface TmAnchoredOverlay {
  /** The object-form CDK config; always carries `disableClose: true`. */
  readonly overlayConfig: Signal<CdkConnectedOverlayConfig>;
  /** Wire to `(attach)`: runs `onAttach`, then the re-measure strategy. */
  handleAttach(): void;
  /** Wire to `(detach)`: runs `onDetach`. */
  handleDetach(): void;
  /** Wire to `(overlayOutsideClick)`: runs `onOutsideClick`. */
  handleOutsideClick(event: MouseEvent): void;
  /**
   * Re-measures without re-running `onAttach` — for re-anchoring an
   * already-open overlay (one continuous open, not a fresh one). Honors
   * the configured `remeasure` strategy, and measures IMMEDIATELY under
   * `'none'`: that setting governs the automatic post-attach measure, so
   * an explicit call must never be silently dropped.
   */
  reanchor(): void;
}

/**
 * Creates the shared anchored-overlay composition. Must be called in an
 * injection context (a field initializer or constructor): the helper
 * registers its own destroy cleanup — a pending macrotask re-measure must
 * not outlive the component, because `updatePosition()` on a disposed
 * overlay throws — and `afterNextRender` scheduling needs the injector.
 */
export function tmCreateAnchoredOverlay(config: TmAnchoredOverlayConfig): TmAnchoredOverlay {
  const injector = inject(Injector);
  let pendingTimer: ReturnType<typeof setTimeout> | undefined;
  let pendingRender: AfterRenderRef | undefined;
  inject(DestroyRef).onDestroy(() => {
    clearTimeout(pendingTimer);
    pendingRender?.destroy();
  });

  const remeasureNow = (): void => {
    config.overlay?.()?.overlayRef?.updatePosition();
  };

  const scheduleRemeasure = (): void => {
    switch (config.remeasure ?? 'none') {
      case 'macrotask':
        clearTimeout(pendingTimer);
        pendingTimer = setTimeout(remeasureNow);
        break;
      case 'afterNextRender':
        pendingRender?.destroy();
        pendingRender = afterNextRender(remeasureNow, { injector });
        break;
      case 'none':
        break;
    }
  };

  const overlayConfig = computed<CdkConnectedOverlayConfig>(() => {
    const origin = config.origin();
    const popoverHost =
      typeof config.popoverHost === 'function'
        ? config.popoverHost()
        : (config.popoverHost ?? 'inline');
    const positions =
      typeof config.positions === 'function' ? config.positions() : config.positions;
    const result: CdkConnectedOverlayConfig = {
      usePopover: popoverHost,
      disableClose: true,
      positions: [...positions],
    };
    if (config.keepOnScreen === true) {
      result.push = true;
      result.viewportMargin = config.viewportMargin ?? 0;
    }
    if (origin !== null) {
      result.origin =
        origin instanceof DOMRect
          ? { x: origin.x, y: origin.y, width: origin.width, height: origin.height }
          : origin;
    }
    if (config.matchWidth === true) {
      result.matchWidth = true;
    }
    return result;
  });

  return {
    overlayConfig,
    handleAttach(): void {
      config.onAttach?.();
      scheduleRemeasure();
    },
    handleDetach(): void {
      config.onDetach?.();
    },
    handleOutsideClick(event: MouseEvent): void {
      config.onOutsideClick?.(event);
    },
    reanchor(): void {
      if ((config.remeasure ?? 'none') === 'none') {
        remeasureNow();
        return;
      }
      scheduleRemeasure();
    },
  };
}

/**
 * Expands a logical side + alignment into the preference-ordered CDK
 * position pair: the requested placement first, then its flip to the
 * opposite side. `start`/`end` are direction-resolved by the CDK against
 * the overlay's `Directionality`, so the same pair mirrors under RTL.
 */
export function tmLogicalPositions(
  side: TmOverlaySide,
  align: TmOverlayAlign,
): ConnectedPosition[] {
  if (side === 'block-end' || side === 'block-start') {
    const x = align;
    const below: ConnectedPosition = { originX: x, originY: 'bottom', overlayX: x, overlayY: 'top' };
    const above: ConnectedPosition = { originX: x, originY: 'top', overlayX: x, overlayY: 'bottom' };
    return side === 'block-end' ? [below, above] : [above, below];
  }
  const y = align === 'start' ? 'top' : align === 'end' ? 'bottom' : 'center';
  const after: ConnectedPosition = { originX: 'end', originY: y, overlayX: 'start', overlayY: y };
  const before: ConnectedPosition = { originX: 'start', originY: y, overlayX: 'end', overlayY: y };
  return side === 'inline-end' ? [after, before] : [before, after];
}
