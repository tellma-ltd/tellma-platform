// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injectable, signal, type Signal } from '@angular/core';
import {
  TmApi,
  TmApiError,
  type TmBlobDescriptor,
  type TmDownload,
  type TmDownloadOptions,
  type TmReadOptions,
  type TmSendOptions,
  type TmUploadOptions,
  type TmWriteOptions,
} from '@tellma/core';

/** What kind of call a {@link TmFakeApi} recorded. */
export type TmFakeApiCallKind = 'read' | 'write' | 'download' | 'upload' | 'fetch-blob' | 'send';

/** One call a {@link TmFakeApi} received. */
export interface TmFakeApiCall {
  /** What kind of call it was. */
  readonly kind: TmFakeApiCallKind;
  /** The route; an upload's is `blobs/<kind>`, and a blob fetch's is its URL. */
  readonly route: string;
  /** The body; an upload's is the file, and a blob fetch has none. */
  readonly body: unknown;
  /** The options the call received, or an empty object. */
  readonly options: object;
}

/**
 * How a {@link TmFakeApi} answers a route: from the call, with a value, a
 * promise of one, or a thrown error, such as a `TmApiError`.
 */
export type TmFakeApiResponder = (call: TmFakeApiCall) => unknown;

/**
 * A {@link TmApi} that answers from a script and records every call, so a
 * test runs a page or a service with no server. A route with no answer
 * rejects with an error naming it; an aborted read, download or upload
 * rejects as the real layer does, with a `cancelled` `TmApiError`, and an
 * aborted blob fetch with its signal's reason. Under `provideTellmaTesting()`,
 * the images `tm-image` fetches are scripted by URL, such as `/17/blobs/51`.
 *
 * @example
 * ```ts
 * TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
 * const api = TestBed.inject(TmFakeApi);
 * api.respond('centers/query', { result: [], count: 0 });
 * api.respond('centers/save', () => {
 *   throw new TmApiError({ kind: 'validation', status: 422, code: 'validation' });
 * });
 * // … run the code under test …
 * expect(api.calls().map((call) => call.route)).toEqual(['centers/query', 'centers/save']);
 * ```
 */
@Injectable()
export class TmFakeApi extends TmApi {
  readonly #calls = signal<readonly TmFakeApiCall[]>([]);
  readonly #online = signal(true);
  readonly #writes = signal(0);
  readonly #responders = new Map<string, TmFakeApiResponder>();
  readonly #fingerprints = new Map<string, string>();

  /** Every call received, oldest first. */
  readonly calls: Signal<readonly TmFakeApiCall[]> = this.#calls.asReadonly();

  /** Whether the server is reachable; true until {@link TmFakeApi.setOnline} says otherwise. */
  override readonly online: Signal<boolean> = this.#online.asReadonly();

  /** The writes whose answers are still pending. */
  override readonly writesInFlight: Signal<number> = this.#writes.asReadonly();

  /**
   * Scripts a route's answer. A function answers each call from the call;
   * any other value is the answer of every call. A later script replaces an
   * earlier one.
   *
   * @param route - The route, such as `centers/query`; an upload's is `blobs/<kind>`, and a blob fetch's is its URL.
   * @param answer - The answer, or the function that answers.
   */
  respond(route: string, answer: unknown): void {
    this.#responders.set(route, typeof answer === 'function' ? (answer as TmFakeApiResponder) : () => answer);
  }

  /**
   * Sets whether the server is reachable.
   *
   * @param online - The new value of {@link TmFakeApi.online}.
   */
  setOnline(online: boolean): void {
    this.#online.set(online);
  }

  /**
   * Sets the contract fingerprint {@link TmFakeApi.fingerprint} returns for a route.
   *
   * @param route - The route.
   * @param fingerprint - The fingerprint, or `null` for a route outside the map.
   */
  setFingerprint(route: string, fingerprint: string | null): void {
    if (fingerprint === null) {
      this.#fingerprints.delete(route);
    } else {
      this.#fingerprints.set(route, fingerprint);
    }
  }

  /** Records the read and answers it from the script. */
  override read<T>(route: string, body: unknown, options: TmReadOptions): Promise<T> {
    return this.#answer({ kind: 'read', route, body, options }, options.signal) as Promise<T>;
  }

  /** Records the write and answers it from the script, counting it in flight meanwhile. */
  override async write<T>(route: string, body: unknown, options: TmWriteOptions = {}): Promise<T> {
    this.#writes.update((count) => count + 1);
    try {
      return (await this.#answer({ kind: 'write', route, body, options }, null)) as T;
    } finally {
      this.#writes.update((count) => count - 1);
    }
  }

  /** Records the download and answers it from the script. */
  override download(route: string, body: unknown, options: TmDownloadOptions): Promise<TmDownload> {
    return this.#answer({ kind: 'download', route, body, options }, options.signal) as Promise<TmDownload>;
  }

  /** Records the upload and answers it from the script under `blobs/<kind>`. */
  override upload(kind: string, file: Blob, options: TmUploadOptions): Promise<TmBlobDescriptor> {
    return this.#answer({ kind: 'upload', route: `blobs/${kind}`, body: file, options }, options.signal) as Promise<TmBlobDescriptor>;
  }

  /** Records the blob fetch and answers it from the script under its URL. */
  override fetchBlob(url: string, options: Parameters<TmApi['fetchBlob']>[1] = {}): ReturnType<TmApi['fetchBlob']> {
    const signal = options.signal ?? null;
    return this.#answer({ kind: 'fetch-blob', route: url, body: undefined, options }, signal).catch((error: unknown) => {
      // As the real layer does, an aborted fetch rejects with its signal's reason.
      throw signal?.aborted === true ? (signal.reason as unknown) : error;
    }) as ReturnType<TmApi['fetchBlob']>;
  }

  /** Records the send; its answer is never read, so it needs no script. */
  override send(route: string, body: unknown, options: TmSendOptions = {}): void {
    this.#calls.update((calls) => [...calls, { kind: 'send', route, body, options }]);
  }

  /** The fingerprint {@link TmFakeApi.setFingerprint} set, or `null`. */
  override fingerprint(route: string): Promise<string | null> {
    return Promise.resolve(this.#fingerprints.get(route) ?? null);
  }

  /**
   * Records a call and answers it from the script, unless its signal aborts first.
   *
   * @param call - The call.
   * @param signal - The call's signal, or `null` for a write.
   * @returns The answer.
   */
  #answer(call: TmFakeApiCall, signal: AbortSignal | null): Promise<unknown> {
    this.#calls.update((calls) => [...calls, call]);
    const responder = this.#responders.get(call.route);
    if (responder === undefined) {
      return Promise.reject(new Error(`TmFakeApi has no answer for '${call.route}'; script one with respond().`));
    }
    if (signal?.aborted === true) {
      return Promise.reject(new TmApiError({ kind: 'cancelled' }));
    }
    // The answer runs a microtask later, as a real call never settles synchronously.
    const answer = Promise.resolve().then(() => responder(call));
    if (signal === null) {
      return answer;
    }
    return new Promise((resolve, reject) => {
      const cancel = (): void => reject(new TmApiError({ kind: 'cancelled' }));
      signal.addEventListener('abort', cancel, { once: true });
      void answer.then(resolve, reject).finally(() => signal.removeEventListener('abort', cancel));
    });
  }
}
