// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, inject, Injectable, signal, type Signal } from '@angular/core';

import { TM_API_HOST } from '../api/tm-api-host';

// The input-idle clock (spec 0028 §9.3): one per tab, so everything that
// would move the page under the user's hands (the banner row opening or
// closing, the time-zone prompt of spec 0024 §7.8) waits for the same quiet
// moment. Passive capture listeners on the window see every key, pointer,
// wheel and touch before any handler can stop it, and never delay
// scrolling. The handler only stamps the time; one timer re-arms itself
// for whatever is left of the 2 s, so a stream of pointer moves costs no
// timer churn. The stamps are the page's monotonic clock, never the wall
// clock: a clock set back (by hand, by a time sync, or on waking a
// suspended machine) would otherwise hold the tab busy for as long as it
// moved.

/** How long without input makes the tab idle, in milliseconds. */
export const TM_INPUT_IDLE_MS = 2000;

/** The inputs that end an idle moment. */
const INPUTS = ['keydown', 'pointerdown', 'pointermove', 'wheel', 'touchstart'] as const;

/**
 * The tab's input-idle clock: idle once 2 s have passed without a key,
 * pointer, wheel or touch input on the window. A root service; its
 * listeners run once per tab. The clock starts when the service is first
 * created, so the tab is idle 2 s later unless input comes first.
 *
 * @example
 * ```ts
 * // Open a prompt only when the user's hands are off the keyboard and pointer.
 * await inject(TmInputIdle).whenIdle();
 * dialog.open();
 * ```
 */
@Injectable({ providedIn: 'root' })
export class TmInputIdle {
  readonly #idle = signal(false);
  // When the last input came, or the clock started, on the monotonic clock.
  #last = performance.now();
  // The pending check, while the tab is not idle.
  #timer: ReturnType<typeof setTimeout> | undefined;
  // What the waiters of the next idle moment resolve, while any wait.
  #wake: (() => void) | null = null;
  #next: Promise<void> | null = null;

  /** Whether 2 s have passed without input. */
  readonly idle: Signal<boolean> = this.#idle.asReadonly();

  /** Creates the clock and listens to the window's input, until the app ends. */
  constructor() {
    const host = inject(TM_API_HOST);
    const input = (): void => this.#input();
    const stops = INPUTS.map((type) => host.listen('window', type, input, { capture: true, passive: true }));
    this.#arm(TM_INPUT_IDLE_MS);
    inject(DestroyRef).onDestroy(() => {
      stops.forEach((stop) => stop());
      clearTimeout(this.#timer);
    });
  }

  /**
   * Waits for an idle moment.
   *
   * @returns A promise that resolves at once while the tab is idle, else when it next becomes idle.
   */
  whenIdle(): Promise<void> {
    if (this.#idle()) {
      return Promise.resolve();
    }
    this.#next ??= new Promise<void>((resolve) => (this.#wake = resolve));
    return this.#next;
  }

  /** One input: the idle moment ends, and the check runs 2 s after the last input. */
  #input(): void {
    this.#last = performance.now();
    if (this.#idle()) {
      this.#idle.set(false);
    }
    if (this.#timer === undefined) {
      this.#arm(TM_INPUT_IDLE_MS);
    }
  }

  /** Arms the check. */
  #arm(ms: number): void {
    this.#timer = setTimeout(() => this.#check(), ms);
  }

  /** The tab turns idle when 2 s have passed since the last input; otherwise the check waits out the rest. */
  #check(): void {
    const left = this.#last + TM_INPUT_IDLE_MS - performance.now();
    if (left > 0) {
      this.#arm(left);
      return;
    }
    this.#timer = undefined;
    this.#idle.set(true);
    const wake = this.#wake;
    this.#wake = null;
    this.#next = null;
    wake?.();
  }
}
