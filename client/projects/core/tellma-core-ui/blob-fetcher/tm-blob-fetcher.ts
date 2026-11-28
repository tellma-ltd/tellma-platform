// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken, Injector, isDevMode } from '@angular/core';
import { Subscription } from 'rxjs';
// Types only: the module itself is loaded at the first fetch.
import type * as NgHttp from '@angular/common/http';

/** One fetched blob response, as {@link TmBlobFetcher} reports it. */
export interface TmBlobFetchResult {
  /** The HTTP status (`304` for a not-modified revalidation). */
  readonly status: number;
  /** The response body; `null` on `304`. */
  readonly blob: Blob | null;
  /** The response's `ETag` header, when present. */
  readonly etag: string | null;
}

/** Options of a {@link TmBlobFetcher} call. */
export interface TmBlobFetchOptions {
  /** Sends `If-None-Match` for a conditional (revalidation) request. */
  readonly ifNoneMatch?: string;
  /**
   * Aborts the request. A fetcher stops the request on abort and rejects
   * with `signal.reason`, at once if the signal is already aborted. A
   * fetcher that ignores it still works, without cancellation.
   */
  readonly signal?: AbortSignal;
}

/**
 * Fetches a binary resource. `tm-image` (and the blob cache behind it)
 * performs ALL its network access through this seam.
 */
export type TmBlobFetcher = (url: string, options?: TmBlobFetchOptions) => Promise<TmBlobFetchResult>;

/**
 * Loads `@angular/common/http` and the app's `HttpClient` for the default
 * fetcher. In development a failure (a chunk gone after a deploy, or a client
 * whose providers throw) is logged, because `tm-image` turns a rejected
 * fetch into its error placeholder silently.
 *
 * @param injector - The root injector, which holds the app's `HttpClient`.
 * @returns The module and the client.
 */
async function loadHttp(injector: Injector): Promise<{
  readonly module: typeof NgHttp;
  readonly client: NgHttp.HttpClient;
}> {
  try {
    // A dynamic import keeps HttpClient out of the eager bundle of every app
    // that overrides this token: a static one would keep it there, since the
    // bundler cannot tell that the factory never runs.
    const module = await import('@angular/common/http');
    return { module, client: injector.get(module.HttpClient) };
  } catch (error) {
    if (isDevMode()) {
      console.error(
        "tm-image's default TM_BLOB_FETCHER could not load HttpClient, so every image shows its error placeholder; fix the app's HttpClient providers, or provide TM_BLOB_FETCHER.",
        error,
      );
    }
    throw error;
  }
}

/**
 * The blob-fetching seam of `tm-image`. The default implementation rides
 * `HttpClient`, so the app's interceptors apply — auth headers (and later
 * BFF cookies) reach image requests without any component knowledge — and
 * an abort unsubscribes, which cancels the request in flight. It loads
 * `@angular/common/http` at its first fetch, so an app that provides its own
 * fetcher never ships it eagerly. `HttpClient` is provided at the root;
 * `provideHttpClient()` configures it, with the app's interceptors.
 * Provide a custom implementation to swap the transport wholesale; one over
 * `fetch` passes the signal straight through.
 */
export const TM_BLOB_FETCHER = new InjectionToken<TmBlobFetcher>('TM_BLOB_FETCHER', {
  providedIn: 'root',
  factory: (): TmBlobFetcher => {
    const injector = inject(Injector);
    return async (url, options) => {
      const signal = options?.signal;
      signal?.throwIfAborted();
      const { module, client } = await loadHttp(injector);
      // An abort while the module loaded rejects with its reason, unsent.
      signal?.throwIfAborted();
      return new Promise<TmBlobFetchResult>((resolve, reject) => {
        let headers = new module.HttpHeaders();
        if (options?.ifNoneMatch !== undefined) {
          headers = headers.set('If-None-Match', options.ifNoneMatch);
        }
        // Holds the request; unsubscribing it cancels the request, even one
        // added after the abort (a closed subscription tears down at once).
        const subscription = new Subscription();
        // The abort reason is the rejection.
        const onAbort = (): void => {
          subscription.unsubscribe();
          reject(signal?.reason);
        };
        // Detaches the abort listener once the request has settled.
        const settle = (): void => signal?.removeEventListener('abort', onAbort);
        // Listening first: an interceptor may settle the request synchronously.
        signal?.addEventListener('abort', onAbort, { once: true });
        subscription.add(
          client.get(url, { observe: 'response', responseType: 'blob', headers }).subscribe({
            next: (response) => {
              settle();
              resolve({
                status: response.status,
                blob: response.body,
                etag: response.headers.get('ETag'),
              });
            },
            error: (error: unknown) => {
              settle();
              // A not-modified revalidation surfaces as an HTTP error.
              if (error instanceof module.HttpErrorResponse && error.status === 304) {
                resolve({ status: 304, blob: null, etag: error.headers.get('ETag') });
              } else {
                reject(error);
              }
            },
          }),
        );
      });
    };
  },
});
