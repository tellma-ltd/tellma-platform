// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/** Options of {@link tmObserveLongPress}. */
export interface TmLongPressOptions {
  /** Press duration before the gesture fires. Defaults to 500ms. */
  readonly delayMs?: number;
  /** Movement tolerance before the press cancels. Defaults to 8px. */
  readonly slopPx?: number;
}

/**
 * Observes touch/pen long-presses on an element (mouse presses never
 * qualify — right-click owns that path). The press cancels on movement
 * beyond the slop, on release, on cancellation, and on scrolling; when it
 * fires, the trailing synthetic `click` and native `contextmenu` are
 * suppressed once so the page doesn't double-react. Returns the cleanup
 * function.
 */
export function tmObserveLongPress(
  element: HTMLElement,
  onLongPress: (point: { x: number; y: number }) => void,
  options?: TmLongPressOptions,
): () => void {
  const delay = options?.delayMs ?? 500;
  const slop = options?.slopPx ?? 8;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let startX = 0;
  let startY = 0;
  let pointerId: number | null = null;
  /** Trailing click/contextmenu events are suppressed until this instant. */
  let suppressUntil = 0;

  const cancel = (): void => {
    clearTimeout(timer);
    timer = undefined;
    pointerId = null;
  };

  const onPointerDown = (event: PointerEvent): void => {
    if (event.pointerType === 'mouse' || !event.isPrimary) {
      return;
    }
    startX = event.clientX;
    startY = event.clientY;
    pointerId = event.pointerId;
    clearTimeout(timer);
    timer = setTimeout(() => {
      if (pointerId !== null) {
        // The browser may fire contextmenu and/or a synthetic click after
        // the press releases (engine-dependent); swallow that trailing burst.
        suppressUntil = Date.now() + 700;
        onLongPress({ x: startX, y: startY });
        cancel();
      }
    }, delay);
  };

  const onPointerMove = (event: PointerEvent): void => {
    if (
      pointerId === event.pointerId &&
      (Math.abs(event.clientX - startX) > slop || Math.abs(event.clientY - startY) > slop)
    ) {
      cancel();
    }
  };

  const onPointerEnd = (event: PointerEvent): void => {
    if (pointerId === event.pointerId) {
      cancel();
    }
  };

  const onScroll = (): void => {
    cancel();
  };

  /** Suppresses the events trailing a fired long-press. */
  const suppress = (event: Event): void => {
    if (Date.now() < suppressUntil) {
      event.preventDefault();
      event.stopPropagation();
    }
  };

  element.addEventListener('pointerdown', onPointerDown);
  element.addEventListener('pointermove', onPointerMove);
  element.addEventListener('pointerup', onPointerEnd);
  element.addEventListener('pointercancel', onPointerEnd);
  window.addEventListener('scroll', onScroll, { capture: true, passive: true });
  element.addEventListener('click', suppress, true);
  element.addEventListener('contextmenu', suppress, true);

  return () => {
    cancel();
    element.removeEventListener('pointerdown', onPointerDown);
    element.removeEventListener('pointermove', onPointerMove);
    element.removeEventListener('pointerup', onPointerEnd);
    element.removeEventListener('pointercancel', onPointerEnd);
    window.removeEventListener('scroll', onScroll, { capture: true });
    element.removeEventListener('click', suppress, true);
    element.removeEventListener('contextmenu', suppress, true);
  };
}
