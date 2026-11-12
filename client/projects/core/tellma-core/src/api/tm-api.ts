// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, type Signal } from '@angular/core';
import type { TmBlobFetchOptions, TmBlobFetchResult } from '@tellma/core-ui/image';

import type { TmDecimal } from '../json/tm-json';
import { TmHttpApi } from './tm-http-api';

// The HTTP layer's public contract (spec 0024 §10.1). Every request the app
// makes goes through TmApi: reads are cancellable and retried by class,
// writes are never aborted or retried, and a user check runs before and after
// every authenticated call. The root default is the real layer over `fetch`;
// a test provides a fake.

/** Options of a read: a call that is cancellable and retried. */
export interface TmReadOptions {
  /** Cancels the read: every read is cancellable, and an abort also ends a retry's wait. */
  readonly signal: AbortSignal;
  /** Marks the read as background work: it renews no session and shows no dialog. */
  readonly background?: boolean;
  /** The tenant to call; the routed tenant when omitted. Ignored on a tenantless route. */
  readonly tenantId?: number;
  /** `GET`, for a tenantless route such as `/bff/user`; every other read is a `POST`. */
  readonly method?: 'GET';
  /** Gives a route the server does not describe the 6-minute allowance of a long operation. */
  readonly long?: boolean;
  /** Runs the read in the server's low-priority lane, as a dashboard tile does. */
  readonly priority?: 'low';
}

/** Options of a write: a call that is never aborted and never retried. */
export interface TmWriteOptions {
  /** The tenant to call; the routed tenant when omitted. Ignored on a tenantless route. */
  readonly tenantId?: number;
  /** Gives a route the server does not describe the 6-minute allowance of a long operation. */
  readonly long?: boolean;
  /** The keys of the warnings the user confirmed, at most 16 of at most 100 ASCII characters each. */
  readonly confirmations?: readonly string[];
}

/** Options of a download: a read that also reports the bytes received. */
export interface TmDownloadOptions extends TmReadOptions {
  /** Reports the bytes received so far, as they arrive. */
  readonly onProgress?: (bytes: number) => void;
}

/** Options of an upload. */
export interface TmUploadOptions {
  /** The file's name, as the user picked it. */
  readonly fileName: string;
  /** Cancels the upload, as removing the file or leaving the page does. */
  readonly signal: AbortSignal;
  /** Reports the share of the file sent so far, from 0 to 1. */
  readonly onProgress?: (fraction: number) => void;
  /** The tenant to upload to; the routed tenant when omitted. */
  readonly tenantId?: number;
}

/** Options of a fire-and-forget send. */
export interface TmSendOptions {
  /** Marks the send as background work, which renews no session. */
  readonly background?: boolean;
}

/** Options of a blob fetch: `tm-image`'s, and the signal that aborts it. */
export interface TmFetchBlobOptions extends TmBlobFetchOptions {
  /** Aborts the fetch; the fetch then rejects with the signal's reason. */
  readonly signal?: AbortSignal;
}

/** A downloaded file. */
export interface TmDownload {
  /** The file's name, from the response's `Content-Disposition`. */
  readonly fileName: string;
  /** The file's media type. */
  readonly contentType: string;
  /** The file's bytes. */
  readonly blob: Blob;
}

/** A staged upload, as the server describes it; a record save attaches it by `id`. */
export interface TmBlobDescriptor {
  /** The blob's id. */
  readonly id: number;
  /** The blob kind it was staged under. */
  readonly kind: string;
  /** The media type the server determined. */
  readonly contentType: string;
  /** The size in bytes, as exact decimal text. */
  readonly size: TmDecimal;
  /** An image's width in pixels. */
  readonly width?: number;
  /** An image's height in pixels. */
  readonly height?: number;
  /** The file's name, as uploaded. */
  readonly fileName?: string;
  /** When the staged blob expires unless a save attaches it, as the server's instant text. */
  readonly expiresAt?: string;
}

/**
 * The app's one HTTP layer. Routes are relative to the routed tenant's API
 * (`'centers/query'` calls `/17/api/web/centers/query`); a route with a
 * leading `/` is tenantless and sent as is. Every call rejects with a
 * `TmApiError`.
 *
 * @example
 * ```ts
 * const api = inject(TmApi);
 * const page = resource({
 *   params: () => ({ filter: this.filter() }),
 *   loader: ({ params, abortSignal }) => api.read<CenterPage>('centers/query', params, { signal: abortSignal }),
 * });
 * await api.write('centers/save', { entities: [center] }, { confirmations: ['Gl.CenterReparented'] });
 * ```
 */
@Injectable({ providedIn: 'root', useFactory: () => inject(TmHttpApi) })
export abstract class TmApi {
  /** Whether the server is reachable, as far as this tab knows; the offline banner reads it. */
  abstract readonly online: Signal<boolean>;

  /** This tab's unsettled writes. */
  abstract readonly writesInFlight: Signal<number>;

  /**
   * Reads: a `POST` route the server lists as safe to retry, or a `GET`.
   * The read is retried on a network failure, a `429` and a `503` that asks
   * for a wait, and aborting its signal ends it at once.
   *
   * @param route - The route, such as `centers/query`, or a tenantless one such as `/bff/user`.
   * @param body - The request body, sent as JSON.
   * @param options - The signal, and how to send the read.
   * @returns The response body, parsed as plain JSON.
   */
  abstract read<T>(route: string, body: unknown, options: TmReadOptions): Promise<T>;

  /**
   * Writes: any route, sent once and never aborted. A write while the browser
   * is offline is not sent.
   *
   * @param route - The route, such as `centers/save`.
   * @param body - The request body, sent as JSON.
   * @param options - The tenant, the long allowance and the confirmed warnings.
   * @returns The response body, parsed as plain JSON.
   */
  abstract write<T>(route: string, body: unknown, options?: TmWriteOptions): Promise<T>;

  /**
   * Downloads a file, such as an Excel export, as a read.
   *
   * @param route - The route, such as `centers/export`.
   * @param body - The request body, sent as JSON.
   * @param options - The signal, how to send the read, and the progress callback.
   * @returns The file, named from the response.
   */
  abstract download(route: string, body: unknown, options: TmDownloadOptions): Promise<TmDownload>;

  /**
   * Stages a file as a blob of a kind, reporting its progress. An upload is
   * never retried.
   *
   * @param kind - The blob kind, such as `customer-image`.
   * @param file - The file.
   * @param options - Its name, the signal and the progress callback.
   * @returns The staged blob, which a record save attaches by its id.
   */
  abstract upload(kind: string, file: Blob, options: TmUploadOptions): Promise<TmBlobDescriptor>;

  /**
   * Fetches the bytes of an image or another blob for `tm-image`, whose
   * `TM_BLOB_FETCHER` `provideTellma` points here. A `GET`, revalidated with
   * `If-None-Match`, never retried, and kept out of the request log.
   *
   * @param url - The blob's URL, such as `/17/blobs/51?size=64`.
   * @param options - The held ETag, and the signal that aborts the fetch.
   * @returns The status (`304` when the held copy is current), the bytes and the ETag.
   */
  abstract fetchBlob(url: string, options?: TmFetchBlobOptions): Promise<TmBlobFetchResult>;

  /**
   * Sends a small body that outlives the page and whose answer is never read,
   * as when the page hides. A body over 16 KB throws in development and is
   * dropped in production.
   *
   * @param route - The route.
   * @param body - The request body, sent as JSON.
   * @param options - Whether the send is background work.
   */
  abstract send(route: string, body: unknown, options?: TmSendOptions): void;

  /**
   * The contract fingerprint of a route in the running build, which a draft
   * stamps so a restore under another build can tell the route moved.
   *
   * @param route - The route, such as `centers/save`.
   * @returns The fingerprint, or `null` for a route the server does not describe.
   */
  abstract fingerprint(route: string): Promise<string | null>;
}
