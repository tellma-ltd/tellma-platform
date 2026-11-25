// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TmApi } from '../../api/tm-api';
import { TM_CLIENT_METADATA, TmClientMetadataStore, type TmClientMetadata } from '../../metadata/tm-client-metadata';
import { tmWaitUntil } from '../../routes/tm-app.testing.util';
import { tmParseProfileCookie, type TmProfile } from '../../session/tm-profile';
import { tmProfileCookies } from '../../session/tm-profile.testing.util';
import { TmSession } from '../../session/tm-session';
import { TmFakeSessionHost, tmSessionTestingProviders } from '../../session/tm-session.testing.util';
import type { TmBusMessage } from './tm-bus-messages';
import { TmBroadcastTabBus, TmTabBus } from './tm-tab-bus';

// The tab bus over the browser's real BroadcastChannels, with other tabs
// played by channels the spec opens on the same names.
//
// Chromium carries every post through the browser process, even between two
// ends in one page. Within a page, a post reaches every end open when it is
// made, in the order of the posts, but how long it takes depends on how busy
// the browser process is: with it busy, a delivery takes seconds while the
// page itself stays responsive. So the spec never gives a delivery a fixed
// time (vi.waitFor's 1 s), and never concludes from a quiet channel that
// nothing was sent. It waits on the delivery itself, a drain mark heard,
// bounded only by the shared wait deadline, which names what it waited for.

const FARAH = '0b4f';
const SARA = '7c1e';
const CORE = { distribution: { slug: 'acme' } } as unknown as TmClientMetadata;
const STORE_CHANGED: TmBusMessage = { type: 'store.changed', partition: 'u:0b4f/', names: ['tenants@1'] };
const SESSION_STARTED: TmBusMessage = { type: 'session.started', sub: FARAH };

/** One real task, the step of every wait: the channels deliver between tasks. */
function task(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve));
}

/**
 * Waits, a real task at a time, until a condition holds. It fails after the
 * shared wait deadline with an error naming the condition.
 *
 * @param condition - The state the spec waits for.
 * @param description - What the condition waits for, in the error.
 */
function until(condition: () => boolean, description: string): Promise<void> {
  return tmWaitUntil(condition, task, description);
}

/** What a drain posts: no envelope, so the bus ignores it. */
interface DrainMark {
  /** The mark's number, new at every drain. */
  readonly drain: number;
}

/** Whether a value a channel delivered is a drain mark. */
function isMark(value: unknown): value is DrainMark {
  return typeof value === 'object' && value !== null && typeof (value as Partial<DrainMark>).drain === 'number';
}

/** The number of the last drain mark posted, so that no two marks are alike. */
let lastMark = 0;

/**
 * Another tab's end of one channel: what it heard, a way to post, and a way
 * to wait until everything posted on the channel so far has been delivered.
 */
class OtherTab {
  /** The tabs open now, closed after each test. */
  static readonly #tabs = new Set<OtherTab>();

  /** Every value the channel delivered to this tab, oldest first; the drain marks are left out. */
  readonly heard: unknown[] = [];

  readonly #name: string;
  readonly #channel: BroadcastChannel;
  /** A second end of the same channel: it hears this tab's own posts, which the tab itself never does. */
  readonly #witness: BroadcastChannel;
  /** The drain marks the witness heard. */
  readonly #witnessed = new Set<number>();

  /**
   * Opens the channel's two ends.
   *
   * @param name - The channel's name.
   */
  private constructor(name: string) {
    this.#name = name;
    this.#channel = new BroadcastChannel(name);
    this.#channel.onmessage = (event: MessageEvent<unknown>) => {
      if (!isMark(event.data)) {
        this.heard.push(event.data);
      }
    };
    this.#witness = new BroadcastChannel(name);
    this.#witness.onmessage = (event: MessageEvent<unknown>) => {
      if (isMark(event.data)) {
        this.#witnessed.add(event.data.drain);
      }
    };
    OtherTab.#tabs.add(this);
  }

  /**
   * Opens another tab's end of a channel, and waits until the channel
   * delivers to it: from then on, it hears every later post on the channel.
   *
   * @param name - The channel's name.
   * @returns The tab.
   */
  static async open(name: string): Promise<OtherTab> {
    const tab = new OtherTab(name);
    await tab.drain();
    return tab;
  }

  /** Closes every tab still open. */
  static closeAll(): void {
    for (const tab of OtherTab.#tabs) {
      tab.close();
    }
  }

  /**
   * Posts a value on the channel, as another tab would.
   *
   * @param envelope - The value.
   */
  post(envelope: unknown): void {
    this.#channel.postMessage(envelope);
  }

  /**
   * Waits until the channel delivered everything this page posted on it so
   * far, to every end of it open in this page. The tab posts a mark last,
   * and waits for its witness to hear it: a page's posts on a channel are
   * delivered to its ends one after another, in the order they were posted,
   * so once the witness hears the mark, every end heard what came before it.
   */
  async drain(): Promise<void> {
    const mark = ++lastMark;
    this.#channel.postMessage({ drain: mark } satisfies DrainMark);
    await until(() => this.#witnessed.has(mark), `the channel ${this.#name} delivered drain mark ${String(mark)}`);
  }

  /** Closes the channel's two ends. */
  close(): void {
    this.#channel.close();
    this.#witness.close();
    OtherTab.#tabs.delete(this);
  }
}

describe('TmTabBus', () => {
  let device: OtherTab;
  let user: OtherTab;

  beforeEach(async () => {
    // Opened, and heard from, before the bus posts anything.
    device = await OtherTab.open('tm:acme');
    user = await OtherTab.open(`tm:acme:${FARAH}`);
  });

  afterEach(() => {
    OtherTab.closeAll();
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
    await device.drain();
    await user.drain();
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
    await device.drain();
    await user.drain();
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
    await user.drain();
    expect(received).toEqual([]);
  });

  it('opens no user channel while no one is signed in', async () => {
    const { bus, received } = setUp({ cookies: '' });
    await bus.ɵopened;
    bus.post(STORE_CHANGED);
    bus.post(SESSION_STARTED);
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    await device.drain();
    await user.drain();
    expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]);
    expect(user.heard).toEqual([]);
    expect(received).toEqual([]);
  });

  it('holds the posts made before the Core chunk loads, and sends them in order once it does', async () => {
    const { bus } = setUp({ loaded: false });
    bus.post(STORE_CHANGED);
    bus.post(SESSION_STARTED);
    // Drained, not waited out: nothing the page posted so far reached either channel.
    await device.drain();
    await user.drain();
    expect([device.heard, user.heard]).toEqual([[], []]);
    TestBed.inject(TmClientMetadataStore).set(CORE);
    await bus.ɵopened;
    await device.drain();
    await user.drain();
    expect(device.heard).toEqual([{ v: 1, from: bus.tabId, message: SESSION_STARTED }]);
    expect(user.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
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
    const sara = await OtherTab.open(`tm:acme:${SARA}`);
    profile.set(tmParseProfileCookie(tmProfileCookies(SARA)));
    TestBed.tick();
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    sara.post({ v: 1, from: 'tab-c', message: STORE_CHANGED });
    bus.post(STORE_CHANGED);
    await user.drain();
    await sara.drain();
    expect(received).toEqual([STORE_CHANGED]);
    expect(sara.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
    expect(user.heard).toEqual([]);

    // Signed out: no user channel at all.
    profile.set(null);
    TestBed.tick();
    sara.post({ v: 1, from: 'tab-c', message: STORE_CHANGED });
    bus.post(STORE_CHANGED);
    await sara.drain();
    expect(received).toHaveLength(1);
    expect(sara.heard).toHaveLength(1);
  });

  it('closes its channels while the page sits in the back-forward cache, and opens them when it comes back', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    // A page hidden without entering the cache keeps its channels.
    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    await user.drain();
    expect(received).toHaveLength(1);

    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    device.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    await user.drain();
    await device.drain();
    expect(received).toHaveLength(1);

    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    device.post({ v: 1, from: 'tab-b', message: SESSION_STARTED });
    bus.post(STORE_CHANGED);
    await user.drain();
    await device.drain();
    expect(received).toHaveLength(3);
    expect(user.heard).toEqual([{ v: 1, from: bus.tabId, message: STORE_CHANGED }]);
    // The two channels deliver in no set order between them.
    expect(received.slice(1)).toEqual(expect.arrayContaining([STORE_CHANGED, SESSION_STARTED]));
  });

  it('closes its channels when the app ends', async () => {
    const { bus, received } = setUp();
    await bus.ɵopened;
    TestBed.resetTestingModule();
    user.post({ v: 1, from: 'tab-b', message: STORE_CHANGED });
    bus.post(SESSION_STARTED);
    await user.drain();
    await device.drain();
    expect([received, device.heard]).toEqual([[], []]);
  });
});
