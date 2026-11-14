// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { inject, Injectable, InjectionToken } from '@angular/core';

import { TM_BLOB_FETCHER } from '../tm-blob-fetcher';

/**
 * The `CacheStorage` seam — `null` where the Cache API is unavailable
 * (insecure context, restrictive browser profile), which runs the whole
 * pipeline cache-less from the start. Unit tests provide an in-memory
 * implementation.
 * @internal
 */
export const ɵTM_CACHE_STORAGE = new InjectionToken<CacheStorage | null>('ɵTM_CACHE_STORAGE', {
  providedIn: 'root',
  factory: () => (typeof caches === 'undefined' ? null : caches),
});

/**
 * Options of {@link ɵTmImageBlobCache.getImage}.
 * @internal
 */
export interface ɵTmImageRequestOptions {
  /** Reports a blob that a background revalidation fetched in place of the served one. */
  readonly onRefresh?: (blob: Blob) => void;
  /**
   * The caller's wait. Aborting it rejects the call with `signal.reason`
   * and releases the caller's hold on the shared request and on any
   * background revalidation of the URL. A caller without a signal holds
   * both until they finish.
   */
  readonly signal?: AbortSignal;
}

/** Swept by `TmClientCache.clearAll()` through its `tm-` prefix. */
const CACHE_NAME = 'tm-images-v1';
/** The record's version stamp (the `etag` input) at put time. */
const VERSION_HEADER = 'x-tm-version';
/** The response's own `ETag`, for `If-None-Match` revalidation. */
const ETAG_HEADER = 'x-tm-etag';

/** An operation on one URL, shared by every caller and held open by them. */
interface Shared {
  /** Aborts the operation once its last holder has gone. */
  readonly controller: AbortController;
  /** The callers still holding the operation open. */
  holders: number;
}

/** The one network request of a URL, shared by every waiter. */
interface SharedRequest extends Shared {
  /** The blob, written to the cache before it resolves. */
  readonly blob: Promise<Blob>;
}

/** One instance's hold on a background revalidation. */
interface RevalidationHold {
  /** Swaps in the fresh bytes. */
  readonly onRefresh: ((blob: Blob) => void) | undefined;
  /** Detaches the instance's abort listener. */
  release: () => void;
}

/** The one background revalidation of a URL, shared by every instance showing it. */
interface Revalidation extends Shared {
  /** The instances still showing the URL. */
  readonly refreshes: Set<RevalidationHold>;
}

/**
 * Holds `entry` for one caller until `signal` aborts (a caller without a
 * signal holds it for good). On abort `leave` runs, and the last holder out
 * drops the entry from `map` (so a later caller starts afresh) and aborts it.
 *
 * @returns Detaches the caller's abort listener once the entry has settled.
 */
function hold<T extends Shared>(
  map: Map<string, T>,
  url: string,
  entry: T,
  signal: AbortSignal | undefined,
  leave: () => void,
): () => void {
  entry.holders += 1;
  const onAbort = (): void => {
    leave();
    if (--entry.holders === 0) {
      if (map.get(url) === entry) {
        map.delete(url);
      }
      entry.controller.abort();
    }
  };
  signal?.addEventListener('abort', onAbort, { once: true });
  return () => signal?.removeEventListener('abort', onAbort);
}

/**
 * A URL with its `size` rendition param removed — the identity that
 * groups one image's size variants. Purging by this (instead of the Cache
 * API's `ignoreSearch`, which drops the WHOLE query) keeps query-keyed
 * endpoints safe: `/api/images?id=7&size=64` must never purge
 * `/api/images?id=9&size=64`.
 */
function withoutSizeParam(url: string): string {
  try {
    // Resolved against the document base: cache keys come back absolute,
    // while callers may pass app-relative URLs — both sides must compare
    // in the same shape.
    const parsed = new URL(url, document.baseURI);
    parsed.searchParams.delete('size');
    return parsed.toString();
  } catch {
    return url;
  }
}

/**
 * The origin-scoped image blob cache (`tm-images-v1`) — entries are
 * tenant-scoped through their keys, because the cache key is the full
 * request URL and the tenant id is part of every API URL by platform
 * convention.
 *
 * Semantics per version stamp:
 * - stamp matches the stored one → serve from cache, no network;
 * - stamp differs → purge every size variant of the src (`ignoreSearch`)
 *   and refetch;
 * - stamp unknown (`null`) → serve immediately, revalidate in the
 *   background with `If-None-Match` (304 keeps, 200 replaces + reports).
 *
 * Concurrent fetches of one URL coalesce into one request, and concurrent
 * revalidations into one conditional request; both are reference-counted.
 * The shared request is aborted only when every waiter has aborted, and a
 * revalidation when no instance showing the URL remains. An aborted
 * request writes nothing to the cache. A failing `put` (quota) drops the
 * whole cache and continues uncached — the cache is an accelerator, never
 * required.
 *
 * @internal Consumed by `TmImage`; never use directly.
 */
@Injectable({ providedIn: 'root' })
export class ɵTmImageBlobCache {
  private readonly storage = inject(ɵTM_CACHE_STORAGE);
  private readonly fetcher = inject(TM_BLOB_FETCHER);
  /** The shared request per URL, while it is in flight and not abandoned. */
  private readonly inflight = new Map<string, SharedRequest>();
  /**
   * The background revalidation per URL, holding EVERY waiting caller's
   * refresh callback — one conditional GET, but all instances swap in the
   * fresh bytes (else same-URL siblings show two different images).
   */
  private readonly revalidating = new Map<string, Revalidation>();
  /** Set on a failed put: the pipeline continues uncached from then on. */
  private cacheBroken = false;

  /**
   * Resolves the blob for a sized URL under the record stamp `version`.
   * Rejects with the signal's reason once `options.signal` aborts.
   */
  async getImage(
    url: string,
    version: string | null,
    options?: ɵTmImageRequestOptions,
  ): Promise<Blob> {
    const signal = options?.signal;
    signal?.throwIfAborted();
    if (version === '') {
      // An empty stamp is "unknown", never a value: storing/comparing ''
      // would turn two unknowns into a permanent, unrevalidated hit.
      version = null;
    }
    const cache = await this.openCache();
    signal?.throwIfAborted();
    if (cache === null) {
      return this.fetchShared(url, version, null, signal);
    }
    let match: Response | undefined;
    try {
      match = await cache.match(url);
    } catch {
      match = undefined;
    }
    signal?.throwIfAborted();
    if (match !== undefined) {
      const stored = match.headers.get(VERSION_HEADER);
      if (version !== null && stored === version) {
        return match.blob();
      }
      if (version === null) {
        const blob = await match.blob();
        // An instance that left meanwhile never joins the revalidation.
        signal?.throwIfAborted();
        this.revalidate(cache, url, match.headers.get(ETAG_HEADER), options);
        return blob;
      }
      try {
        // Purge the SIZE VARIANTS of this src only — never every entry
        // sharing the path (query-keyed endpoints put the record id in
        // the query, where the Cache API's ignoreSearch would blast it).
        const target = withoutSizeParam(url);
        for (const request of await cache.keys()) {
          if (withoutSizeParam(request.url) === target) {
            await cache.delete(request);
          }
        }
      } catch {
        // Purge is best-effort; the refetch below overwrites this key.
      }
      signal?.throwIfAborted();
    }
    return this.fetchShared(url, version, cache, signal);
  }

  private async openCache(): Promise<Cache | null> {
    if (this.storage === null || this.cacheBroken) {
      return null;
    }
    try {
      return await this.storage.open(CACHE_NAME);
    } catch {
      return null;
    }
  }

  /**
   * Joins (or starts) the shared request for `url`. The caller holds it
   * until it settles or the caller's `signal` aborts; the request itself is
   * aborted only when its last holder lets go.
   */
  private fetchShared(
    url: string,
    version: string | null,
    cache: Cache | null,
    signal: AbortSignal | undefined,
  ): Promise<Blob> {
    let shared = this.inflight.get(url);
    if (shared === undefined) {
      const controller = new AbortController();
      const blob = (async (): Promise<Blob> => {
        const response = await this.fetcher(url, { signal: controller.signal });
        // A fetcher that ignores the signal may still answer after the
        // abort: its bytes are dropped, never written.
        controller.signal.throwIfAborted();
        if (response.blob === null) {
          throw new Error(`tm-image: the fetcher returned no body for ${url}`);
        }
        if (cache !== null) {
          await this.put(cache, url, response.blob, version ?? response.etag, response.etag);
        }
        return response.blob;
      })();
      const entry: SharedRequest = { controller, blob, holders: 0 };
      // Settled (or abandoned) requests leave the map; a later caller
      // starts afresh. Handling both outcomes also keeps a request every
      // holder abandoned from surfacing as an unhandled rejection.
      const forget = (): void => {
        if (this.inflight.get(url) === entry) {
          this.inflight.delete(url);
        }
      };
      void blob.then(forget, forget);
      this.inflight.set(url, entry);
      shared = entry;
    }
    const entry = shared;
    // Each caller waits on its own promise: its abort rejects only its wait.
    return new Promise<Blob>((resolve, reject) => {
      const release = hold(this.inflight, url, entry, signal, () => reject(signal?.reason));
      entry.blob.finally(release).then(resolve, reject);
    });
  }

  /** Writes a matched (blob, stamp) pair; a failure drops the cache. */
  private async put(
    cache: Cache,
    url: string,
    blob: Blob,
    version: string | null,
    etag: string | null,
  ): Promise<void> {
    const headers = new Headers({ 'content-type': blob.type || 'application/octet-stream' });
    if (version !== null && version !== '') {
      // An absent header means "stamp unknown" — never store '' where a
      // later '' input could turn two unknowns into a permanent hit.
      headers.set(VERSION_HEADER, version);
    }
    if (etag !== null) {
      headers.set(ETAG_HEADER, etag);
    }
    try {
      await cache.put(url, new Response(blob, { headers }));
    } catch {
      this.cacheBroken = true;
      try {
        await this.storage?.delete(CACHE_NAME);
      } catch {
        // Even the drop failed — carry on uncached regardless.
      }
    }
  }

  /**
   * Joins (or starts) the background revalidation of `url`. The caller
   * holds it until it finishes or the caller's signal aborts; it is aborted
   * when its last holder lets go.
   */
  private revalidate(
    cache: Cache,
    url: string,
    storedEtag: string | null,
    options: ɵTmImageRequestOptions | undefined,
  ): void {
    let entry = this.revalidating.get(url);
    const started = entry === undefined;
    if (entry === undefined) {
      entry = { controller: new AbortController(), holders: 0, refreshes: new Set() };
      this.revalidating.set(url, entry);
    }
    const refreshes = entry.refreshes;
    // The instance leaves: its swap is dropped, and the conditional GET is
    // aborted when no instance showing the URL remains.
    const refresh: RevalidationHold = { onRefresh: options?.onRefresh, release: () => undefined };
    refreshes.add(refresh);
    refresh.release = hold(this.revalidating, url, entry, options?.signal, () =>
      refreshes.delete(refresh),
    );
    if (started) {
      void this.runRevalidation(cache, url, storedEtag, entry);
    }
  }

  /** Runs one conditional GET and hands a fresh blob to every holder. */
  private async runRevalidation(
    cache: Cache,
    url: string,
    storedEtag: string | null,
    entry: Revalidation,
  ): Promise<void> {
    const signal = entry.controller.signal;
    try {
      const response = await this.fetcher(
        url,
        storedEtag !== null ? { ifNoneMatch: storedEtag, signal } : { signal },
      );
      // Still current, or abandoned: an aborted answer is never written.
      if (signal.aborted || response.status === 304 || response.blob === null) {
        return;
      }
      await this.put(cache, url, response.blob, response.etag, response.etag);
      for (const refresh of entry.refreshes) {
        refresh.onRefresh?.(response.blob);
      }
    } catch {
      // Background refresh is best-effort; the stale entry stays valid.
    } finally {
      if (this.revalidating.get(url) === entry) {
        this.revalidating.delete(url);
      }
      for (const refresh of entry.refreshes) {
        refresh.release();
      }
    }
  }
}
