// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, InjectionToken } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

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
}

/**
 * Fetches a binary resource. `tm-image` (and the blob cache behind it)
 * performs ALL its network access through this seam.
 */
export type TmBlobFetcher = (url: string, options?: TmBlobFetchOptions) => Promise<TmBlobFetchResult>;

/**
 * The blob-fetching seam of `tm-image`. The default implementation rides
 * `HttpClient`, so the app's interceptors apply — auth headers (and later
 * BFF cookies) reach image requests without any component knowledge.
 * Provide a custom implementation to swap the transport wholesale.
 */
export const TM_BLOB_FETCHER = new InjectionToken<TmBlobFetcher>('TM_BLOB_FETCHER', {
  providedIn: 'root',
  factory: (): TmBlobFetcher => {
    const http = inject(HttpClient);
    return async (url, options) => {
      let headers = new HttpHeaders();
      if (options?.ifNoneMatch !== undefined) {
        headers = headers.set('If-None-Match', options.ifNoneMatch);
      }
      try {
        const response = await firstValueFrom(
          http.get(url, { observe: 'response', responseType: 'blob', headers }),
        );
        return { status: response.status, blob: response.body, etag: response.headers.get('ETag') };
      } catch (error) {
        if (error instanceof HttpErrorResponse && error.status === 304) {
          return { status: 304, blob: null, etag: error.headers.get('ETag') };
        }
        throw error;
      }
    };
  },
});
