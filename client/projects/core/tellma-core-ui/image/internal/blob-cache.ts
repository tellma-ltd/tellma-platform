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

/** Swept by `TmClientCache.clearAll()` through its `tm-` prefix. */
const CACHE_NAME = 'tm-images-v1';
/** The record's version stamp (the `etag` input) at put time. */
const VERSION_HEADER = 'x-tm-version';
/** The response's own `ETag`, for `If-None-Match` revalidation. */
const ETAG_HEADER = 'x-tm-etag';

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
 * A per-tab in-flight map coalesces concurrent fetches of one URL; a
 * failing `put` (quota) drops the whole cache and continues uncached —
 * the cache is an accelerator, never required.
 *
 * @internal Consumed by `TmImage`; never use directly.
 */
@Injectable({ providedIn: 'root' })
export class ɵTmImageBlobCache {
  private readonly storage = inject(ɵTM_CACHE_STORAGE);
  private readonly fetcher = inject(TM_BLOB_FETCHER);
  private readonly inflight = new Map<string, Promise<Blob>>();
  /** Set on a failed put: the pipeline continues uncached from then on. */
  private cacheBroken = false;

  /**
   * Resolves the blob for a sized URL under the record stamp `version`.
   * `onRefresh` reports a background-revalidated replacement blob.
   */
  async getImage(
    url: string,
    version: string | null,
    onRefresh?: (blob: Blob) => void,
  ): Promise<Blob> {
    const cache = await this.openCache();
    if (cache === null) {
      return this.fetchCoalesced(url, version, null);
    }
    let match: Response | undefined;
    try {
      match = await cache.match(url);
    } catch {
      match = undefined;
    }
    if (match !== undefined) {
      const stored = match.headers.get(VERSION_HEADER);
      if (version !== null && stored === version) {
        return match.blob();
      }
      if (version === null) {
        const blob = await match.blob();
        void this.revalidate(cache, url, match.headers.get(ETAG_HEADER), onRefresh);
        return blob;
      }
      try {
        await cache.delete(url, { ignoreSearch: true });
      } catch {
        // Purge is best-effort; the refetch below overwrites this key.
      }
    }
    return this.fetchCoalesced(url, version, cache);
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

  private fetchCoalesced(url: string, version: string | null, cache: Cache | null): Promise<Blob> {
    const existing = this.inflight.get(url);
    if (existing !== undefined) {
      return existing;
    }
    const request = (async (): Promise<Blob> => {
      const response = await this.fetcher(url);
      if (response.blob === null) {
        throw new Error(`tm-image: the fetcher returned no body for ${url}`);
      }
      if (cache !== null) {
        await this.put(cache, url, response.blob, version ?? response.etag, response.etag);
      }
      return response.blob;
    })().finally(() => this.inflight.delete(url));
    this.inflight.set(url, request);
    return request;
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
    headers.set(VERSION_HEADER, version ?? '');
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

  private async revalidate(
    cache: Cache,
    url: string,
    storedEtag: string | null,
    onRefresh?: (blob: Blob) => void,
  ): Promise<void> {
    try {
      const response = await this.fetcher(
        url,
        storedEtag !== null ? { ifNoneMatch: storedEtag } : undefined,
      );
      if (response.status === 304 || response.blob === null) {
        return; // still current
      }
      await this.put(cache, url, response.blob, response.etag, response.etag);
      onRefresh?.(response.blob);
    } catch {
      // Background refresh is best-effort; the stale entry stays valid.
    }
  }
}
