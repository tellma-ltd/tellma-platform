// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { AbortError, type AbortSignal as TmSignalRAbortSignal, HttpError, LogLevel } from '@microsoft/signalr';
import type { TmSessionCheck } from '@tellma/core';
import { TmFakeTelemetry } from '@tellma/core-testing';

import { TmHubHttpClient, TmHubLogError, TmHubLogger } from './tm-hub-signalr';

// The connection's HTTP client over a stubbed fetch: the user check before
// every request, the negotiate refusals it keeps, and the aborts of a stop;
// and the library's logger, which keeps warnings and errors without content.

const NEGOTIATE = 'http://localhost/17/hub/negotiate?negotiateVersion=1';
const POLL = 'http://localhost/17/hub?id=t&_=1';

/** A fetch whose answers the spec scripts, and which records every request and its signal. */
function stubFetch(answer: (url: string) => Promise<Response> | Response): { readonly requests: { readonly url: string; readonly init: RequestInit }[] } {
  const requests: { readonly url: string; readonly init: RequestInit }[] = [];
  vi.stubGlobal('fetch', (url: string, init: RequestInit) => {
    requests.push({ url, init });
    return Promise.resolve(answer(url));
  });
  return { requests };
}

/** A fetch answer that never comes, unless the request is aborted. */
function hung(init: RequestInit): Promise<Response> {
  return new Promise<Response>((_, reject) => init.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))));
}

describe('TmHubHttpClient', () => {
  let check: TmSessionCheck;

  beforeEach(() => {
    check = 'same';
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('sends with credentials while the user check finds the same user', async () => {
    const { requests } = stubFetch(() => new Response('{}', { status: 200 }));
    const client = new TmHubHttpClient(() => check);
    const response = await client.post(NEGOTIATE, { content: '', withCredentials: true });
    expect(response.statusCode).toBe(200);
    expect(requests.map((request) => [request.url, request.init.credentials])).toEqual([[NEGOTIATE, 'include']]);
    expect(client.refusal()).toBeNull();
  });

  it('sends nothing once the user changed or signed out, and reports the refusal', async () => {
    const { requests } = stubFetch(() => new Response('{}', { status: 200 }));
    const client = new TmHubHttpClient(() => check);
    await client.get(POLL);
    check = 'changed';
    await expect(client.get(POLL)).rejects.toThrow('The session no longer belongs to the user who connected.');
    check = 'missing';
    await expect(client.post(NEGOTIATE)).rejects.toThrow();
    expect(requests).toHaveLength(1);
    expect(client.refusal()).toBe('user');
  });

  for (const status of [401, 403, 404] as const) {
    it(`keeps a negotiate's ${status} as a refusal`, async () => {
      stubFetch(() => new Response('', { status }));
      const client = new TmHubHttpClient(() => check);
      await expect(client.post(NEGOTIATE)).rejects.toBeInstanceOf(HttpError);
      expect(client.refusal()).toBe(status);
    });
  }

  it("keeps no refusal for a negotiate's other failures, nor for any other request's 401", async () => {
    stubFetch((url) => new Response('', { status: url === NEGOTIATE ? 503 : 401 }));
    const client = new TmHubHttpClient(() => check);
    await expect(client.post(NEGOTIATE)).rejects.toBeInstanceOf(HttpError);
    await expect(client.get(POLL)).rejects.toBeInstanceOf(HttpError);
    await expect(client.post('http://localhost/17/negotiate-report')).rejects.toBeInstanceOf(HttpError);
    expect(client.refusal()).toBeNull();
  });

  it('keeps the first refusal', async () => {
    stubFetch(() => new Response('', { status: 403 }));
    const client = new TmHubHttpClient(() => check);
    await expect(client.post(NEGOTIATE)).rejects.toThrow();
    check = 'changed';
    await expect(client.post(NEGOTIATE)).rejects.toThrow();
    expect(client.refusal()).toBe(403);
  });

  it('aborts every request in flight on close, the negotiate included, and refuses every later one', async () => {
    const requests: { readonly url: string; readonly init: RequestInit }[] = [];
    vi.stubGlobal('fetch', (url: string, init: RequestInit) => {
      requests.push({ url, init });
      return hung(init);
    });
    const client = new TmHubHttpClient(() => check);
    const negotiate = client.post(NEGOTIATE, { content: '' });
    const poll = client.get(POLL);
    client.close();
    await expect(negotiate).rejects.toBeInstanceOf(AbortError);
    await expect(poll).rejects.toBeInstanceOf(AbortError);
    expect(requests.every((request) => request.init.signal?.aborted)).toBe(true);
    await expect(client.get(POLL)).rejects.toThrow('The connection was stopped.');
    expect(requests).toHaveLength(2);
    expect(client.refusal()).toBeNull();
  });

  it("aborts a request when the library aborts it through the request's own signal", async () => {
    const requests: RequestInit[] = [];
    vi.stubGlobal('fetch', (_: string, init: RequestInit) => {
      requests.push(init);
      return hung(init);
    });
    const client = new TmHubHttpClient(() => check);
    const signal: TmSignalRAbortSignal = { aborted: false, onabort: null };
    const poll = client.get(POLL, { abortSignal: signal });
    signal.aborted = true;
    signal.onabort?.();
    await expect(poll).rejects.toBeInstanceOf(AbortError);
    expect(requests[0]!.signal?.aborted).toBe(true);

    // A signal aborted before the send sends nothing.
    await expect(client.get(POLL, { abortSignal: signal })).rejects.toBeInstanceOf(AbortError);
    expect(requests).toHaveLength(1);
  });
});

describe('TmHubLogger', () => {
  it('records warnings and errors as errors without content, and nothing else', () => {
    const telemetry = new TmFakeTelemetry();
    const logger = new TmHubLogger(telemetry);
    for (const level of [LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.None]) {
      logger.log(level, 'Not news.');
    }
    logger.log(LogLevel.Warning, 'Timeout from the server.');
    logger.log(LogLevel.Error, "Failed to start the transport 'WebSockets': ws://localhost/17/hub?id=t&access_token=secret.token was refused.");
    logger.log(LogLevel.Critical, 'Broken.');
    expect(telemetry.recent().map((item) => item.props)).toEqual([
      { source: 'tm-hub', level: 'warning', errorClass: 'TmHubLogError' },
      { source: 'tm-hub', level: 'error', errorClass: 'TmHubLogError' },
      { source: 'tm-hub', level: 'error', errorClass: 'TmHubLogError' },
    ]);
    const errors = telemetry.errors() as TmHubLogError[];
    expect(errors[1]!.message).toBe("Failed to start the transport 'WebSockets': ws://localhost/17/hub?id=t&access_token=*** was refused.");
  });

  it('records nothing once muted', () => {
    const telemetry = new TmFakeTelemetry();
    const logger = new TmHubLogger(telemetry);
    logger.mute();
    logger.log(LogLevel.Error, 'An ended connection failed.');
    expect(telemetry.recent()).toEqual([]);
  });
});
