// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal } from '@angular/core';

import { TmTelemetry, type TmTelemetryItem, type TmTelemetryProps } from '../observe/tm-telemetry';
import { TmRequestLog, type TmRequestEnd, type TmRequestHandle, type TmRequestStart } from '../requests/tm-request-log';
import type { TmRecordedEvent } from '../session/tm-session.testing.util';
import type { TmApiHost } from './tm-api-host';
import type { TmApiCache, TmApiLocale, TmApiUpdateCheck, TmApiUpdates } from './tm-api-seams';

// The HTTP layer's browser, faked for its specs: a `fetch` whose requests the
// spec answers one by one, upload requests the spec drives, connectivity and
// visibility flags, and fixed random bytes, so every header is predictable.

/** One request the faked `fetch` received, which the spec answers or fails. */
export class TmFakeRequest {
  /** The promise `fetch` returned. */
  readonly promise: Promise<Response>;
  #resolve!: (response: Response) => void;
  #reject!: (error: unknown) => void;
  #settled = false;

  /**
   * Records a request; aborting its signal rejects it as `fetch` does.
   *
   * @param url - The request's URL.
   * @param init - Its method, headers, body and signal.
   */
  constructor(
    readonly url: string,
    readonly init: RequestInit,
  ) {
    this.promise = new Promise<Response>((resolve, reject) => {
      this.#resolve = resolve;
      this.#reject = reject;
    });
    init.signal?.addEventListener('abort', () => this.fail(new DOMException('The operation was aborted.', 'AbortError')));
  }

  /** Whether the request was answered, failed or aborted. */
  get settled(): boolean {
    return this.#settled;
  }

  /** Whether the request's signal was aborted. */
  get aborted(): boolean {
    return this.init.signal?.aborted === true;
  }

  /**
   * One request header.
   *
   * @param name - The header's name.
   * @returns Its value, or `null`.
   */
  header(name: string): string | null {
    return new Headers(this.init.headers).get(name);
  }

  /**
   * Answers the request.
   *
   * @param response - The answer.
   */
  answer(response: Response): void {
    if (!this.#settled) {
      this.#settled = true;
      this.#resolve(response);
    }
  }

  /**
   * Fails the request at the network level.
   *
   * @param error - The failure; a `TypeError` as `fetch` throws when omitted.
   */
  fail(error: unknown = new TypeError('Failed to fetch')): void {
    if (!this.#settled) {
      this.#settled = true;
      this.#reject(error);
    }
  }
}

/** One upload request, which the spec drives through progress to an answer. */
export class TmFakeXhr {
  /** The method `open` received. */
  method = '';
  /** The URL `open` received. */
  url = '';
  /** The request headers set. */
  readonly headers: Record<string, string> = {};
  /** The body sent. */
  body: unknown = undefined;
  /** Whether the request was aborted. */
  aborted = false;
  /** The answer's status. */
  status = 0;
  /** The answer's body. */
  responseText = '';
  /** The answer's headers, by lowercase name. */
  #responseHeaders: Record<string, string> = {};
  /** The upload's progress events. */
  readonly upload: { onprogress: ((event: ProgressEvent) => void) | null } = { onprogress: null };
  /** Runs on an answer. */
  onload: (() => void) | null = null;
  /** Runs on a network failure. */
  onerror: (() => void) | null = null;
  /** Runs on an abort. */
  onabort: (() => void) | null = null;

  /**
   * Records the method and URL.
   *
   * @param method - The method.
   * @param url - The URL.
   */
  open(method: string, url: string): void {
    this.method = method;
    this.url = url;
  }

  /**
   * Records a request header.
   *
   * @param name - The header's name.
   * @param value - Its value.
   */
  setRequestHeader(name: string, value: string): void {
    this.headers[name] = value;
  }

  /**
   * Records the body.
   *
   * @param body - The body.
   */
  send(body: unknown): void {
    this.body = body;
  }

  /** Aborts the request. */
  abort(): void {
    this.aborted = true;
    this.onabort?.();
  }

  /**
   * One answer header.
   *
   * @param name - The header's name.
   * @returns Its value, or `null`.
   */
  getResponseHeader(name: string): string | null {
    return this.#responseHeaders[name.toLowerCase()] ?? null;
  }

  /**
   * Reports upload progress.
   *
   * @param loaded - The bytes sent.
   * @param total - The bytes in all.
   */
  progress(loaded: number, total: number): void {
    this.upload.onprogress?.({ lengthComputable: true, loaded, total } as ProgressEvent);
  }

  /**
   * Answers the upload.
   *
   * @param status - The status.
   * @param body - The body, as JSON unless already text.
   * @param headers - The answer's headers.
   */
  respond(status: number, body: unknown, headers: Record<string, string> = {}): void {
    this.status = status;
    this.responseText = typeof body === 'string' ? body : JSON.stringify(body);
    this.#responseHeaders = Object.fromEntries(Object.entries(headers).map(([name, value]) => [name.toLowerCase(), value]));
    this.onload?.();
  }

  /** Fails the upload at the network level. */
  error(): void {
    this.onerror?.();
  }
}

/** A faked browser behind the HTTP layer. */
export class TmFakeApiHost implements TmApiHost {
  /** The requests `fetch` received, in order. */
  readonly requests: TmFakeRequest[] = [];
  /** The uploads created, in order. */
  readonly uploads: TmFakeXhr[] = [];
  /** What `navigator.onLine` says. */
  onLineNow = true;
  /** Whether the page is visible. */
  visibleNow = true;
  /** Whether the app runs in development mode. */
  dev = true;
  /** The byte every random draw returns. */
  byte = 0xab;
  /** Answers each request as it arrives, when set: a response, a failure, or `null` to leave it pending. */
  responder: ((request: TmFakeRequest) => Response | Error | null) | null = null;
  /** The event listeners, by target and event. */
  readonly #listeners = new Map<string, Set<() => void>>();

  /** Records a request, and answers it when a responder is set. */
  fetch(url: string, init: RequestInit): Promise<Response> {
    const request = new TmFakeRequest(url, init);
    this.requests.push(request);
    const outcome = this.responder?.(request) ?? null;
    if (outcome instanceof Response) {
      request.answer(outcome);
    } else if (outcome !== null) {
      request.fail(outcome);
    }
    return request.promise;
  }

  /** Creates an upload request. */
  xhr(): XMLHttpRequest {
    const xhr = new TmFakeXhr();
    this.uploads.push(xhr);
    return xhr as unknown as XMLHttpRequest;
  }

  /** `navigator.onLine` now. */
  onLine(): boolean {
    return this.onLineNow;
  }

  /** Whether the page is visible now. */
  visible(): boolean {
    return this.visibleNow;
  }

  /** Records a listener. */
  listen(target: 'window' | 'document', type: string, listener: () => void): () => void {
    const key = `${target}:${type}`;
    const set = this.#listeners.get(key) ?? new Set();
    set.add(listener);
    this.#listeners.set(key, set);
    return () => set.delete(listener);
  }

  /**
   * Fires an event at its listeners.
   *
   * @param target - Where it fires.
   * @param type - The event's name.
   */
  fire(target: 'window' | 'document', type: string): void {
    this.#listeners.get(`${target}:${type}`)?.forEach((listener) => listener());
  }

  /** Fills the array with the fixed byte. */
  random(array: Uint8Array<ArrayBuffer>): Uint8Array<ArrayBuffer> {
    return array.fill(this.byte);
  }

  /** Whether the app runs in development mode. */
  devMode(): boolean {
    return this.dev;
  }

  /** The last request. */
  last(): TmFakeRequest {
    const request = this.requests.at(-1);
    if (request === undefined) {
      throw new Error('No request was sent.');
    }
    return request;
  }
}

/**
 * An answer.
 *
 * @param status - The status.
 * @param body - The body: JSON unless already text; none when omitted.
 * @param headers - The headers.
 * @returns The response.
 */
export function tmAnswer(status: number, body?: unknown, headers: Record<string, string> = {}): Response {
  const text = body === undefined ? null : typeof body === 'string' ? body : JSON.stringify(body);
  return new Response(status === 204 || status === 304 ? null : text, { status, headers });
}

/**
 * A problem answer, as the server refuses a call.
 *
 * @param status - The status.
 * @param code - The problem code.
 * @param headers - The headers.
 * @param extra - More problem members.
 * @returns The response.
 */
export function tmProblemAnswer(
  status: number,
  code: string,
  headers: Record<string, string> = {},
  extra: Record<string, unknown> = {},
): Response {
  return tmAnswer(
    status,
    { type: 'about:blank', title: code, status, code, instance: '0af7651916cd43dd8448eb211c80319c', ...extra },
    { 'Content-Type': 'application/problem+json', ...headers },
  );
}

/** One request-log entry: what began it and how it ended. */
export interface TmLoggedRequest {
  /** What began it. */
  readonly start: TmRequestStart;
  /** How it ended, once it did. */
  end: TmRequestEnd | null;
}

/** A request log that records its entries. */
@Injectable()
export class TmRecordingRequestLog extends TmRequestLog {
  /** The entries, in order. */
  readonly entries: TmLoggedRequest[] = [];

  /** Nothing is listed. */
  override readonly recent = signal([]).asReadonly();

  /** Nothing is listed. */
  override diagnosticsText(): string {
    return '';
  }

  /** Nothing is copied. */
  override copy(): void {
    // Nothing to copy.
  }

  /** Records an entry. */
  override begin(start: TmRequestStart): TmRequestHandle {
    const entry: TmLoggedRequest = { start, end: null };
    this.entries.push(entry);
    return { end: (result) => void (entry.end = result) };
  }
}

/** Telemetry that records its events and its errors. */
@Injectable()
export class TmRecordingApiTelemetry extends TmTelemetry {
  /** The events, in order. */
  readonly events: TmRecordedEvent[] = [];

  /** The errors, with their context. */
  readonly errors: { readonly error: unknown; readonly context: Readonly<Record<string, string>> | undefined }[] = [];

  /** Nothing is kept for the diagnostics view. */
  override readonly recent = signal<readonly TmTelemetryItem[]>([]).asReadonly();

  /** Records an event. */
  override event(name: string, props?: TmTelemetryProps): void {
    this.events.push({ name, props });
  }

  /** Ignores a timing. */
  override timing(): void {
    // Not recorded.
  }

  /** Records an error. */
  override error(error: unknown, context?: Readonly<Record<string, string>>): void {
    this.errors.push({ error, context });
  }

  /** Ignores a flush. */
  override flush(): void {
    // Not recorded.
  }
}

/** The cache refresher, faked: it records what it was told. */
export class TmFakeApiCache implements TmApiCache {
  /** The observed tags, in order. */
  readonly observed: { readonly tenantId: number; readonly tags: Readonly<Record<string, string>> }[] = [];
  /** The refreshes, in order. */
  readonly refreshed: { readonly tenantId: number; readonly docs: readonly string[] }[] = [];

  /** Records observed tags. */
  observe(tenantId: number, tags: Readonly<Record<string, string>>): void {
    this.observed.push({ tenantId, tags });
  }

  /** Records a refresh. */
  refresh(tenantId: number, docs: readonly string[]): Promise<void> {
    this.refreshed.push({ tenantId, docs });
    return Promise.resolve();
  }
}

/** The worker's update check, faked: it answers what the spec says, or hangs. */
export class TmFakeApiUpdates implements TmApiUpdates {
  /** The answer of the next checks, or `null` to hang. */
  answer: TmApiUpdateCheck | null = 'disabled';
  /** How many checks ran. */
  checks = 0;

  /** Runs a check. */
  check(): Promise<TmApiUpdateCheck> {
    this.checks++;
    const answer = this.answer;
    return answer === null ? new Promise(() => undefined) : Promise.resolve(answer);
  }
}

/** The request locale, fixed. */
export const TM_TEST_API_LOCALE: TmApiLocale = { acceptLanguage: () => 'ar-SA', calendarCode: () => 'uq' };
