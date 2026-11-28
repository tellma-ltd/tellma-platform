// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, type Signal } from '@angular/core';
import { TmTabBus, type TmBusMessage } from '@tellma/core';
import { Subject, type Observable } from 'rxjs';

/**
 * A {@link TmTabBus} with no other tabs: it records what the code under test
 * posts, and delivers what the test plays as another tab's message.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
 * const bus = TestBed.inject(TmFakeTabBus);
 * bus.receive({ type: 'store.changed', partition: 'u:test-user/', names: ['tenants@1'] });
 * expect(bus.posted()).toEqual([]);
 * ```
 */
@Injectable()
export class TmFakeTabBus extends TmTabBus {
  /** This tab's id: `test-tab`. */
  override readonly tabId: string = 'test-tab';

  readonly #received = new Subject<TmBusMessage>();
  readonly #posted = signal<readonly TmBusMessage[]>([]);

  /** The messages the test plays as other tabs' posts. */
  override readonly messages: Observable<TmBusMessage> = this.#received.asObservable();

  /** Resolved from the start: the fake's channels are always open. */
  override readonly ɵopened: Promise<void> = Promise.resolve();

  /** Every message the code under test posted, oldest first. */
  readonly posted: Signal<readonly TmBusMessage[]> = this.#posted.asReadonly();

  /**
   * Records a message; no other tab receives it, and this one never does.
   *
   * @param message - The message.
   */
  override post(message: TmBusMessage): void {
    this.#posted.update((posted) => [...posted, message]);
  }

  /**
   * Delivers a message to this tab's subscribers, as if another tab posted it.
   *
   * @param message - The message.
   */
  receive(message: TmBusMessage): void {
    this.#received.next(message);
  }

  /** Forgets the recorded posts. */
  clear(): void {
    this.#posted.set([]);
  }
}
