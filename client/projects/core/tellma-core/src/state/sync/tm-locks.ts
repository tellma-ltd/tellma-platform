// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT, inject, Injectable } from '@angular/core';

// The browser's exclusive locks (Web Locks), behind one class, so the testing
// package replaces them with in-memory ones. A request is queued, never
// `ifAvailable`, so no wake-up is lost (spec 0025 §5.3). A page without Web
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
}
