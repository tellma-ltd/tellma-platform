// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TM_CLIENT_METADATA, TmClientMetadataStore, type TmClientMetadata } from '../../metadata/tm-client-metadata';
import { tmProfileCookies } from '../../session/tm-profile.testing.util';
import { TmFakeSessionHost, tmSessionTestingProviders } from '../../session/tm-session.testing.util';
import type { TmBusMessage } from './tm-bus-messages';
import { TmBroadcastTabBus, TmTabBus } from './tm-tab-bus';

// The tab bus over the browser's real BroadcastChannels, with other tabs
// played by channels the spec opens on the same names.

const FARAH = '0b4f';
const CORE = { distribution: { slug: 'acme' } } as unknown as TmClientMetadata;
const STORE_CHANGED: TmBusMessage = { type: 'store.changed', partition: 'u:0b4f/', names: ['tenants@1'] };
const SESSION_STARTED: TmBusMessage = { type: 'session.started', sub: FARAH };

/** Lets the channels deliver what is in flight. */
function settle(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 50));
}

/** Another tab's end of one channel: what it heard, and a way to post. */
class OtherTab {
  readonly heard: unknown[] = [];
  readonly #channel: BroadcastChannel;

  constructor(name: string) {
    this.#channel = new BroadcastChannel(name);
    this.#channel.onmessage = (event: MessageEvent<unknown>) => this.heard.push(event.data);
  }

  post(envelope: unknown): void {
    this.#channel.postMessage(envelope);
  }

  close(): void {
    this.#channel.close();
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
  function setUp(options: { cookies?: string; loaded?: boolean } = {}): { bus: TmTabBus; received: TmBusMessage[] } {
    const host = new TmFakeSessionHost();
    host.cookieJar = options.cookies ?? tmProfileCookies(FARAH);
    TestBed.configureTestingModule({
      providers: [
        ...tmSessionTestingProviders(host, null, 0),
        ...(options.loaded === false ? [] : [{ provide: TM_CLIENT_METADATA, useValue: CORE }]),
      ],
    });
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
    await settle();
    expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]);
    expect(user.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
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
    await settle();
    expect(received).toEqual([SESSION_STARTED, STORE_CHANGED]);
  });

  it('ignores an unknown envelope version, an unknown type and a malformed envelope', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    user.post({ v: 2, from: 'tab-b', message: STORE_CHANGED });
    user.post({ v: 1, from: 'tab-b', message: { type: 'pins.changed' } });
    user.post({ v: 1, from: 'tab-b', message: null });
    user.post({ v: 1, message: STORE_CHANGED });
    user.post('store.changed');
    await settle();
    expect(received).toEqual([]);
  });

  it('opens no user channel while no one is signed in', async () => {
    const { bus, received } = setUp({ cookies: '' });
    await bus.ɵopened;
    bus.post(STORE_CHANGED);
    bus.post(SESSION_STARTED);
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    await settle();
    expect(user.heard).toEqual([]);
    expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]);
    expect(received).toEqual([]);
  });

  it('holds the posts made before the Core chunk loads, and sends them in order once it does', async () => {
    const { bus } = setUp({ loaded: false });
    bus.post(STORE_CHANGED);
    bus.post(SESSION_STARTED);
    await settle();
    expect([device.heard, user.heard]).toEqual([[], []]);
    TestBed.inject(TmClientMetadataStore).set(CORE);
    await bus.ɵopened;
    await settle();
    expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]);
    expect(user.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
  });

  it('closes its channels while the page sits in the back-forward cache, and opens them when it comes back', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    // A page hidden without entering the cache keeps its channels.
    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    await settle();
    expect(received).toHaveLength(1);

    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    device.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    await settle();
    expect(received).toHaveLength(1);

    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    device.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    bus.post(STORE_CHANGED);
    await settle();
    expect(received).toEqual([STORE_CHANGED, STORE_CHANGED, SESSION_STARTED]);
    expect(user.heard).toContainEqual({ v: 1, from: bus.tabId, message: STORE_CHANGED });
  });

  it('closes its channels when the app ends', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    TestBed.resetTestingModule();
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    bus.post(SESSION_STARTED);
    await settle();
    expect([received, device.heard]).toEqual([[], []]);
  });
});
