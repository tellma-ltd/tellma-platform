// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, Injectable, signal, type Signal } from '@angular/core';

import { TM_NOW } from '@tellma/core-ui';

/** One minute, in milliseconds. */
const MINUTE_MS = 60_000;

/**
 * The one clock every `tm-relative-time` reads: a signal that ticks on
 * each minute boundary of `TM_NOW`, so a page of relative times re-renders
 * together, once a minute, from a single timer.
 *
 * It runs only while something subscribes and the page is visible. While
 * the page is hidden it schedules nothing; when the page shows again it
 * ticks at once, so every text catches up, and re-aligns to the next
 * boundary.
 */
@Injectable({ providedIn: 'root' })
export class TmMinuteTicker {
  private readonly document = inject(DOCUMENT);
  private readonly now = inject(TM_NOW);

  /** The tick count; a read makes the reader re-run on every tick. */
  private readonly count = signal(0);

  /** How many instances currently read the ticker. */
  private subscribers = 0;

  /** The pending boundary timer, or `null` while none is scheduled. */
  private timer: ReturnType<typeof setTimeout> | null = null;

  /** Ticks at once on a visit, and stops the timer while hidden. */
  private readonly onVisibilityChange = (): void => {
    if (this.document.visibilityState === 'hidden') {
      this.disarm();
      return;
    }
    this.count.update((value) => value + 1);
    this.arm();
  };

  /** The tick signal: read it in a reactive context to re-run on each minute. */
  readonly tick: Signal<number> = this.count.asReadonly();

  /**
   * Starts the ticker for one more reader (the first starts the timer and
   * the visibility listener) and returns the function that stops it for
   * that reader (the last stops both).
   */
  subscribe(): () => void {
    this.subscribers++;
    if (this.subscribers === 1) {
      this.document.addEventListener('visibilitychange', this.onVisibilityChange);
      this.arm();
    }
    let subscribed = true;
    return () => {
      if (!subscribed) {
        return;
      }
      subscribed = false;
      this.subscribers--;
      if (this.subscribers === 0) {
        this.document.removeEventListener('visibilitychange', this.onVisibilityChange);
        this.disarm();
      }
    };
  }

  /** Schedules the next tick on the next minute boundary, unless hidden. */
  private arm(): void {
    this.disarm();
    if (this.document.visibilityState === 'hidden') {
      return;
    }
    const delay = MINUTE_MS - (this.now() % MINUTE_MS);
    this.timer = setTimeout(() => {
      this.timer = null;
      this.count.update((value) => value + 1);
      this.arm();
    }, delay);
  }

  /** Cancels the pending tick. */
  private disarm(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
