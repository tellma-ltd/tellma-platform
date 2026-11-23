// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, inject, Injectable, signal, type Signal } from '@angular/core';

// The shell frame's seams (spec 0027 §5.1). Both shells are a fixed-height
// grid of rows around `main`, whose heights are root CSS variables in
// @tellma/core/styles.css (tm-frame.css): the shell writes them, and pages
// and the toast host read `--tm-shell-top`. TM_SHELL_LAYOUT mirrors the row
// heights for code that measures. Immersive mode takes every row away by an
// attribute on the root element, which the frame stylesheet's rule turns into
// zero heights over whatever the shell wrote.

/** The shell's row heights in CSS pixels, as the frame stylesheet sets them: the app bar, the banner row and the pinned bar. */
export const TM_SHELL_LAYOUT = Object.freeze({ appBar: 40, banner: 32, pinnedBar: 32 } as const);

/** The root element's attribute that makes the frame immersive. */
const IMMERSIVE = 'data-tm-immersive';

/**
 * The shell frame's state that pages change: immersive mode, which takes
 * the shell's rows away (the app bar, the banner row and the pinned bar),
 * so `--tm-shell-top` and the toast offset fall to the page's top and no
 * `header` renders. Banners wait, and show when it ends. A root service.
 *
 * @example
 * ```ts
 * // Presentation mode: the page fills the window until it is destroyed.
 * inject(TmShell).setImmersive(true, inject(DestroyRef));
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmShell {
  readonly #root = inject(DOCUMENT).documentElement;
  // Who holds the frame immersive, each with the release of its destroy hook.
  readonly #holders = new Map<DestroyRef, () => void>();
  readonly #immersive = signal(false);

  /** Whether the frame is immersive: some caller asked for it, and its `DestroyRef` has not ended. */
  readonly immersive: Signal<boolean> = this.#immersive.asReadonly();

  /** Creates the service; the app's end restores the frame. */
  constructor() {
    inject(DestroyRef).onDestroy(() => this.#root.removeAttribute(IMMERSIVE));
  }

  /**
   * Makes the frame immersive, or restores its rows. The frame stays
   * immersive while any caller holds it, until that caller passes `false`
   * with the same `destroyRef` or the `destroyRef` ends.
   *
   * @param on - `true` to take the rows away, `false` to give them back.
   * @param destroyRef - The caller's lifetime, which also ends its hold.
   */
  setImmersive(on: boolean, destroyRef: DestroyRef): void {
    if (on === this.#holders.has(destroyRef)) {
      return;
    }
    if (on) {
      this.#holders.set(destroyRef, destroyRef.onDestroy(() => this.#release(destroyRef)));
      this.#apply();
    } else {
      // Unhook the destroy callback, which has nothing left to release.
      this.#holders.get(destroyRef)?.();
      this.#release(destroyRef);
    }
  }

  /** Ends one caller's hold. */
  #release(destroyRef: DestroyRef): void {
    this.#holders.delete(destroyRef);
    this.#apply();
  }

  /** Shows the holders' state: in the caller's frame, the attribute moves the CSS variables at once. */
  #apply(): void {
    const immersive = this.#holders.size > 0;
    this.#root.toggleAttribute(IMMERSIVE, immersive);
    this.#immersive.set(immersive);
  }
}
