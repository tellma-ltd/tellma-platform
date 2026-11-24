// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { TmSession, type TmClientEvent, type TmHubConnection, type TmHubSink } from '@tellma/core';
import { provideTellmaStateTesting, provideTellmaTesting, TmFakeApi, TmFakeSession, TmFakeTelemetry } from '@tellma/core-testing';

import { TmHub } from './tm-hub';
import { tmCreateHub } from './tm-create-hub';

// The hub over the real SignalR client, with the network stubbed: fetch
// answers the negotiate, and a fake WebSocket the transport. The connection
// opens, hands on the server's events, reports a refusal, makes no request
// for a changed user, and never holds up a navigation or a stop.

/** The record separator that ends each message of the JSON hub protocol. */
const RS = '\u001e';

/** A negotiate answer that offers the WebSocket transport alone. */
const NEGOTIATED = JSON.stringify({
  negotiateVersion: 1,
  connectionId: 'c',
  connectionToken: 't',
  availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }],
});

/** A WebSocket the spec drives: it opens on its own unless told to hang, and answers the handshake. */
class TmFakeSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;
  /** Every socket opened, oldest first. */
  static sockets: TmFakeSocket[] = [];
  /** Whether new sockets never open. */
  static hang = false;

  readyState = TmFakeSocket.CONNECTING;
  binaryType = 'blob';
  readonly sent: string[] = [];
  onopen: ((event: Event) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  onmessage: ((event: { readonly data: string }) => void) | null = null;
  onclose: ((event: { readonly code: number; readonly reason: string; readonly wasClean: boolean }) => void) | null = null;

  constructor(readonly url: string) {
    TmFakeSocket.sockets.push(this);
    if (!TmFakeSocket.hang) {
      queueMicrotask(() => {
        this.readyState = TmFakeSocket.OPEN;
        this.onopen?.(new Event('open'));
      });
    }
  }

  send(data: string): void {
    this.sent.push(data);
    // The handshake's answer: no error.
    if (data.startsWith('{"protocol":"json"')) {
      queueMicrotask(() => this.receive(`{}${RS}`));
    }
  }

  close(): void {
    if (this.readyState !== TmFakeSocket.CLOSED) {
      this.readyState = TmFakeSocket.CLOSED;
      this.onclose?.({ code: 1000, reason: '', wasClean: true });
    }
  }

  /** A message from the server. */
  receive(data: string): void {
    this.onmessage?.({ data });
  }

  /** The server drops the connection. */
  drop(): void {
    this.readyState = TmFakeSocket.CLOSED;
    this.onclose?.({ code: 1006, reason: '', wasClean: false });
  }
}

/** A blank page to navigate between. */
@Component({ template: '' })
class TmBlankPage {}

describe('tmCreateHub', () => {
  let negotiates: { readonly url: string; readonly init: RequestInit }[];
  let answer: (init: RequestInit) => Promise<Response>;
  let reports: string[];
  let events: TmClientEvent[];
  let telemetry: TmFakeTelemetry;
  let hub: TmHubConnection;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideTellmaTesting(),
        provideTellmaStateTesting(),
        provideRouter([
          { path: 'a', component: TmBlankPage },
          { path: 'b', component: TmBlankPage },
        ]),
      ],
    });
    TmFakeSocket.sockets = [];
    TmFakeSocket.hang = false;
    vi.stubGlobal('WebSocket', TmFakeSocket);
    negotiates = [];
    answer = () => Promise.resolve(new Response(NEGOTIATED, { status: 200 }));
    vi.stubGlobal('fetch', (url: string, init: RequestInit) => {
      negotiates.push({ url, init });
      return answer(init);
    });
    reports = [];
    events = [];
    telemetry = TestBed.inject(TmFakeTelemetry);
    const sink: TmHubSink = {
      event: (event) => events.push(event),
      state: (state) => reports.push(state),
      connected: () => reports.push('sink.connected'),
      refused: (status) => reports.push(`refused ${status}`),
      ended: (reason) => reports.push(`ended ${reason}`),
    };
    hub = tmCreateHub(17, sink, TestBed.inject(Injector));
  });

  afterEach(async () => {
    await hub.stop();
    vi.unstubAllGlobals();
  });

  /** Waits until a condition holds, on real time. */
  const until = (condition: () => boolean): Promise<void> => vi.waitFor(() => expect(condition()).toBe(true), { timeout: 2_000, interval: 5 });

  /** A negotiate answer that never comes, unless its request is aborted. */
  const hung = (init: RequestInit): Promise<Response> =>
    new Promise<Response>((_, reject) => init.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))));

  it("negotiates at the tenant's hub with the session cookie, connects, and hands on the server's events", async () => {
    hub.start();
    await until(() => reports.includes('sink.connected'));
    expect(reports).toEqual(['connecting', 'connected', 'sink.connected']);
    expect(negotiates).toHaveLength(1);
    expect(new URL(negotiates[0]!.url).pathname).toBe('/17/hub/negotiate');
    expect(negotiates[0]!.init.credentials).toBe('include');
    const socket = TmFakeSocket.sockets[0]!;
    expect(new URL(socket.url).pathname).toBe('/17/hub');
    expect(socket.sent[0]).toBe(`{"protocol":"json","version":1}${RS}`);

    socket.receive(`{"type":1,"target":"event","arguments":[{"name":"acme.jobs.changed","tenantId":17,"payload":{"id":4}}]}${RS}`);
    socket.receive(`{"type":1,"target":"event","arguments":["not an envelope"]}${RS}`);
    expect(events).toEqual([{ name: 'acme.jobs.changed', tenantId: 17, payload: { id: 4 } }]);
    expect(telemetry.recent().filter((item) => item.name === 'tm.realtime.connected').map((item) => item.props['attempt'])).toEqual(['1']);
  });

  it('reports a dropped connection, and reconnects', async () => {
    hub.start();
    await until(() => reports.includes('sink.connected'));
    TmFakeSocket.sockets[0]!.drop();
    await until(() => reports.at(-1) === 'reconnecting');
    expect(telemetry.recent().filter((item) => item.name === 'tm.realtime.disconnected').map((item) => item.props)).toEqual([{ reason: 'error' }]);
    // The first attempt after a drop goes at once.
    await until(() => reports.filter((report) => report === 'sink.connected').length === 2);
    expect(negotiates).toHaveLength(2);
  });

  it("stops when the server ends the user's access", async () => {
    hub.start();
    await until(() => reports.includes('sink.connected'));
    const socket = TmFakeSocket.sockets[0]!;
    socket.receive(`{"type":1,"target":"event","arguments":[{"name":"session.ended","tenantId":17,"payload":{"reason":"tenant-unavailable"}}]}${RS}`);
    expect(reports.at(-1)).toBe('ended tenant-unavailable');
    await until(() => socket.readyState === TmFakeSocket.CLOSED);
    hub.nudge();
    expect(negotiates).toHaveLength(1);
  });

  for (const status of [401, 403, 404] as const) {
    it(`reports a negotiate refused with ${status}, and stops trying`, async () => {
      answer = () => Promise.resolve(new Response('', { status }));
      hub.start();
      await until(() => reports.includes(`refused ${status}`));
      expect(reports).toEqual(['connecting', `refused ${status}`]);
      hub.nudge();
      expect(negotiates).toHaveLength(1);
      expect(TmFakeSocket.sockets).toEqual([]);
    });
  }

  it('keeps trying after a negotiate that failed for a while, and reports the failure', async () => {
    answer = () => Promise.resolve(new Response('', { status: 503 }));
    hub.start();
    await until(() => telemetry.errors().length > 0);
    expect(telemetry.recent().filter((item) => item.name === 'tm.error.captured').every((item) => item.props['source'] === 'tm-hub')).toBe(true);
    // The first retry after a failed start goes at once, and it fails the same way.
    await until(() => negotiates.length >= 2);
    expect(reports).toEqual(['connecting']);
  });

  it('sends nothing for a user who changed, and reports it as a refusal', async () => {
    TestBed.inject(TmFakeSession).setCheck('changed');
    hub.start();
    await until(() => reports.includes('refused 401'));
    expect(negotiates).toEqual([]);
    expect(telemetry.recent().filter((item) => item.name === 'tm.realtime.refused').map((item) => item.props)).toEqual([{ status: 'user-check' }]);
  });

  it('waits in offline while the server is unreachable, and connects once it is', async () => {
    const api = TestBed.inject(TmFakeApi);
    api.setOnline(false);
    hub.start();
    await until(() => reports.includes('offline'));
    expect(negotiates).toEqual([]);
    api.setOnline(true);
    TestBed.tick();
    await until(() => reports.includes('sink.connected'));
  });

  it('never delays a navigation while the negotiate hangs, and stops it at once', async () => {
    answer = hung;
    const router = TestBed.inject(Router);
    hub.start();
    await until(() => negotiates.length === 1);
    expect(await router.navigateByUrl('/a')).toBe(true);
    expect(await router.navigateByUrl('/b')).toBe(true);
    expect(router.url).toBe('/b');
    await hub.stop();
    expect(negotiates[0]!.init.signal?.aborted).toBe(true);
    expect(reports).toEqual(['connecting']);
  });

  it('settles a stop within 5 s while the WebSocket never opens', async () => {
    TmFakeSocket.hang = true;
    hub.start();
    await until(() => TmFakeSocket.sockets.length === 1);
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    try {
      let settled = false;
      void hub.stop().then(() => (settled = true));
      await vi.advanceTimersByTimeAsync(4_999);
      expect(settled).toBe(false);
      await vi.advanceTimersByTimeAsync(1);
      expect(settled).toBe(true);
    } finally {
      vi.useRealTimers();
    }
  });
});

describe('the leader election\'s default hub factory', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), TmFakeSession, { provide: TmSession, useExisting: TmFakeSession }] });
  });

  it('loads the realtime entry point and creates the connection there, not started', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>();
    vi.stubGlobal('fetch', fetch);
    try {
      const sink: TmHubSink = { event: () => undefined, state: () => undefined, connected: () => undefined, refused: () => undefined, ended: () => undefined };
      // The leader's entry point loads this one, so the spec reaches it as the app does, with import().
      const { ɵTM_HUB_FACTORY } = await import('@tellma/core/leader');
      const hub = await TestBed.inject(ɵTM_HUB_FACTORY)!(17, sink, TestBed.inject(Injector));
      expect(hub).toBeInstanceOf(TmHub);
      await hub.stop();
      expect(fetch).not.toHaveBeenCalled();
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
