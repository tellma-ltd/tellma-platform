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
  /**
   * Consulted when a press starts (and again when its timer elapses). While
   * it returns `false` the press starts no timer and arms no suppression, so
   * the platform's own long-press behavior is left intact. Defaults to
   * always-enabled.
   */
  readonly enabled?: () => boolean;
}

/**
 * Observes touch/pen long-presses on an element (mouse presses never
 * qualify — right-click owns that path). The press cancels on movement
 * beyond the slop, on release, on cancellation, and on scrolling; when it
 * fires, the single trailing synthetic `click` or native `contextmenu` it
 * spawns is suppressed once so the page doesn't double-react. Returns the
 * cleanup function.
 */
export function tmObserveLongPress(
  element: HTMLElement,
  onLongPress: (point: { x: number; y: number }) => void,
  options?: TmLongPressOptions,
): () => void {
  const delay = options?.delayMs ?? 500;
  const slop = options?.slopPx ?? 8;
  const enabled = options?.enabled ?? (() => true);
  let timer: ReturnType<typeof setTimeout> | undefined;
  let startX = 0;
  let startY = 0;
  let pointerId: number | null = null;
  /** Armed between a fired press and the single trailing event it spawns. */
  let suppressArmed = false;
  /** The node the firing press landed on; the trailing burst targets it, its subtree, or an ancestor. */
  let pressedTarget: Node | null = null;
  /** Safety net: disarms suppression if the trailing burst never arrives. */
  let disarmTimer: ReturnType<typeof setTimeout> | undefined;

  const cancel = (): void => {
    clearTimeout(timer);
    timer = undefined;
    pointerId = null;
  };

  const disarm = (): void => {
    suppressArmed = false;
    pressedTarget = null;
    clearTimeout(disarmTimer);
    disarmTimer = undefined;
  };

  const onPointerDown = (event: PointerEvent): void => {
    if (event.pointerType === 'mouse' || !event.isPrimary || !enabled()) {
      return;
    }
    // A fresh press supersedes any suppression still armed from a prior one.
    disarm();
    startX = event.clientX;
    startY = event.clientY;
    pointerId = event.pointerId;
    pressedTarget = event.target instanceof Node ? event.target : element;
    clearTimeout(timer);
    timer = setTimeout(() => {
      if (pointerId === null || !enabled()) {
        cancel();
        return;
      }
      // The browser may fire contextmenu and/or a synthetic click after the
      // press releases (engine-dependent); arm a one-shot swallow of that
      // trailing burst so the page doesn't double-react.
      suppressArmed = true;
      onLongPress({ x: startX, y: startY });
      cancel();
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
    // Release of the pressing pointer: if a long-press already fired, give its
    // trailing burst a brief window to land, then disarm so a later, unrelated
    // tap is never swallowed.
    if (suppressArmed && disarmTimer === undefined) {
      disarmTimer = setTimeout(disarm, 700);
    }
  };

  const onScroll = (): void => {
    cancel();
  };

  /** Suppresses the single event trailing a fired long-press. */
  const suppress = (event: Event): void => {
    if (!suppressArmed) {
      return;
    }
    const target = event.target;
    // Only the trailing burst from THIS press — the pressed node, its subtree,
    // or an ancestor up to the document — never a tap on the overlay panel the
    // press just opened (it portals outside the pressed element's subtree).
    if (
      target instanceof Node &&
      (element.contains(target) || (pressedTarget !== null && target.contains(pressedTarget)))
    ) {
      disarm(); // one-shot: the first trailing event consumes the suppression
      event.preventDefault();
      event.stopPropagation();
    }
  };

  element.addEventListener('pointerdown', onPointerDown);
  element.addEventListener('pointermove', onPointerMove);
  element.addEventListener('pointerup', onPointerEnd);
  element.addEventListener('pointercancel', onPointerEnd);
  window.addEventListener('scroll', onScroll, { capture: true, passive: true });
  // Suppression sits at DOCUMENT capture: the trailing synthetic click must
  // die before overlay outside-click dispatchers (body capture) can treat
  // it as an outside press and close the menu the long-press just opened.
  document.addEventListener('click', suppress, true);
  document.addEventListener('contextmenu', suppress, true);

  return () => {
    cancel();
    disarm();
    element.removeEventListener('pointerdown', onPointerDown);
    element.removeEventListener('pointermove', onPointerMove);
    element.removeEventListener('pointerup', onPointerEnd);
    element.removeEventListener('pointercancel', onPointerEnd);
    window.removeEventListener('scroll', onScroll, { capture: true });
    document.removeEventListener('click', suppress, true);
    document.removeEventListener('contextmenu', suppress, true);
  };
}
