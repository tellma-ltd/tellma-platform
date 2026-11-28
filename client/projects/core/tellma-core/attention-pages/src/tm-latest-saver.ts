// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Signal, signal } from '@angular/core';

// The saving rule of a control that saves itself (spec 0028 §10.3): a change
// asks for a save after a delay, a later change restarts the wait, and one
// save is in flight at a time; the latest value wanted follows it. A failed
// save keeps the value wanted, so Try again sends it. Leaving sends what is
// still waiting, since a write is never aborted; the page hiding (a closed
// tab, a reload, an external link) hands it to the beacon, a keepalive send
// whose answer is never read.

/** Where a self-saving control's saves stand. Internal. */
export type TmSaveState = 'idle' | 'pending' | 'saving' | 'saved' | 'failed';

/**
 * How a save ended: `saved`, or `reset` when the control reloaded its
 * values instead, so nothing is left to save or to report.
 */
export type TmSaveOutcome = 'saved' | 'reset';

/**
 * Saves the latest value a control wants, one save at a time, each after the
 * wait its change asked for. Internal.
 *
 * @example
 * ```ts
 * const saver = new TmLatestSaver<boolean>(
 *   (value) => api.write('users/preferences/set', body(value)).then(() => 'saved'),
 *   (value) => api.send('users/preferences/set', body(value)),
 * );
 * saver.want(true, 600);
 * ```
 */
export class TmLatestSaver<T> {
  /** Where the saves stand. */
  readonly #state = signal<TmSaveState>('idle');

  /** The value to save next; `undefined` with nothing waiting. */
  #wanted: T | undefined;

  /** The wait before the next save. */
  #timer: ReturnType<typeof setTimeout> | null = null;

  /** Whether a save is in flight. */
  #flying = false;

  /** Whether the wait ended while a save was in flight, so the next save follows it at once. */
  #due = false;

  /** Whether leaving sent what was waiting; nothing is asked for after that. */
  #disposed = false;

  /**
   * Creates the saver.
   *
   * @param send - Sends one value; it rejects when the save failed.
   * @param beacon - Sends one value as the page hides, its answer never read.
   */
  constructor(
    private readonly send: (value: T) => Promise<TmSaveOutcome>,
    private readonly beacon: (value: T) => void,
  ) {}

  /** Where the saves stand: `pending` while a change waits, `saving` while one is in flight. */
  readonly state: Signal<TmSaveState> = this.#state.asReadonly();

  /**
   * Asks for a value to be saved once the wait ends; a later call restarts
   * the wait with its own value.
   *
   * @param value - The value.
   * @param waitMs - The wait, in milliseconds; 0 saves at once.
   */
  want(value: T, waitMs: number): void {
    if (this.#disposed) {
      return;
    }
    this.#wanted = value;
    if (this.#timer !== null) {
      clearTimeout(this.#timer);
    }
    if (!this.#flying) {
      this.#state.set('pending');
    }
    this.#timer = setTimeout(() => {
      this.#timer = null;
      this.#run();
    }, waitMs);
  }

  /** Sends what a failed save left waiting, at once: Try again. */
  retry(): void {
    if (this.#state() === 'failed') {
      this.#run();
    }
  }

  /** Sends what is still waiting at once, and asks for nothing more: the control is leaving. */
  dispose(): void {
    if (this.#timer !== null) {
      clearTimeout(this.#timer);
      this.#timer = null;
      this.#run();
    }
    this.#disposed = true;
  }

  /**
   * Hands the beacon a value still waiting, out its delay or behind a
   * flight, at once: the page is hiding. A failed value waits for Try again,
   * as before; a page restored from the back-forward cache saves on as usual.
   */
  hide(): void {
    if (this.#timer === null && !this.#due) {
      return;
    }
    if (this.#timer !== null) {
      clearTimeout(this.#timer);
      this.#timer = null;
    }
    this.#due = false;
    const value = this.#wanted;
    this.#wanted = undefined;
    if (value !== undefined) {
      this.beacon(value);
    }
    if (!this.#flying) {
      // The beacon's answer is never read: nothing is left saving.
      this.#state.set('idle');
    }
  }

  /** Sends the value wanted, unless one is in flight, which it then follows. */
  #run(): void {
    if (this.#flying) {
      this.#due = true;
      return;
    }
    const value = this.#wanted;
    if (value === undefined) {
      return;
    }
    this.#wanted = undefined;
    this.#flying = true;
    this.#due = false;
    this.#state.set('saving');
    this.send(value).then(
      (outcome) => this.#settled(outcome === 'reset' ? 'reset' : 'saved'),
      () => {
        // A newer value wins; else the failed one waits for Try again.
        this.#wanted ??= value;
        this.#settled('failed');
      },
    );
  }

  /** After a save: the latest value follows it, else the state says how it ended. */
  #settled(outcome: TmSaveOutcome | 'failed'): void {
    this.#flying = false;
    if (outcome === 'reset') {
      // The control reloaded its values: what waited belongs to the set it replaced.
      this.#wanted = undefined;
      if (this.#timer !== null) {
        clearTimeout(this.#timer);
        this.#timer = null;
      }
      this.#state.set('idle');
      return;
    }
    if (this.#timer !== null) {
      // A newer change is still waiting out its delay.
      this.#state.set('pending');
    } else if (this.#due || (outcome === 'saved' && this.#wanted !== undefined)) {
      this.#run();
    } else {
      this.#state.set(outcome);
    }
  }
}
