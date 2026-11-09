// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The shared document-capture Escape coordinator. Transient layers that
 * dismiss on Escape AHEAD of the overlay keyboard machinery (menus,
 * tooltips) each used to install their own `document` capture listener —
 * but same-node capture listeners ALL run for one event, so a single
 * Escape dismissed several layers at once. One listener, one stack: only
 * the topmost (most recently pushed) layer acts, and the event is
 * consumed.
 *
 * The listener exists only while the stack is non-empty, so an idle app
 * carries no global keydown hook.
 */

const stack: Array<() => void> = [];
let listener: ((event: KeyboardEvent) => void) | null = null;

/**
 * Pushes a dismissal handler onto the shared Escape stack and returns its
 * idempotent release. Push on open or show, release on close or hide: the
 * most recently pushed layer is the one a single Escape dismisses, and the
 * key press is consumed (`preventDefault` and `stopPropagation`).
 *
 * Every surface that closes on Escape goes through this stack rather than
 * adding a `document` listener of its own: capture listeners on one node
 * all run for the same key press, so a second listener would let one
 * Escape close two layers at once.
 *
 * @example
 * ```ts
 * private release: (() => void) | null = null;
 *
 * open(): void {
 *   this.release ??= tmPushEscapeDismissal(() => this.close());
 * }
 *
 * close(): void {
 *   this.release?.();
 *   this.release = null;
 * }
 * ```
 */
export function tmPushEscapeDismissal(onEscape: () => void): () => void {
  if (listener === null) {
    listener = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape' || stack.length === 0) {
        return;
      }
      event.preventDefault();
      event.stopPropagation();
      stack[stack.length - 1]();
    };
    document.addEventListener('keydown', listener, true);
  }
  stack.push(onEscape);
  let released = false;
  return () => {
    if (released) {
      return;
    }
    released = true;
    const index = stack.lastIndexOf(onEscape);
    if (index !== -1) {
      stack.splice(index, 1);
    }
    if (stack.length === 0 && listener !== null) {
      document.removeEventListener('keydown', listener, true);
      listener = null;
    }
  };
}
