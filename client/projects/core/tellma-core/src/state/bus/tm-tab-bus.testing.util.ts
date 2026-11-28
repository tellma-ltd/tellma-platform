// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable } from '@angular/core';
import { Subject, type Observable } from 'rxjs';

import type { TmBusMessage } from './tm-bus-messages';
import { TmTabBus } from './tm-tab-bus';

// A tab bus for the specs with no other tabs: it records the posts, and
// delivers what the spec plays as another tab's message.

/** A tab bus that records its posts. */
@Injectable()
export class TmRecordingTabBus extends TmTabBus {
  /** This tab's id. */
  override readonly tabId: string = 'spec-tab';

  readonly #received = new Subject<TmBusMessage>();

  /** The messages the spec plays as other tabs' posts. */
  override readonly messages: Observable<TmBusMessage> = this.#received.asObservable();

  /** Resolves when the spec opens the bus: at once unless the spec holds it. */
  override ɵopened: Promise<void> = Promise.resolve();

  /** The posts, oldest first. */
  readonly posted: TmBusMessage[] = [];

  /** Records a post. */
  override post(message: TmBusMessage): void {
    this.posted.push(message);
  }

  /** Delivers a message as another tab's post. */
  receive(message: TmBusMessage): void {
    this.#received.next(message);
  }
}
