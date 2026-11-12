// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { effect, inject, Injectable, Injector, signal, untracked, type Signal } from '@angular/core';
import { Router } from '@angular/router';
import type { TmBlobFetchResult } from '@tellma/core-ui/image';

import { TM_NOW } from '../app/tm-now';
import { TmLaunch } from '../launch/tm-launch';
import { tmRoutedTenant } from '../launch/tm-routed-tenant';
import { TM_CLIENT_METADATA } from '../metadata/tm-client-metadata';
import { TM_METADATA_LOADERS } from '../metadata/tm-metadata-loaders';
import { TmTelemetry } from '../observe/tm-telemetry';
import { TmRequestLog, type TmRequestEnd } from '../requests/tm-request-log';
import { TmSession } from '../session/tm-session';
import { TmSessionPaths } from '../session/tm-session-paths';
import type {
  TmApi,
  TmBlobDescriptor,
  TmDownload,
  TmDownloadOptions,
  TmFetchBlobOptions,
  TmReadOptions,
  TmSendOptions,
  TmUploadOptions,
  TmWriteOptions,
} from './tm-api';
import { TmApiError, type TmApiErrorKind, type TmProblem } from './tm-api-error';
import { TM_API_HOST } from './tm-api-host';
import { TmApiOnline } from './tm-api-online';
import {
  TM_API_CACHE,
  TM_API_LOCALE,
  TM_API_UPDATES,
  type TmApiCache,
  type TmApiLocale,
  type TmApiUpdateCheck,
  type TmApiUpdates,
} from './tm-api-seams';
import {
  TM_OUTDATED,
  tmCompareBuilds,
  tmDispositionFileName,
  tmKindOf,
  tmParseProblem,
  tmParseStepUp,
  tmParseVersionTags,
  tmRandomHex,
  tmRetryAfterMs,
} from './tm-api-wire';

// The HTTP layer (spec 0024 §10). Every call runs §10.3's steps: the user
// check, the offline-write gate, the send under one AbortController that
// follows the caller's signal and the watchdog, the response check, the
// observers, and the status mapping of §10.6. Reads retry by §10.5's table;
// writes, uploads, sends and blob fetches never do. This class implements
// TmApi's members structurally and imports its file only for types, so the
// public class can name this one as its root default without a module cycle.

/** The watchdog of an ordinary call, in milliseconds. */
const WATCHDOG_MS = 60_000;

/** The watchdog of a long operation, in milliseconds. */
const LONG_WATCHDOG_MS = 360_000;

/** The largest body a keepalive send carries, in bytes. */
const SEND_LIMIT_BYTES = 16 * 1024;

/** How long the outdated-client verdict waits for the worker's update check, in milliseconds. */
const WORKER_CAP_MS = 3_000;

/** The waits before the first and second retry of a network failure, in milliseconds, before jitter. */
const NETWORK_WAITS_MS: readonly number[] = [1_000, 3_000];

/** The jitter of a network retry's wait: ±25 %. */
const NETWORK_JITTER = 0.25;

/** The wait of a `429` without `Retry-After`, in milliseconds. */
const RATE_LIMIT_WAIT_MS = 1_000;

/** The longest wait a `429` may ask for, in milliseconds. */
const RATE_LIMIT_CAP_MS = 10_000;

/** The longest wait a `503` may ask for, in milliseconds. */
const UNAVAILABLE_CAP_MS = 30_000;

/** The wait before a read judged `server-outdated` is retried, in milliseconds. */
const SERVER_OUTDATED_WAIT_MS = 30_000;

/** The route of the sign-out write, the one call a signing-out tab still sends. */
const LOGOUT_ROUTE = '/bff/logout';

/**
 * A URL the layer sends: a path on this origin. One leading `/`, not followed
 * by `/` or `\` (which would name another host), and no tab or line break
 * (which the URL parser drops, so `/\t/host` would become `//host`).
 */
const LOCAL_PATH = /^\/(?![/\\])[^\t\n\r]*$/;

/** A `.` or `..` path segment, plain or percent-encoded, which the URL parser resolves away. */
const DOT_SEGMENT = /[/\\](?:\.|%2e){1,2}(?=[/\\]|$)/i;

/** The routes every instance answers without a session, which skip the user check. */
const ANONYMOUS_ROUTE = /^\/(?:api\/distribution-info(?:[/?#]|$)|health\/|\.well-known\/)/;

/** One confirmation key: 1 to 100 visible ASCII characters other than the comma that joins them. */
const CONFIRMATION_KEY = /^[\x21-\x2b\x2d-\x7e]{1,100}$/;

/** The most confirmation keys one call may carry. */
const MAX_CONFIRMATIONS = 16;

/** Why a read is retried, as `tm.api.retry` reports it. */
type TmRetryReason = 'network' | '429' | '503' | 'server-outdated';

/** How many retries each reason allows. */
const RETRY_LIMITS: Readonly<Record<TmRetryReason, number>> = { network: 2, '429': 2, '503': 2, 'server-outdated': 1 };

/** The codes a refusal without a problem body still carries, by status. */
const BARE_CODES: Readonly<Record<number, string>> = { 401: 'unauthenticated', 429: 'rate-limited' };

/** A retry a failed attempt asks for. */
interface TmRetry {
  /** Why. */
  readonly reason: TmRetryReason;
  /** The wait before it, in milliseconds; `null` for the network's own schedule. */
  readonly waitMs: number | null;
}

/** What one attempt produced: a value, or an error and the retry it allows. */
type TmAttempt<T> =
  | { readonly ok: true; readonly value: T; readonly status: number; readonly build: string | null }
  | { readonly ok: false; readonly error: TmApiError; readonly retry: TmRetry | null; readonly build: string | null };

/** The parts of an answer the layer reads: its status and its headers. */
interface TmAnswer {
  /** The HTTP status. */
  readonly status: number;
  /** Reads one response header. */
  readonly header: (name: string) => string | null;
}

/** How a call reads and finishes a success body. */
interface TmReader<R, T> {
  /** Whether a status is a success the reader takes. */
  readonly accepts: (status: number) => boolean;
  /** Reads the body; `rearm` restarts the watchdog as bytes arrive. */
  readonly read: (response: Response, rearm: () => void) => Promise<R>;
  /** Turns the read body into the call's value, after the response check; throws on a malformed body. */
  readonly finish: (raw: R, answer: TmAnswer) => T;
}

/** One prepared call: what each attempt sends, and how its answers are judged. */
interface TmCall {
  /** The route as the caller gave it. */
  readonly route: string;
  /** The URL sent. */
  readonly url: string;
  /** The tenant called, or `null` for a tenantless route. */
  readonly tenantId: number | null;
  /** The HTTP method. */
  readonly method: 'POST' | 'GET';
  /** The JSON body, or `undefined` for none. */
  readonly body: string | undefined;
  /** The `Tellma-Client` value. */
  readonly client: string;
  /** The headers of this call beyond the ones every call sends. */
  readonly headers: Readonly<Record<string, string>>;
  /** Whether a network failure leaves the outcome unknown, as for a write. */
  readonly write: boolean;
  /** Whether the call is retried on a read's retryable failures. */
  readonly retried: boolean;
  /** Whether the request log records the call. */
  readonly logged: boolean;
  /** Whether the user check runs before the call is sent. */
  readonly authenticated: boolean;
  /** Whether the user check runs on the answer; never for the sign-out, whose answer clears the session. */
  readonly checksAnswer: boolean;
  /** Whether the call is background work. */
  readonly background: boolean;
  /** The watchdog, in milliseconds. */
  readonly watchdogMs: number;
  /** Cancels the call: the caller's signal, or the session leaving the tab; `null` for a write. */
  readonly cancel: AbortSignal | null;
  /** The call's trace id. */
  readonly traceId: string;
  /** The session handle the call went out under, so a late `401` cannot end a newer session. */
  readonly sh: string | undefined;
}

/** The Contracts chunk, indexed for lookups. */
interface TmContractIndex {
  /** Each route's fingerprint. */
  readonly fingerprints: ReadonlyMap<string, string>;
  /** The long routes. */
  readonly long: ReadonlySet<string>;
  /** The `POST` routes a retry may repeat. */
  readonly idempotent: ReadonlySet<string>;
}

/** The index of a distribution with no Contracts chunk: no route is described. */
const NO_CONTRACTS: TmContractIndex = { fingerprints: new Map(), long: new Set(), idempotent: new Set() };

/** Whether a status is a 2xx. */
const isSuccess = (status: number): boolean => status >= 200 && status < 300;

/** Reads a JSON body: the parsed value, or `undefined` for an empty body. */
const JSON_READER: TmReader<string, unknown> = {
  accepts: isSuccess,
  read: (response) => response.text(),
  finish: (text) => (text === '' ? undefined : (JSON.parse(text) as unknown)),
};

/** Reads a blob fetch's body: a 2xx's bytes, or nothing on a `304`. */
const BLOB_READER: TmReader<Blob | null, TmBlobFetchResult> = {
  accepts: (status) => isSuccess(status) || status === 304,
  read: (response) => (response.status === 304 ? Promise.resolve(null) : response.blob()),
  finish: (blob, answer) => ({ status: answer.status, blob, etag: answer.header('ETag') }),
};

/**
 * The reader of a download: streams the body, reporting the bytes received
 * and restarting the watchdog at each chunk, and names the file from
 * `Content-Disposition`.
 *
 * @param onProgress - Receives the bytes received so far.
 * @returns The reader.
 */
function downloadReader(onProgress: ((bytes: number) => void) | undefined): TmReader<Blob, TmDownload> {
  return {
    accepts: isSuccess,
    read: async (response, rearm) => {
      const type = response.headers.get('Content-Type') ?? '';
      if (response.body === null) {
        return new Blob([], { type });
      }
      const reader = response.body.getReader();
      const chunks: Uint8Array<ArrayBuffer>[] = [];
      let bytes = 0;
      for (;;) {
        const { done, value } = await reader.read();
        if (done) {
          break;
        }
        chunks.push(value);
        bytes += value.byteLength;
        // A slow export that keeps sending is alive: only a stall trips the watchdog.
        rearm();
        onProgress?.(bytes);
      }
      return new Blob(chunks, { type });
    },
    finish: (blob, answer) => ({
      fileName: tmDispositionFileName(answer.header('Content-Disposition')) ?? 'download',
      contentType: blob.type,
      blob,
    }),
  };
}

/**
 * The key of a route in the Contracts chunk, such as
 * `POST /{tenantId}/api/web/centers/query`.
 *
 * @param method - The HTTP method.
 * @param route - The route as the caller gave it.
 * @returns The key.
 */
function contractKey(method: 'POST' | 'GET', route: string): string {
  const path = route.split(/[?#]/, 1)[0];
  return route.startsWith('/') ? `${method} ${path}` : `${method} /{tenantId}/api/web/${path}`;
}

/** The HTTP layer over `fetch` and, for uploads, `XMLHttpRequest`. */
@Injectable({ providedIn: 'root' })
export class TmHttpApi implements TmApi {
  readonly #host = inject(TM_API_HOST);
  readonly #connectivity = inject(TmApiOnline);
  readonly #router = inject(Router);
  readonly #log = inject(TmRequestLog);
  readonly #loaders = inject(TM_METADATA_LOADERS, { optional: true });
  readonly #injector = inject(Injector);
  readonly #now = inject(TM_NOW);
  readonly #writes = signal(0);
  readonly #random = (array: Uint8Array<ArrayBuffer>): Uint8Array<ArrayBuffer> => this.#host.random(array);
  // The calls a session leaving the tab aborts: reads, downloads, uploads and blob fetches.
  readonly #cancellable = new Set<AbortController>();
  // When each server build was first judged older. A verdict for that build
  // within the first one's retry window is the same slot swap; a later one is
  // §5.6's second verdict, as after a rollback, and asks the worker.
  readonly #judgedOlder = new Map<string, number>();
  #contracts: TmContractIndex | null = null;
  #contractsLoad: Promise<TmContractIndex> | null = null;
  #client: string | null = null;

  // The services below are resolved at each use, never in a field
  // initialiser: each may call TmApi itself (a sign-out, a telemetry flush, a
  // cache sync, a locale save), and eager injection on both sides would be a
  // circular dependency. The injector caches them, so a use costs a lookup.

  /** The tab's session. */
  get #session(): TmSession {
    return this.#injector.get(TmSession);
  }

  /** The expiry and changed-user paths. */
  get #paths(): TmSessionPaths {
    return this.#injector.get(TmSessionPaths);
  }

  /** The launch mark a tenant's first 2xx settles. */
  get #launch(): TmLaunch {
    return this.#injector.get(TmLaunch);
  }

  /** The telemetry the layer reports to. */
  get #telemetry(): TmTelemetry {
    return this.#injector.get(TmTelemetry);
  }

  /** The language and calendar every call declares. */
  get #locale(): TmApiLocale {
    return this.#injector.get(TM_API_LOCALE);
  }

  /** The cache refresher the version tags go to. */
  get #cache(): TmApiCache {
    return this.#injector.get(TM_API_CACHE);
  }

  /** The worker's update check. */
  get #updates(): TmApiUpdates {
    return this.#injector.get(TM_API_UPDATES);
  }

  /** Whether the server is reachable, as far as this tab knows. */
  readonly online: Signal<boolean> = this.#connectivity.online;

  /** This tab's unsettled writes. */
  readonly writesInFlight: Signal<number> = this.#writes.asReadonly();

  /** Aborts the tab's cancellable calls when the session leaves it: an expiry or a sign-out. */
  constructor() {
    effect(() => {
      if (this.#session.state() !== 'active') {
        untracked(() => this.#cancellable.forEach((controller) => controller.abort()));
      }
    });
  }

  /** {@inheritDoc TmApi.read} */
  read<T>(route: string, body: unknown, options: TmReadOptions): Promise<T> {
    return this.#cancellableCall(options.signal, (cancel) =>
      this.#exchange(route, body, 'read', options, cancel, JSON_READER as TmReader<string, T>),
    );
  }

  /** {@inheritDoc TmApi.write} */
  async write<T>(route: string, body: unknown, options: TmWriteOptions = {}): Promise<T> {
    this.#writes.update((count) => count + 1);
    try {
      return await this.#exchange(route, body, 'write', options, null, JSON_READER as TmReader<string, T>);
    } finally {
      this.#writes.update((count) => count - 1);
    }
  }

  /** {@inheritDoc TmApi.download} */
  download(route: string, body: unknown, options: TmDownloadOptions): Promise<TmDownload> {
    return this.#cancellableCall(options.signal, (cancel) =>
      this.#exchange(route, body, 'read', options, cancel, downloadReader(options.onProgress)),
    );
  }

  /** {@inheritDoc TmApi.upload} */
  upload(kind: string, file: Blob, options: TmUploadOptions): Promise<TmBlobDescriptor> {
    return this.#cancellableCall(options.signal, (cancel) => this.#upload(kind, file, options, cancel));
  }

  /** {@inheritDoc TmApi.fetchBlob} */
  async fetchBlob(url: string, options: TmFetchBlobOptions = {}): Promise<TmBlobFetchResult> {
    const signal = options.signal ?? null;
    try {
      return await this.#cancellableCall(signal, (cancel) => this.#fetchBlob(url, options.ifNoneMatch, cancel));
    } catch (error) {
      // The blob cache tells an abort from a failure by the signal's own reason (spec 0010 §D.6).
      if (signal?.aborted === true) {
        throw signal.reason;
      }
      throw error;
    }
  }

  /** {@inheritDoc TmApi.send} */
  send(route: string, body: unknown, options: TmSendOptions = {}): void {
    // A misuse throws in development; in production the send is dropped,
    // since nobody reads its outcome.
    let tenantId: number | null;
    let url: string;
    try {
      tenantId = this.#tenantOf(route, undefined);
      url = this.#urlOf(route, tenantId);
    } catch (error) {
      if (error instanceof TmApiError) {
        return;
      }
      throw error;
    }
    const json = body === undefined ? undefined : JSON.stringify(body);
    const bytes = json === undefined ? 0 : new TextEncoder().encode(json).byteLength;
    if (bytes > SEND_LIMIT_BYTES) {
      if (this.#host.devMode()) {
        throw new Error(`The send to '${route}' is ${bytes} bytes, over the 16 KB limit.`);
      }
      return;
    }
    // A send runs as a page hides, so it never starts the expiry or the
    // changed-user path; it goes only under the tab's own user.
    if (!ANONYMOUS_ROUTE.test(route) && this.#session.check() !== 'same') {
      return;
    }
    let client: string;
    try {
      client = this.#clientId();
    } catch {
      // The client metadata is not loaded yet: the app never started, so there is nothing to send for.
      return;
    }
    const headers = this.#headers(
      tenantId,
      client,
      tmRandomHex(16, this.#random),
      json === undefined ? null : 'application/json',
    );
    if (options.background === true) {
      headers['Tellma-Activity'] = 'background';
    }
    // The fingerprint goes along when the chunk is loaded; a send never waits for it.
    const fingerprint = tenantId === null ? undefined : this.#contracts?.fingerprints.get(contractKey('POST', route));
    if (fingerprint !== undefined) {
      headers['Tellma-Contract'] = fingerprint;
    }
    this.#host
      .fetch(url, { method: 'POST', headers, body: json, credentials: 'same-origin', keepalive: true })
      .catch(() => undefined);
  }

  /** {@inheritDoc TmApi.fingerprint} */
  async fingerprint(route: string): Promise<string | null> {
    if (route.startsWith('/')) {
      return null;
    }
    const contracts = await this.#loadContracts('network', '');
    return contracts.fingerprints.get(contractKey('POST', route)) ?? null;
  }

  // --- Preparing a call ---

  /**
   * Runs a cancellable call: one controller follows the caller's signal, and
   * the session leaving the tab aborts it too.
   *
   * @param signal - The caller's signal, if any.
   * @param run - The call, given the controller's signal.
   * @returns The call's result.
   */
  async #cancellableCall<T>(signal: AbortSignal | null, run: (cancel: AbortSignal) => Promise<T>): Promise<T> {
    const controller = new AbortController();
    const follow = (): void => controller.abort();
    if (signal?.aborted === true) {
      controller.abort();
    } else {
      signal?.addEventListener('abort', follow, { once: true });
    }
    this.#cancellable.add(controller);
    try {
      return await run(controller.signal);
    } finally {
      this.#cancellable.delete(controller);
      signal?.removeEventListener('abort', follow);
    }
  }

  /**
   * Prepares and runs a read or a write over `fetch`.
   *
   * @param route - The route.
   * @param body - The body, sent as JSON.
   * @param mode - Whether the call is a read or a write.
   * @param options - The caller's options.
   * @param cancel - The call's cancellation, or `null` for a write.
   * @param reader - How the success body is read.
   * @returns The call's value.
   */
  async #exchange<R, T>(
    route: string,
    body: unknown,
    mode: 'read' | 'write',
    options: Partial<TmReadOptions> & TmWriteOptions,
    cancel: AbortSignal | null,
    reader: TmReader<R, T>,
  ): Promise<T> {
    const traceId = tmRandomHex(16, this.#random);
    const dev = this.#host.devMode();
    const read = mode === 'read';
    const method = read && options.method === 'GET' ? 'GET' : 'POST';
    const tenantId = this.#tenantOf(route, options.tenantId);
    const url = this.#urlOf(route, tenantId);

    // --- Development checks of the call's shape ---
    if (method === 'GET' && body !== undefined && body !== null && dev) {
      throw new Error(`The GET of '${route}' has a body.`);
    }
    const confirmations = options.confirmations ?? [];
    if (dev && (confirmations.length > MAX_CONFIRMATIONS || !confirmations.every((key) => CONFIRMATION_KEY.test(key)))) {
      throw new Error(
        `'${route}' confirms ${confirmations.length} keys: at most 16 keys of 1-100 visible ASCII characters, no comma.`,
      );
    }

    // --- The Contracts chunk: the route's fingerprint, its class and its watchdog ---
    const contracts = tenantId === null ? NO_CONTRACTS : await this.#loadContracts(read ? 'network' : 'not-sent', traceId);
    const key = contractKey(method, route);
    const fingerprint = contracts.fingerprints.get(key);
    const idempotent = method === 'GET' || fingerprint === undefined || contracts.idempotent.has(key);
    if (read && !idempotent && dev) {
      throw new Error(`'${route}' is not safe to retry: call it with write().`);
    }

    // --- The gates: the session, the user check and the offline write ---
    const authenticated = this.#admit(route, read ? 'read' : 'write', cancel, traceId);
    const headers: Record<string, string> = {};
    if (tenantId !== null && fingerprint !== undefined) {
      headers['Tellma-Contract'] = fingerprint;
    }
    if (read && options.background === true) {
      headers['Tellma-Activity'] = 'background';
    }
    if (read && options.priority === 'low') {
      headers['Tellma-Priority'] = 'low';
    }
    if (!read && confirmations.length > 0) {
      headers['Tellma-Confirm'] = confirmations.join(', ');
    }
    const call: TmCall = {
      route,
      url,
      tenantId,
      method,
      body: method === 'GET' || body === undefined ? undefined : JSON.stringify(body),
      client: this.#clientId(),
      headers,
      write: !read,
      // A mapped route that is not idempotent runs once even through read() in production.
      retried: read && idempotent,
      logged: true,
      authenticated,
      // The sign-out's answer clears the session, so the cookie is gone by design.
      checksAnswer: authenticated && route !== LOGOUT_ROUTE,
      background: read && options.background === true,
      watchdogMs: options.long === true || contracts.long.has(key) ? LONG_WATCHDOG_MS : WATCHDOG_MS,
      cancel,
      traceId,
      sh: this.#session.profile()?.sh,
    };
    return this.#run(call, () => this.#attempt(call, reader));
  }

  /**
   * Stages a file over `XMLHttpRequest`, the one transport that reports
   * upload progress.
   *
   * @param kind - The blob kind.
   * @param file - The file.
   * @param options - Its name, the progress callback and the tenant.
   * @param cancel - The upload's cancellation.
   * @returns The staged blob.
   */
  async #upload(kind: string, file: Blob, options: TmUploadOptions, cancel: AbortSignal): Promise<TmBlobDescriptor> {
    const traceId = tmRandomHex(16, this.#random);
    const route = `blobs/${kind}`;
    const tenantId = this.#tenantOf(route, options.tenantId);
    // Spec 0017 §8.1: the raw file, named in the query.
    const url = this.#local(`/${tenantId}/blobs/${encodeURIComponent(kind)}?fileName=${encodeURIComponent(options.fileName)}`);
    const authenticated = this.#admit(route, 'upload', cancel, traceId);
    const call: TmCall = {
      route,
      url,
      tenantId,
      method: 'POST',
      body: undefined,
      client: this.#clientId(),
      headers: {},
      write: false,
      retried: false,
      logged: true,
      authenticated,
      checksAnswer: authenticated,
      background: false,
      // Restarted at each progress event, so only a stalled upload or a lost answer ends it.
      watchdogMs: WATCHDOG_MS,
      cancel,
      traceId,
      sh: this.#session.profile()?.sh,
    };
    return this.#run(call, () => this.#xhrAttempt(call, file, options.onProgress));
  }

  /**
   * Fetches a blob for `tm-image`.
   *
   * @param url - The blob's URL.
   * @param ifNoneMatch - The held ETag, for a revalidation.
   * @param cancel - The fetch's cancellation.
   * @returns The status, the bytes and the ETag.
   */
  async #fetchBlob(url: string, ifNoneMatch: string | undefined, cancel: AbortSignal): Promise<TmBlobFetchResult> {
    this.#local(url);
    const traceId = tmRandomHex(16, this.#random);
    const authenticated = this.#admit(url, 'read', cancel, traceId);
    const call: TmCall = {
      route: url,
      url,
      tenantId: null,
      method: 'GET',
      body: undefined,
      client: this.#clientId(),
      headers: ifNoneMatch === undefined ? {} : { 'If-None-Match': ifNoneMatch },
      write: false,
      retried: false,
      // Images would crowd out the calls a support message quotes.
      logged: false,
      authenticated,
      checksAnswer: authenticated,
      background: false,
      watchdogMs: WATCHDOG_MS,
      cancel,
      traceId,
      sh: this.#session.profile()?.sh,
    };
    return this.#run(call, () => this.#attempt(call, BLOB_READER));
  }

  /**
   * The tenant a route goes to: `null` for a tenantless route, else the
   * caller's tenant or the routed one. A tenant route with neither is a
   * programming error: it throws in development, and rejects `server`
   * (`no-tenant`) in production.
   *
   * @param route - The route.
   * @param tenantId - The caller's tenant, if any.
   * @returns The tenant, or `null`.
   */
  #tenantOf(route: string, tenantId: number | undefined): number | null {
    if (route.startsWith('/')) {
      return null;
    }
    const tenant = tenantId ?? tmRoutedTenant(this.#router.url);
    if (tenant !== null) {
      return tenant;
    }
    if (this.#host.devMode()) {
      throw new Error(`'${route}' was called with no tenant: pass tenantId, or call it under a tenant's route.`);
    }
    throw new TmApiError({ kind: 'server', code: 'no-tenant' });
  }

  /**
   * The gates before a call is sent: a signing-out tab sends nothing but
   * its sign-out, the user check runs for an authenticated route, and a
   * write while offline is not sent.
   *
   * @param route - The route.
   * @param mode - What the call is.
   * @param cancel - The call's cancellation, if any.
   * @param traceId - The call's trace id.
   * @returns Whether the call is authenticated.
   */
  #admit(route: string, mode: 'read' | 'write' | 'upload', cancel: AbortSignal | null, traceId: string): boolean {
    if (cancel?.aborted === true) {
      throw new TmApiError({ kind: 'cancelled', traceId });
    }
    if (this.#session.state() !== 'active' && !(mode === 'write' && route === LOGOUT_ROUTE)) {
      throw new TmApiError({ kind: 'cancelled', traceId });
    }
    const authenticated = !ANONYMOUS_ROUTE.test(route);
    if (authenticated) {
      this.#check(traceId);
    }
    if (mode !== 'read' && !this.#host.onLine()) {
      throw new TmApiError({ kind: 'offline', code: 'not-sent', traceId });
    }
    return authenticated;
  }

  /**
   * The user check: another user's or no session runs its path and throws.
   *
   * @param traceId - The call's trace id.
   * @throws TmApiError `unauthenticated` when the session is gone, `cancelled` when another user's replaced it.
   */
  #check(traceId: string): void {
    const check = this.#session.check();
    if (check === 'missing') {
      this.#paths.expired('cookie');
      throw new TmApiError({ kind: 'unauthenticated', traceId });
    }
    if (check === 'changed') {
      this.#paths.changedUser();
      throw new TmApiError({ kind: 'cancelled', traceId });
    }
  }

  /**
   * The Contracts chunk, loaded once; an app with no metadata loaders has
   * no described route.
   *
   * @param code - The `offline` code of a failed load: `network` for a read, `not-sent` for a write.
   * @param traceId - The call's trace id.
   * @returns The indexed chunk.
   */
  async #loadContracts(code: 'network' | 'not-sent', traceId: string): Promise<TmContractIndex> {
    if (this.#contracts !== null) {
      return this.#contracts;
    }
    const loaders = this.#loaders;
    if (loaders === null) {
      return NO_CONTRACTS;
    }
    // Concurrent first calls share one load; a failed load is tried again by the next call.
    this.#contractsLoad ??= loaders.contracts().then(
      (chunk) =>
        (this.#contracts = {
          fingerprints: new Map(Object.entries(chunk.fingerprints)),
          long: new Set(chunk.long),
          idempotent: new Set(chunk.idempotent),
        }),
      (error: unknown) => {
        this.#contractsLoad = null;
        throw error;
      },
    );
    try {
      return await this.#contractsLoad;
    } catch {
      throw new TmApiError({ kind: 'offline', code, traceId });
    }
  }

  /**
   * The `Tellma-Client` value: `web/` and the UI version.
   *
   * @returns The value.
   * @throws Error when the client metadata is not loaded yet.
   */
  #clientId(): string {
    this.#client ??= `web/${this.#injector.get(TM_CLIENT_METADATA).distribution.version}`;
    return this.#client;
  }

  /**
   * The URL of a route, checked to be a path on this origin.
   *
   * @param route - The route.
   * @param tenantId - Its tenant, or `null` for a tenantless route.
   * @returns The URL.
   */
  #urlOf(route: string, tenantId: number | null): string {
    return this.#local(tenantId === null ? route : `/${tenantId}/api/web/${route}`);
  }

  /**
   * Admits only a path on this origin, before anything is sent: a foreign
   * server must never be judged as this one, and a dot segment must never
   * slip a route past the anonymous-route rule.
   *
   * @param url - The URL.
   * @returns The URL.
   * @throws Error in development; `TmApiError` `server` (`invalid-url`) in production.
   */
  #local(url: string): string {
    if (LOCAL_PATH.test(url) && !DOT_SEGMENT.test(url.split(/[?#]/, 1)[0])) {
      return url;
    }
    if (this.#host.devMode()) {
      throw new Error(`'${url}' is not a path on this origin.`);
    }
    throw new TmApiError({ kind: 'server', code: 'invalid-url' });
  }

  /**
   * The headers every call sends, with a fresh span id.
   *
   * @param tenantId - The call's tenant, or `null`.
   * @param client - The `Tellma-Client` value.
   * @param traceId - The call's trace id.
   * @param contentType - The body's media type, or `null` without a body.
   * @returns The headers.
   */
  #headers(tenantId: number | null, client: string, traceId: string, contentType: string | null): Record<string, string> {
    const headers: Record<string, string> = {
      'Tellma-Client': client,
      traceparent: `00-${traceId}-${tmRandomHex(8, this.#random)}-01`,
    };
    const language = this.#locale.acceptLanguage();
    if (language !== '') {
      headers['Accept-Language'] = language;
    }
    const calendar = tenantId === null ? null : this.#locale.calendarCode();
    if (calendar !== null) {
      headers['Tellma-Calendar'] = calendar;
    }
    if (contentType !== null) {
      headers['Content-Type'] = contentType;
    }
    return headers;
  }

  // --- Running a call ---

  /**
   * Runs a call's attempts: the first, then each retry its failure allows after
   * its wait, which an abort ends at once. The request log holds one entry
   * for the whole call.
   *
   * @param call - The call.
   * @param attempt - Sends one attempt.
   * @returns The call's value.
   */
  async #run<T>(call: TmCall, attempt: () => Promise<TmAttempt<T>>): Promise<T> {
    const handle = call.logged
      ? this.#log.begin({
          traceId: call.traceId,
          route: call.route,
          tenantId: call.tenantId,
          method: call.method,
          background: call.background,
        })
      : null;
    const used: Partial<Record<TmRetryReason, number>> = {};
    let result: TmAttempt<T>;
    for (;;) {
      result = await attempt();
      if (result.ok || result.retry === null || !call.retried) {
        break;
      }
      const { reason } = result.retry;
      const count = used[reason] ?? 0;
      if (count >= RETRY_LIMITS[reason]) {
        break;
      }
      used[reason] = count + 1;
      const waitMs = result.retry.waitMs ?? this.#jitter(NETWORK_WAITS_MS[count]);
      this.#telemetry.event('tm.api.retry', { reason });
      if (!(await this.#wait(waitMs, call.cancel))) {
        result = { ok: false, error: new TmApiError({ kind: 'cancelled', traceId: call.traceId }), retry: null, build: result.build };
        break;
      }
      // Step 1's gates run again before the retry is sent: during the wait the
      // session may have left the tab, or another user may have signed in.
      try {
        this.#admit(call.route, 'read', call.cancel, call.traceId);
      } catch (error) {
        result = { ok: false, error: error as TmApiError, retry: null, build: result.build };
        break;
      }
    }
    handle?.end(this.#ending(result));
    if (result.ok) {
      return result.value;
    }
    throw result.error;
  }

  /**
   * Sends one attempt over `fetch`, under one controller that follows the
   * call's cancellation and the watchdog.
   *
   * @param call - The call.
   * @param reader - How the success body is read.
   * @returns What the attempt produced.
   */
  async #attempt<R, T>(call: TmCall, reader: TmReader<R, T>): Promise<TmAttempt<T>> {
    if (call.cancel?.aborted === true) {
      return this.#unanswered(call, false);
    }
    const controller = new AbortController();
    const follow = (): void => controller.abort();
    call.cancel?.addEventListener('abort', follow, { once: true });
    let timedOut = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const rearm = (): void => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        timedOut = true;
        controller.abort();
      }, call.watchdogMs);
    };
    rearm();
    let response: Response;
    let accepted: boolean;
    let raw: R | string;
    try {
      response = await this.#host.fetch(call.url, {
        method: call.method,
        headers: { ...this.#headers(call.tenantId, call.client, call.traceId, call.body === undefined ? null : 'application/json'), ...call.headers },
        body: call.body,
        credentials: 'same-origin',
        signal: controller.signal,
      });
      this.#connectivity.answered();
      accepted = reader.accepts(response.status);
      // The watchdog covers the body too: a stalled body is as black-holed as a stalled answer.
      raw = accepted ? await reader.read(response, rearm) : await response.text();
    } catch {
      return this.#unanswered(call, timedOut);
    } finally {
      clearTimeout(timer);
      call.cancel?.removeEventListener('abort', follow);
    }
    const headers = response.headers;
    return this.#conclude(call, { status: response.status, header: (name) => headers.get(name) }, accepted, raw, reader.finish);
  }

  /**
   * Sends an upload's one attempt over `XMLHttpRequest`.
   *
   * @param call - The upload.
   * @param file - The file.
   * @param onProgress - Receives the share of the file sent.
   * @returns What the attempt produced.
   */
  #xhrAttempt(call: TmCall, file: Blob, onProgress: ((fraction: number) => void) | undefined): Promise<TmAttempt<TmBlobDescriptor>> {
    return new Promise((resolve) => {
      const xhr = this.#host.xhr();
      const abort = (): void => xhr.abort();
      // The watchdog restarts at each progress event: a slow upload that keeps
      // sending is alive, while a stalled one, or a lost answer, ends.
      let timedOut = false;
      let timer: ReturnType<typeof setTimeout> | undefined;
      const rearm = (): void => {
        clearTimeout(timer);
        timer = setTimeout(() => {
          timedOut = true;
          xhr.abort();
        }, call.watchdogMs);
      };
      const settle = (attempt: Promise<TmAttempt<TmBlobDescriptor>> | TmAttempt<TmBlobDescriptor>): void => {
        clearTimeout(timer);
        call.cancel?.removeEventListener('abort', abort);
        resolve(attempt);
      };
      xhr.open('POST', call.url);
      // An empty type would leave the body undeclared, which the server refuses.
      const headers = this.#headers(call.tenantId, call.client, call.traceId, file.type === '' ? 'application/octet-stream' : file.type);
      for (const [name, value] of Object.entries(headers)) {
        xhr.setRequestHeader(name, value);
      }
      xhr.upload.onprogress = (event): void => {
        rearm();
        if (event.lengthComputable && event.total > 0) {
          onProgress?.(event.loaded / event.total);
        }
      };
      xhr.onload = (): void => {
        this.#connectivity.answered();
        const answer: TmAnswer = { status: xhr.status, header: (name) => xhr.getResponseHeader(name) };
        settle(
          this.#conclude(call, answer, isSuccess(xhr.status), xhr.responseText, (text) => JSON.parse(text) as TmBlobDescriptor),
        );
      };
      xhr.onerror = (): void => settle(this.#unanswered(call, false));
      xhr.onabort = (): void => settle(this.#unanswered(call, timedOut));
      call.cancel?.addEventListener('abort', abort, { once: true });
      rearm();
      xhr.send(file);
    });
  }

  /**
   * The attempt of a call that got no answer: cancelled by its caller or
   * the session, ended by the watchdog, or failed at the network level.
   *
   * @param call - The call.
   * @param timedOut - Whether the watchdog ended it.
   * @returns The failed attempt.
   */
  #unanswered(call: TmCall, timedOut: boolean): TmAttempt<never> {
    const traceId = call.traceId;
    if (call.cancel?.aborted === true) {
      return { ok: false, error: new TmApiError({ kind: 'cancelled', traceId }), retry: null, build: null };
    }
    if (timedOut) {
      // A black-holed call may have run on the server, so its outcome is unknown; it is never retried.
      return { ok: false, error: new TmApiError({ kind: 'offline', code: 'outcome-unknown', traceId }), retry: null, build: null };
    }
    this.#connectivity.failed();
    return {
      ok: false,
      error: new TmApiError({ kind: 'offline', code: call.write ? 'outcome-unknown' : 'network', traceId }),
      // Only a browser that believes it is online retries; an offline one waits for the banner.
      retry: this.#host.onLine() ? { reason: 'network', waitMs: null } : null,
      build: null,
    };
  }

  /**
   * Judges an answer: the response check, the observers,
   * then the value or the refusal.
   *
   * @param call - The call.
   * @param answer - The answer's status and headers.
   * @param accepted - Whether the reader took the status as a success.
   * @param raw - The read body: the reader's on a success, else the text.
   * @param finish - Turns a success body into the call's value.
   * @returns What the attempt produced.
   */
  async #conclude<R, T>(
    call: TmCall,
    answer: TmAnswer,
    accepted: boolean,
    raw: R | string,
    finish: (raw: R, answer: TmAnswer) => T,
  ): Promise<TmAttempt<T>> {
    const build = answer.header('Tellma-Build');
    const traceId = answer.header('Tellma-Trace-Id') || call.traceId;

    // --- Step 4: the response check; another user's answer is discarded ---
    if (call.checksAnswer) {
      try {
        this.#check(traceId);
      } catch (error) {
        return { ok: false, error: error as TmApiError, retry: null, build };
      }
    }

    // --- Step 5: the observers ---
    const tenantId = call.tenantId;
    if (tenantId !== null) {
      const tags = tmParseVersionTags(answer.header('Tellma-Version-Tags'));
      if (tags !== null) {
        // One task later, so the caller adopts its own result before the refresher compares tags.
        setTimeout(() => this.#cache.observe(tenantId, tags, 'header'), 0);
      }
      if (isSuccess(answer.status)) {
        this.#launch.settle(tenantId);
      }
    }

    // --- Step 6: the value, or the refusal ---
    if (!accepted) {
      return this.#refusal(call, answer, raw as string, build);
    }
    try {
      return { ok: true, value: finish(raw as R, answer), status: answer.status, build };
    } catch {
      const error = new TmApiError({ kind: 'server', status: answer.status, code: 'unexpected-response', traceId });
      return { ok: false, error, retry: null, build };
    }
  }

  /**
   * Maps a refused answer to its kind, runs the paths some kinds
   * start, and names the retry its failure allows.
   *
   * @param call - The call.
   * @param answer - The answer's status and headers.
   * @param text - The body's text.
   * @param build - The server's build.
   * @returns The failed attempt.
   */
  async #refusal(call: TmCall, answer: TmAnswer, text: string, build: string | null): Promise<TmAttempt<never>> {
    const status = answer.status;
    const problem = tmParseProblem(text);
    const instance = typeof problem?.instance === 'string' && problem.instance !== '' ? problem.instance : null;
    const traceId = answer.header('Tellma-Trace-Id') || instance || call.traceId;
    const retryAfterMs = tmRetryAfterMs(answer.header('Retry-After'), this.#now());
    const problemCode = problem?.code ?? BARE_CODES[status] ?? null;
    const mapped = tmKindOf(status, problemCode);
    let kind: TmApiErrorKind;
    let code: string | undefined = problemCode ?? undefined;

    // --- The kind, and §5.6's verdict for an outdated side ---
    if (mapped === TM_OUTDATED) {
      ({ kind, code } = await this.#verdict(build));
    } else {
      kind = mapped;
      if (kind === 'server' && problem === null) {
        // A proxy's page, or an answer from outside the app.
        code = 'unexpected-response';
      }
    }
    // A 401 for a session the cookie no longer names (the same user signed in
    // again elsewhere) ends nothing: the session is fine, so the call is cancelled.
    const current = this.#session.profile()?.sh;
    const replaced = call.sh !== undefined && current !== undefined && current !== call.sh;
    if (kind === 'unauthenticated' && call.authenticated && replaced) {
      kind = 'cancelled';
    }
    const error = new TmApiError({
      kind,
      status,
      code,
      traceId,
      problem: problem as TmProblem | null,
      retryAfterMs,
      challenge: kind === 'step-up' ? tmParseStepUp(answer.header('WWW-Authenticate')) : null,
    });

    // --- The paths a kind starts ---
    if (kind === 'unauthenticated' && call.authenticated) {
      this.#paths.expired('401', call.sh);
    }
    if (kind === 'read-only' && call.tenantId !== null) {
      // Every tab's markers and write gates follow the tenant's state.
      this.#cache.refresh(call.tenantId, ['tenant'], 'header').catch(() => undefined);
    }
    if (code === 'contract-mismatch') {
      this.#telemetry.error(error, { route: call.route, build: build ?? '' });
    }

    // --- The retry §10.5 allows; the wait follows the status and headers alone ---
    let retry: TmRetry | null = null;
    if (status === 429) {
      retry = { reason: '429', waitMs: Math.min(retryAfterMs ?? RATE_LIMIT_WAIT_MS, RATE_LIMIT_CAP_MS) };
    } else if (status === 503 && retryAfterMs !== null) {
      retry = { reason: '503', waitMs: Math.min(retryAfterMs, UNAVAILABLE_CAP_MS) };
    } else if (code === 'server-outdated') {
      retry = { reason: 'server-outdated', waitMs: SERVER_OUTDATED_WAIT_MS };
    }
    return { ok: false, error, retry, build };
  }

  /**
   * Decides which side of a `412 client-outdated` or `404 route-not-found`
   * is old: from the builds when they are ordered, else from the
   * worker's update check, capped at 3 s.
   *
   * @param build - The server's build, from `Tellma-Build`.
   * @returns The kind and the code.
   */
  async #verdict(build: string | null): Promise<{ kind: TmApiErrorKind; code: string }> {
    const order = tmCompareBuilds(this.#uiVersion(), build);
    if (order === 0) {
      // The same build refused its own contract: a bug, reported by the caller.
      return { kind: 'server', code: 'contract-mismatch' };
    }
    let verdict: 'client-outdated' | 'server-outdated';
    let source: 'build' | 'worker';
    if (order === 1) {
      verdict = 'client-outdated';
      source = 'build';
      // So an old tab whose only calls are background syncs still learns of the update.
      this.#updates.check().catch(() => undefined);
    } else if (order === -1 && build !== null && this.#firstOlderVerdict(build)) {
      verdict = 'server-outdated';
      source = 'build';
    } else {
      // Unordered builds, or a second older verdict for the same build past
      // the first one's retry window (a rollback): the worker decides, and
      // only a confirmed `none` blames the server.
      verdict = (await this.#askWorker()) === 'none' ? 'server-outdated' : 'client-outdated';
      source = 'worker';
    }
    this.#telemetry.event('tm.api.client-outdated', { verdict, source });
    return { kind: 'client-outdated', code: verdict };
  }

  /**
   * Whether an older server build's verdict counts as its first: the first
   * verdict for the build, or one within the first one's retry window, which
   * the same slot swap answered.
   *
   * @param build - The older server build.
   * @returns `true` for a first verdict, `false` for a second one.
   */
  #firstOlderVerdict(build: string): boolean {
    const now = this.#now();
    const first = this.#judgedOlder.get(build);
    if (first === undefined) {
      this.#judgedOlder.set(build, now);
      return true;
    }
    return now - first < SERVER_OUTDATED_WAIT_MS;
  }

  /**
   * The worker's update check, or `'timeout'` past the 3 s cap.
   *
   * @returns The check's result.
   */
  async #askWorker(): Promise<TmApiUpdateCheck | 'timeout'> {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const cap = new Promise<'timeout'>((resolve) => {
      timer = setTimeout(() => resolve('timeout'), WORKER_CAP_MS);
    });
    try {
      return await Promise.race([this.#updates.check().catch((): TmApiUpdateCheck => 'failed'), cap]);
    } finally {
      clearTimeout(timer);
    }
  }

  /**
   * The UI version, from the client metadata.
   *
   * @returns The version, such as `1.4.0+3f9c2ab`.
   */
  #uiVersion(): string {
    return this.#clientId().slice('web/'.length);
  }

  /**
   * A network retry's wait with ±25 % jitter, so a fleet of tabs does not
   * retry in step.
   *
   * @param baseMs - The wait before jitter.
   * @returns The jittered wait.
   */
  #jitter(baseMs: number): number {
    const unit = this.#host.random(new Uint8Array(1))[0] / 255;
    return Math.round(baseMs * (1 - NETWORK_JITTER + 2 * NETWORK_JITTER * unit));
  }

  /**
   * Waits before a retry; an abort ends the wait at once.
   *
   * @param ms - The wait.
   * @param cancel - The call's cancellation, if any.
   * @returns `true` when the wait ran out, `false` when it was aborted.
   */
  #wait(ms: number, cancel: AbortSignal | null): Promise<boolean> {
    return new Promise((resolve) => {
      if (cancel?.aborted === true) {
        resolve(false);
        return;
      }
      const stop = (): void => {
        clearTimeout(timer);
        resolve(false);
      };
      const timer = setTimeout(() => {
        cancel?.removeEventListener('abort', stop);
        resolve(true);
      }, ms);
      cancel?.addEventListener('abort', stop, { once: true });
    });
  }

  /**
   * What the request log records of a settled call.
   *
   * @param result - The call's last attempt.
   * @returns The log's end record.
   */
  #ending(result: TmAttempt<unknown>): TmRequestEnd {
    const status = result.ok ? result.status : result.error.status;
    return {
      outcome: result.ok ? 'ok' : result.error.kind === 'cancelled' ? 'cancelled' : 'error',
      ...(status > 0 ? { status } : {}),
      ...(result.ok ? {} : { code: result.error.code }),
      ...(result.build === null ? {} : { serverBuild: result.build }),
    };
  }
}
