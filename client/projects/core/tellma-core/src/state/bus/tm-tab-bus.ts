// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, effect, inject, Injectable, Injector, untracked } from '@angular/core';
import { Subject, type Observable } from 'rxjs';

import { TmClientMetadataStore } from '../../metadata/tm-client-metadata';
import { TmSession } from '../../session/tm-session';
import { TM_BUS_MESSAGE_TYPES, type TmBusEnvelope, type TmBusMessage } from './tm-bus-messages';

// The tab bus (spec 0025 §6.1). Two BroadcastChannels carry every cross-tab
// message of the app: the device channel `tm:<slug>` carries the session
// messages, which every user of the browser hears, and the user channel
// `tm:<slug>:<sub>` carries the rest, so one user's tabs never hear another
// user's documents or events. The slug comes from the Core chunk, which the
// boot sequence loads beside the bus's first users, so the channels open as
// soon as it arrives and posts made before then wait for them.

/** The most posts kept while the channels are not open yet. */
const MAX_QUEUED = 64;

/**
 * Carries messages between the tabs of this browser: what one tab posts,
 * every other tab of the same user receives (the session messages: every
 * other tab of the browser), and no tab hears its own posts.
 *
 * @example
 * ```ts
 * const bus = inject(TmTabBus);
 * bus.post({ type: 'leader.probe', tenantId: 17 });
 * bus.messages.subscribe((message) => { … });
 * ```
 */
@Injectable({ providedIn: 'root', useFactory: () => new TmBroadcastTabBus() })
export abstract class TmTabBus {
  /** This tab's id, new at every document load and never stored. */
  abstract readonly tabId: string;

  /** The messages other tabs post; never this tab's own. */
  abstract readonly messages: Observable<TmBusMessage>;

  /**
   * Resolves once the bus's channels are open, so a message another tab
   * posts from then on reaches this one. Internal: the client store reads
   * the stored documents only after it, so that it misses no change.
   */
  abstract readonly ɵopened: Promise<void>;

  /**
   * Posts a message to every other tab; this tab never receives it.
   *
   * @param message - The message.
   */
  abstract post(message: TmBusMessage): void;
}

/** Whether a value is a non-null object. */
function isObject(value: unknown): value is Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null;
}

/** Whether a message type travels on the device channel. */
function isSessionType(type: string): boolean {
  return type.startsWith('session.');
}

/** The tab bus over the browser's BroadcastChannels. Internal. */
export class TmBroadcastTabBus extends TmTabBus {
  /** This tab's id. */
  override readonly tabId: string = crypto.randomUUID();

  readonly #injector = inject(Injector);
  readonly #window = inject(DOCUMENT).defaultView;
  readonly #received = new Subject<TmBusMessage>();

  /** The messages other tabs post. */
  override readonly messages: Observable<TmBusMessage> = this.#received.asObservable();

  /** Resolves once the channels are open. */
  override readonly ɵopened: Promise<void>;

  /** The distribution's slug, once the Core chunk is loaded. */
  #slug: string | null = null;
  /** The user whose channel is open, or `null`. */
  #sub: string | null = null;
  #device: BroadcastChannel | null = null;
  #user: BroadcastChannel | null = null;
  /** Whether the page sits in the back-forward cache, with both channels closed. */
  #cached = false;
  /** The posts made before the channels opened, oldest first. */
  #queue: TmBusMessage[] = [];
  /** Whether the app ended, closing the bus for good. */
  #destroyed = false;

  /** Creates the bus; its channels open once the distribution's slug is known. */
  constructor() {
    super();
    this.ɵopened = this.#slugOf().then((slug) => this.#start(slug));

    // A page that receives a message while in the back-forward cache is
    // evicted from it, so the channels close when the page enters the cache
    // and open again when it comes back.
    const hide = (event: PageTransitionEvent): void => {
      if (event.persisted) {
        this.#cached = true;
        this.#close();
      }
    };
    const show = (event: PageTransitionEvent): void => {
      if (event.persisted && this.#cached) {
        this.#cached = false;
        this.#open();
      }
    };
    this.#window?.addEventListener('pagehide', hide);
    this.#window?.addEventListener('pageshow', show);
    // The app's end closes the channels for good.
    inject(DestroyRef).onDestroy(() => {
      this.#window?.removeEventListener('pagehide', hide);
      this.#window?.removeEventListener('pageshow', show);
      this.#destroyed = true;
      this.#close();
    });
  }

  /**
   * Posts a message to every other tab of the user, or, for a session
   * message, of the browser.
   *
   * @param message - The message.
   */
  override post(message: TmBusMessage): void {
    if (this.#destroyed) {
      return;
    }
    if (this.#slug === null) {
      // The channels are not open yet: the post waits for them.
      if (this.#queue.length < MAX_QUEUED) {
        this.#queue.push(message);
      }
      return;
    }
    const channel = isSessionType(message.type) ? this.#device : this.#user;
    const envelope: TmBusEnvelope = { v: 1, from: this.tabId, message };
    // With no user there is no user channel, and none of its tabs to tell.
    channel?.postMessage(envelope);
  }

  /** The distribution's slug, now when the Core chunk is loaded, else once it is. */
  #slugOf(): Promise<string> {
    // Never through TM_CLIENT_METADATA: injecting it before the chunk loads
    // throws, and a failed injection leaves the token failing for good.
    const store = this.#injector.get(TmClientMetadataStore);
    const core = store.peek();
    return core !== null ? Promise.resolve(core.distribution.slug) : store.loaded.then((loaded) => loaded.distribution.slug);
  }

  /** Opens the channels for the first time, then follows the signed-in user. */
  #start(slug: string): void {
    if (this.#destroyed) {
      return;
    }
    this.#slug = slug;
    if (!this.#cached) {
      this.#open();
    }
    // The posts made while the channels were not open, in order.
    const queued = this.#queue;
    this.#queue = [];
    for (const message of queued) {
      this.post(message);
    }
    // The user channel is replaced when the user changes. The session is
    // resolved here, not at construction, so the session's own paths may
    // inject the bus.
    const session = this.#injector.get(TmSession);
    effect(
      () => {
        const sub = session.profile()?.sub ?? null;
        untracked(() => this.#bindUser(sub));
      },
      { injector: this.#injector },
    );
  }

  /** Opens the device channel and the current user's channel. */
  #open(): void {
    if (this.#slug === null || this.#destroyed) {
      return;
    }
    this.#device ??= this.#channel(`tm:${this.#slug}`, true);
    const sub = untracked(() => this.#injector.get(TmSession).profile()?.sub ?? null);
    this.#sub = null;
    this.#bindUser(sub);
  }

  /** Closes both channels. */
  #close(): void {
    this.#device?.close();
    this.#user?.close();
    this.#device = this.#user = null;
    this.#sub = null;
  }

  /** Opens the channel of a user, closing another user's; nothing while the page is cached. */
  #bindUser(sub: string | null): void {
    if (this.#slug === null || this.#cached || this.#destroyed || (sub === this.#sub && (sub === null || this.#user !== null))) {
      return;
    }
    this.#user?.close();
    this.#user = sub === null ? null : this.#channel(`tm:${this.#slug}:${sub}`, false);
    this.#sub = sub;
  }

  /** Opens one channel, which delivers the messages of its own kind only. */
  #channel(name: string, session: boolean): BroadcastChannel {
    const channel = new BroadcastChannel(name);
    channel.onmessage = (event: MessageEvent<unknown>) => {
      const envelope = event.data;
      if (!isObject(envelope) || envelope['v'] !== 1 || typeof envelope['from'] !== 'string' || envelope['from'] === this.tabId) {
        return;
      }
      const message = envelope['message'];
      // A type this build does not know, or one on the other channel, is
      // ignored, so tabs of an older and a newer build run side by side.
      if (
        isObject(message) &&
        typeof message['type'] === 'string' &&
        TM_BUS_MESSAGE_TYPES.has(message['type']) &&
        isSessionType(message['type']) === session
      ) {
        this.#received.next(message as unknown as TmBusMessage);
      }
    };
    return channel;
  }
}
