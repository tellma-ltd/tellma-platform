// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, type Signal } from '@angular/core';
import { ɵTmLocks } from '@tellma/core';

// The browser's exclusive locks, kept in memory: each name is a queue of the
// test's own requests, so no lock outlives its test or reaches another test
// in the same page, and a test sees which locks are held.

/**
 * In-memory exclusive locks: each request of a name waits for the one
 * before it, however that one settled, as the browser's Web Locks queue
 * them. Part of {@link provideTellmaStateTesting}.
 *
 * @example
 * ```ts
 * const locks = TestBed.inject(TmFakeLocks);
 * await refresher.refresh(17, ['me'], 'event');
 * expect(locks.held()).toEqual([]);
 * ```
 */
@Injectable()
export class TmFakeLocks extends ɵTmLocks {
  /** The last request of each name. */
  readonly #tails = new Map<string, Promise<unknown>>();
  readonly #held = signal<readonly string[]>([]);

  /** The names of the locks held now, in the order they were granted. */
  readonly held: Signal<readonly string[]> = this.#held.asReadonly();

  /**
   * Runs work holding the named lock, once every earlier request of it has settled.
   *
   * @param name - The lock's name.
   * @param work - The work.
   * @returns What the work settles with.
   */
  override run<T>(name: string, work: () => Promise<T>): Promise<T> {
    const granted = async (): Promise<T> => {
      this.#held.update((held) => [...held, name]);
      try {
        return await work();
      } finally {
        this.#held.update((held) => held.filter((other) => other !== name));
      }
    };
    const done = (this.#tails.get(name) ?? Promise.resolve()).then(granted, granted);
    this.#tails.set(
      name,
      done.catch(() => undefined),
    );
    return done;
  }
}
