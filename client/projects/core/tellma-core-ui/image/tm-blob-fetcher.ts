// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Subscription } from 'rxjs';

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
 * The blob-fetching seam of `tm-image`. The default implementation rides
 * `HttpClient`, so the app's interceptors apply — auth headers (and later
 * BFF cookies) reach image requests without any component knowledge — and
 * an abort unsubscribes, which cancels the request in flight.
 * Provide a custom implementation to swap the transport wholesale; one over
 * `fetch` passes the signal straight through.
 */
export const TM_BLOB_FETCHER = new InjectionToken<TmBlobFetcher>('TM_BLOB_FETCHER', {
  providedIn: 'root',
  factory: (): TmBlobFetcher => {
    const http = inject(HttpClient);
    return (url, options) =>
      new Promise<TmBlobFetchResult>((resolve, reject) => {
        const signal = options?.signal;
        if (signal?.aborted) {
          reject(signal.reason);
          return;
        }
        let headers = new HttpHeaders();
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
          http.get(url, { observe: 'response', responseType: 'blob', headers }).subscribe({
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
              if (error instanceof HttpErrorResponse && error.status === 304) {
                resolve({ status: 304, blob: null, etag: error.headers.get('ETag') });
              } else {
                reject(error);
              }
            },
          }),
        );
      });
  },
});
