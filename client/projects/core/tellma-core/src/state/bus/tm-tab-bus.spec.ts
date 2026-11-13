// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TmApi } from '../../api/tm-api';
import { TM_CLIENT_METADATA, TmClientMetadataStore, type TmClientMetadata } from '../../metadata/tm-client-metadata';
import { tmParseProfileCookie, type TmProfile } from '../../session/tm-profile';
import { tmProfileCookies } from '../../session/tm-profile.testing.util';
import { TmSession } from '../../session/tm-session';
import { TmFakeSessionHost, tmSessionTestingProviders } from '../../session/tm-session.testing.util';
import type { TmBusMessage } from './tm-bus-messages';
import { TmBroadcastTabBus, TmTabBus } from './tm-tab-bus';

// The tab bus over the browser's real BroadcastChannels, with other tabs
// played by channels the spec opens on the same names.

const FARAH = '0b4f';
const SARA = '7c1e';
const CORE = { distribution: { slug: 'acme' } } as unknown as TmClientMetadata;
const STORE_CHANGED: TmBusMessage = { type: 'store.changed', partition: 'u:0b4f/', names: ['tenants@1'] };
const SESSION_STARTED: TmBusMessage = { type: 'session.started', sub: FARAH };

/** Waits a little: only where nothing can be in flight, since no channel is open yet. */
function pause(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 50));
}

/** Another tab's end of one channel: what it heard, and a way to post. */
class OtherTab {
  /** Every value the channel delivered to this tab, oldest first. */
  readonly heard: unknown[] = [];
  readonly #channel: BroadcastChannel;

  /**
   * Opens the channel.
   *
   * @param name - The channel's name.
   */
  constructor(name: string) {
    this.#channel = new BroadcastChannel(name);
    this.#channel.onmessage = (event: MessageEvent<unknown>) => this.heard.push(event.data);
  }

  /**
   * Posts a value on the channel, as another tab would.
   *
   * @param envelope - The value.
   */
  post(envelope: unknown): void {
    this.#channel.postMessage(envelope);
  }

  /** Closes the channel. */
  close(): void {
    this.#channel.close();
  }
}

/**
 * Waits until a channel delivered everything another tab posted on it so far:
 * the tab posts a mark last, which the bus ignores, and a fresh listener hears
 * it. One sender's messages arrive in order.
 *
 * @param sender - The tab that posted.
 * @param name - The channel's name.
 */
async function drain(sender: OtherTab, name: string): Promise<void> {
  const listener = new OtherTab(name);
  try {
    sender.post('mark');
    await vi.waitFor(() => expect(listener.heard).toContain('mark'));
  } finally {
    listener.close();
  }
}

describe('TmTabBus', () => {
  let device: OtherTab;
  let user: OtherTab;

  beforeEach(() => {
    device = new OtherTab('tm:acme');
    user = new OtherTab(`tm:acme:${FARAH}`);
  });

  afterEach(() => {
    device.close();
    user.close();
    TestBed.resetTestingModule();
  });

  /** Configures the bus, signed in as Farah unless the cookies say otherwise, with the Core chunk loaded or not. */
  function setUp(options: { cookies?: string; loaded?: boolean; session?: Partial<TmSession> } = {}): {
    bus: TmTabBus;
    received: TmBusMessage[];
  } {
    const host = new TmFakeSessionHost();
    host.cookieJar = options.cookies ?? tmProfileCookies(FARAH);
    TestBed.configureTestingModule({
      providers: [
        ...tmSessionTestingProviders(host, null, 0),
        ...(options.session ? [{ provide: TmSession, useValue: options.session }] : []),
      ],
    });
    if (options.loaded !== false) {
      // The boot sequence loads the Core chunk.
      TestBed.inject(TmClientMetadataStore).set(CORE);
    }
    const bus = TestBed.inject(TmTabBus);
    const received: TmBusMessage[] = [];
    bus.messages.subscribe((message) => received.push(message));
    return { bus, received };
  }

  it('is the BroadcastChannel bus, with a new id at each load', () => {
    const { bus } = setUp();
    expect(bus).toBeInstanceOf(TmBroadcastTabBus);
    expect(bus.tabId).toMatch(/^[0-9a-f-]{36}$/);
  });

  it("posts the session messages on the device channel and the rest on the user's, in envelope 1", async () => {
    const { bus } = setUp();
    await bus.ɵopened;
    bus.post(SESSION_STARTED);
    bus.post(STORE_CHANGED);
    await vi.waitFor(() => {
      expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]);
      expect(user.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
    });
  });

  it("receives each channel's own kind of message, from other tabs only", async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    device.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    // The wrong channel for the type, and the tab's own post, are ignored.
    device.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    user.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    user.post({ v: 1, from: bus.tabId, message: STORE_CHANGED });
    await drain(device, 'tm:acme');
    await drain(user, `tm:acme:${FARAH}`);
    // The two channels deliver in no set order between them.
    expect(received).toHaveLength(2);
    expect(received).toEqual(expect.arrayContaining([SESSION_STARTED, STORE_CHANGED]));
  });

  it('ignores an unknown envelope version, an unknown type and a malformed envelope', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    user.post({ v: 2, from: 'tab-b', message: STORE_CHANGED });
    user.post({ v: 1, from: 'tab-b', message: { type: 'pins.changed' } });
    user.post({ v: 1, from: 'tab-b', message: null });
    user.post({ v: 1, message: STORE_CHANGED });
    user.post('store.changed');
    await drain(user, `tm:acme:${FARAH}`);
    expect(received).toEqual([]);
  });

  it('opens no user channel while no one is signed in', async () => {
    const { bus, received } = setUp({ cookies: '' });
    await bus.ɵopened;
    bus.post(STORE_CHANGED);
    bus.post(SESSION_STARTED);
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    await vi.waitFor(() => expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]));
    await drain(user, `tm:acme:${FARAH}`);
    expect(user.heard).toEqual([]);
    expect(received).toEqual([]);
  });

  it('holds the posts made before the Core chunk loads, and sends them in order once it does', async () => {
    const { bus } = setUp({ loaded: false });
    bus.post(STORE_CHANGED);
    bus.post(SESSION_STARTED);
    await pause();
    expect([device.heard, user.heard]).toEqual([[], []]);
    TestBed.inject(TmClientMetadataStore).set(CORE);
    await bus.ɵopened;
    await vi.waitFor(() => {
      expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]);
      expect(user.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
    });
  });

  it('leaves TM_CLIENT_METADATA working when it starts before the Core chunk loads', async () => {
    const { bus } = setUp({ loaded: false });
    TestBed.inject(TmClientMetadataStore).set(CORE);
    await bus.ɵopened;
    expect(TestBed.inject(TM_CLIENT_METADATA)).toBe(CORE);
    expect(TestBed.inject(TmApi)).toBeDefined();
  });

  it("replaces the user channel when the user changes, so no tab hears the previous user's messages", async () => {
    const profile = signal<TmProfile | null>(tmParseProfileCookie(tmProfileCookies(FARAH)));
    const { bus, received } = setUp({ session: { profile } });
    await bus.ɵopened;
    const sara = new OtherTab(`tm:acme:${SARA}`);
    try {
      profile.set(tmParseProfileCookie(tmProfileCookies(SARA)));
      TestBed.tick();
      user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
      sara.post({ v: 1, from: 'tab-c', message: STORE_CHANGED });
      bus.post(STORE_CHANGED);
      await vi.waitFor(() => {
        expect(received).toEqual([STORE_CHANGED]);
        expect(sara.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
      });
      await drain(user, `tm:acme:${FARAH}`);
      expect(received).toEqual([STORE_CHANGED]);
      expect(user.heard).toEqual([]);

      // Signed out: no user channel at all.
      profile.set(null);
      TestBed.tick();
      sara.post({ v: 1, from: 'tab-c', message: STORE_CHANGED });
      bus.post(STORE_CHANGED);
      await drain(sara, `tm:acme:${SARA}`);
      expect(received).toHaveLength(1);
      expect(sara.heard).toHaveLength(1);
    } finally {
      sara.close();
    }
  });

  it('closes its channels while the page sits in the back-forward cache, and opens them when it comes back', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    // A page hidden without entering the cache keeps its channels.
    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    await vi.waitFor(() => expect(received).toHaveLength(1));

    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    device.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    await drain(user, `tm:acme:${FARAH}`);
    await drain(device, 'tm:acme');
    expect(received).toHaveLength(1);

    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    device.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    bus.post(STORE_CHANGED);
    await vi.waitFor(() => {
      expect(received).toHaveLength(3);
      expect(user.heard).toContainEqual({ v: 1, from: bus.tabId, message: STORE_CHANGED });
    });
    // The two channels deliver in no set order between them.
    expect(received.slice(1)).toEqual(expect.arrayContaining([STORE_CHANGED, SESSION_STARTED]));
  });

  it('closes its channels when the app ends', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    TestBed.resetTestingModule();
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    bus.post(SESSION_STARTED);
    await drain(user, `tm:acme:${FARAH}`);
    expect([received, device.heard]).toEqual([[], []]);
  });
});
