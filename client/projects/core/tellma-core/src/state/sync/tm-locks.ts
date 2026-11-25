// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, Injectable } from '@angular/core';

// The browser's exclusive locks (Web Locks), behind one class, so the testing
// package replaces them with in-memory ones. A sync request is queued, never
// `ifAvailable`, so no wake-up is lost (spec 0025 §5.3); a one-tab-only
// prompt, as the device-zone prompt's (spec 0024 §7.8), takes its lock only
// if no tab holds it, and otherwise gives way to that tab. A page without Web
// Locks, such as one served over plain HTTP from another machine, queues
// within the tab instead: it then shares no lock with its other tabs, which
// costs at most a duplicate sync, never a wrong document.

/**
 * Runs work under a named exclusive lock, queued behind every earlier
 * request of the same name in the browser. Internal.
 */
@Injectable({ providedIn: 'root', useFactory: () => new TmWebLocks() })
export abstract class TmLocks {
  /**
   * Runs work holding the named lock, which the work keeps until it settles.
   *
   * @param name - The lock's name, such as `tm:acme:0b4f:17:sync`.
   * @param work - The work; it starts once the lock is granted.
   * @returns What the work settles with.
   */
  abstract run<T>(name: string, work: () => Promise<T>): Promise<T>;

  /**
   * Runs work holding the named lock if no one holds or awaits it now, and
   * otherwise runs nothing.
   *
   * @param name - The lock's name, such as `tm:acme:0b4f:zone-prompt`.
   * @param work - The work; it starts at once when the lock is free, and keeps the lock until it settles.
   * @returns Whether the work ran: `true` once it settled, `false` at once
   *   when the lock was taken; it rejects with what the work rejects with.
   */
  abstract tryRun(name: string, work: () => Promise<void>): Promise<boolean>;
}

/** The browser's Web Locks, or a queue within the tab where the page has none. */
class TmWebLocks extends TmLocks {
  readonly #locks = inject(DOCUMENT).defaultView?.navigator.locks;
  /** The last request of each name, in the tab's own queue. */
  readonly #tails = new Map<string, Promise<unknown>>();

  /** Runs work holding the named lock. */
  override run<T>(name: string, work: () => Promise<T>): Promise<T> {
    if (this.#locks) {
      return this.#locks.request(name, work);
    }
    // Each request waits for the one before it, however that one settled.
    const done = (this.#tails.get(name) ?? Promise.resolve()).then(work, work);
    const tail = done.catch(() => undefined);
    this.#tails.set(name, tail);
    void tail.then(() => this.#tails.get(name) === tail && this.#tails.delete(name));
    return done;
  }

  /** Runs work holding the named lock if it is free now. */
  override tryRun(name: string, work: () => Promise<void>): Promise<boolean> {
    if (this.#locks) {
      // The browser grants `null` when another holder, in any tab, has the lock.
      return this.#locks.request(name, { ifAvailable: true }, (lock) => (lock === null ? false : work().then(() => true)));
    }
    // Within the tab, the lock is free when no request of the name is unsettled.
    return this.#tails.has(name) ? Promise.resolve(false) : this.run(name, work).then(() => true);
  }
}
