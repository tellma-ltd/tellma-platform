// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { TM_BLOB_FETCHER, type TmBlobFetchOptions } from '../tm-blob-fetcher';
import { ɵTM_CACHE_STORAGE, ɵTmImageBlobCache } from './blob-cache';

/** The real Cache API keys by ABSOLUTE request URL — the fake must too. */
function absolute(url: RequestInfo | URL): string {
  const raw = typeof url === 'string' ? url : url instanceof URL ? url.toString() : url.url;
  return new URL(raw, document.baseURI).toString();
}

/** An in-memory Cache — enough of the contract for the cache logic. */
class FakeCache {
  readonly entries = new Map<string, Response>();
  failPuts = false;

  async match(url: string): Promise<Response | undefined> {
    // Response bodies are one-shot: hand out clones, keep the original.
    return this.entries.get(absolute(url))?.clone();
  }

  async put(url: string, response: Response): Promise<void> {
    if (this.failPuts) {
      throw new DOMException('quota', 'QuotaExceededError');
    }
    this.entries.set(absolute(url), response);
  }

  async delete(url: RequestInfo | URL, options?: CacheQueryOptions): Promise<boolean> {
    const key = absolute(url);
    if (options?.ignoreSearch === true) {
      const base = key.split('?')[0];
      let any = false;
      for (const existing of [...this.entries.keys()]) {
        if (existing.split('?')[0] === base) {
          this.entries.delete(existing);
          any = true;
        }
      }
      return any;
    }
    return this.entries.delete(key);
  }

  async keys(): Promise<Request[]> {
    return [...this.entries.keys()].map((url) => new Request(url));
  }

  has(url: string): boolean {
    return this.entries.has(absolute(url));
  }
}

/** An in-memory CacheStorage over {@link FakeCache}. */
class FakeCacheStorage {
  readonly caches = new Map<string, FakeCache>();
  readonly deleted: string[] = [];

  async open(name: string): Promise<Cache> {
    let cache = this.caches.get(name);
    if (cache === undefined) {
      cache = new FakeCache();
      this.caches.set(name, cache);
    }
    return cache as unknown as Cache;
  }

  async delete(name: string): Promise<boolean> {
    this.deleted.push(name);
    return this.caches.delete(name);
  }
}

interface FetchCall {
  readonly url: string;
  readonly options: TmBlobFetchOptions | undefined;
}

function makeBlob(text: string): Blob {
  return new Blob([text], { type: 'image/png' });
}

/** A scriptable fetcher recording every call. */
function makeFetcher(): {
  calls: FetchCall[];
  respond: (url: string, options?: TmBlobFetchOptions) => Promise<{ status: number; blob: Blob | null; etag: string | null }>;
  script: (fn: (url: string, options?: TmBlobFetchOptions) => Promise<{ status: number; blob: Blob | null; etag: string | null }>) => void;
} {
  const calls: FetchCall[] = [];
  let impl: (
    url: string,
    options?: TmBlobFetchOptions,
  ) => Promise<{ status: number; blob: Blob | null; etag: string | null }> = async (url) => ({
    status: 200,
    blob: makeBlob(`bytes:${url}`),
    etag: 'W/"http-etag"',
  });
  return {
    calls,
    respond: (url, options) => {
      calls.push({ url, options });
      return impl(url, options);
    },
    script: (fn) => {
      impl = fn;
    },
  };
}

function setup(storage: FakeCacheStorage | null): {
  cache: ɵTmImageBlobCache;
  fetcher: ReturnType<typeof makeFetcher>;
} {
  const fetcher = makeFetcher();
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: ɵTM_CACHE_STORAGE, useValue: storage as unknown as CacheStorage | null },
      { provide: TM_BLOB_FETCHER, useValue: fetcher.respond },
    ],
  });
  return { cache: TestBed.inject(ɵTmImageBlobCache), fetcher };
}

async function text(blob: Blob): Promise<string> {
  return blob.text();
}

describe('ɵTmImageBlobCache', () => {
  it('miss → fetch + put; matching stamp → cache hit with NO network', async () => {
    const storage = new FakeCacheStorage();
    const { cache, fetcher } = setup(storage);

    const first = await cache.getImage('/img/1?size=128', 'v1');
    expect(await text(first)).toBe('bytes:/img/1?size=128');
    expect(fetcher.calls).toHaveLength(1);

    const second = await cache.getImage('/img/1?size=128', 'v1');
    expect(await text(second)).toBe('bytes:/img/1?size=128');
    expect(fetcher.calls).toHaveLength(1); // served from cache
  });

  it('a stamp mismatch purges EVERY size variant of the src and refetches', async () => {
    const storage = new FakeCacheStorage();
    const { cache, fetcher } = setup(storage);
    await cache.getImage('/img/1?size=128', 'v1');
    await cache.getImage('/img/1?size=512', 'v1');
    await cache.getImage('/img/2?size=128', 'v1'); // an unrelated src
    expect(fetcher.calls).toHaveLength(3);

    fetcher.script(async (url) => ({ status: 200, blob: makeBlob(`fresh:${url}`), etag: null }));
    const refreshed = await cache.getImage('/img/1?size=128', 'v2');
    expect(await text(refreshed)).toBe('fresh:/img/1?size=128');
    expect(fetcher.calls).toHaveLength(4);

    const fake = storage.caches.get('tm-images-v1')!;
    expect(fake.has('/img/1?size=512')).toBe(false); // variant purged
    expect(fake.has('/img/2?size=128')).toBe(true); // stranger kept

    // The refreshed entry now carries v2: hitting again stays offline.
    await cache.getImage('/img/1?size=128', 'v2');
    expect(fetcher.calls).toHaveLength(4);
  });

  it('a mismatch on a QUERY-KEYED src purges only that record, not path-mates', async () => {
    // Query-keyed endpoints put the record id in the query — the Cache
    // API's ignoreSearch would blast every record under the path.
    const storage = new FakeCacheStorage();
    const { cache, fetcher } = setup(storage);
    await cache.getImage('/api/images?id=7&size=64', 'v1');
    await cache.getImage('/api/images?id=7&size=512', 'v1');
    await cache.getImage('/api/images?id=9&size=64', 'v1');
    expect(fetcher.calls).toHaveLength(3);

    await cache.getImage('/api/images?id=7&size=64', 'v2'); // record 7 moved
    const fake = storage.caches.get('tm-images-v1')!;
    expect(fake.has('/api/images?id=7&size=512')).toBe(false); // 7's variant purged
    expect(fake.has('/api/images?id=9&size=64')).toBe(true); // record 9 UNTOUCHED
    await cache.getImage('/api/images?id=9&size=64', 'v1');
    expect(fetcher.calls).toHaveLength(4); // only 7's refetch — 9 stayed cached
  });

  it('concurrent null-stamp reads coalesce to ONE background revalidation', async () => {
    const storage = new FakeCacheStorage();
    const { cache, fetcher } = setup(storage);
    await cache.getImage('/img/1?size=128', 'v1');
    expect(fetcher.calls).toHaveLength(1);

    let releaseRevalidation!: () => void;
    fetcher.script(
      () =>
        new Promise((resolve) => {
          releaseRevalidation = () => resolve({ status: 304, blob: null, etag: null });
        }),
    );
    // All three reads start before any revalidation finishes — the dedup
    // must hold across the overlap, not merely in sequence.
    await Promise.all([
      cache.getImage('/img/1?size=128', null),
      cache.getImage('/img/1?size=128', null),
      cache.getImage('/img/1?size=128', null),
    ]);
    expect(fetcher.calls).toHaveLength(2); // one conditional GET, not three
    releaseRevalidation();
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(fetcher.calls).toHaveLength(2);
  });

  it('a null stamp serves the cached entry immediately and revalidates in the background', async () => {
    const storage = new FakeCacheStorage();
    const { cache, fetcher } = setup(storage);
    await cache.getImage('/img/1?size=128', 'v1');

    // 304 keeps the entry.
    fetcher.script(async () => ({ status: 304, blob: null, etag: null }));
    const served = await cache.getImage('/img/1?size=128', null);
    expect(await text(served)).toBe('bytes:/img/1?size=128');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(fetcher.calls).toHaveLength(2);
    expect(fetcher.calls[1].options?.ifNoneMatch).toBe('W/"http-etag"');

    // 200 replaces the entry and reports the swap.
    fetcher.script(async (url) => ({ status: 200, blob: makeBlob(`new:${url}`), etag: 'W/"e2"' }));
    const refreshed: Blob[] = [];
    const stale = await cache.getImage('/img/1?size=128', null, (blob) => refreshed.push(blob));
    expect(await text(stale)).toBe('bytes:/img/1?size=128'); // still the old bytes
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(refreshed).toHaveLength(1);
    expect(await text(refreshed[0])).toBe('new:/img/1?size=128');

    const next = await cache.getImage('/img/1?size=128', null);
    expect(await text(next)).toBe('new:/img/1?size=128');
  });

  it('coalesces concurrent fetches of one URL into a single request', async () => {
    const storage = new FakeCacheStorage();
    const { cache, fetcher } = setup(storage);
    let release!: () => void;
    fetcher.script(
      (url) =>
        new Promise((resolve) => {
          release = () => resolve({ status: 200, blob: makeBlob(`bytes:${url}`), etag: null });
        }),
    );
    const first = cache.getImage('/img/1?size=128', 'v1');
    const second = cache.getImage('/img/1?size=128', 'v1');
    // getImage reaches the fetcher only after the async cache lookup.
    await new Promise((resolve) => setTimeout(resolve, 0));
    release();
    expect(await text(await first)).toBe('bytes:/img/1?size=128');
    expect(await text(await second)).toBe('bytes:/img/1?size=128');
    expect(fetcher.calls).toHaveLength(1);
  });

  it('a failing put drops the whole cache and continues uncached', async () => {
    const storage = new FakeCacheStorage();
    const { cache, fetcher } = setup(storage);
    await cache.getImage('/img/1?size=128', 'v1');
    (storage.caches.get('tm-images-v1') as FakeCache).failPuts = true;

    const blob = await cache.getImage('/img/2?size=128', 'v1'); // put fails
    expect(await text(blob)).toBe('bytes:/img/2?size=128'); // still served
    expect(storage.deleted).toContain('tm-images-v1'); // cache dropped

    // From here on the pipeline is cache-less: every call fetches.
    await cache.getImage('/img/1?size=128', 'v1');
    await cache.getImage('/img/1?size=128', 'v1');
    expect(fetcher.calls.map((call) => call.url)).toEqual([
      '/img/1?size=128',
      '/img/2?size=128',
      '/img/1?size=128',
      '/img/1?size=128',
    ]);
  });

  it('runs cache-less from the start where CacheStorage is unavailable, still coalescing', async () => {
    const { cache, fetcher } = setup(null);
    let release!: () => void;
    fetcher.script(
      (url) =>
        new Promise((resolve) => {
          release = () => resolve({ status: 200, blob: makeBlob(`bytes:${url}`), etag: null });
        }),
    );
    const first = cache.getImage('/img/1?size=128', 'v1');
    const second = cache.getImage('/img/1?size=128', 'v1');
    await new Promise((resolve) => setTimeout(resolve, 0));
    release();
    await first;
    await second;
    expect(fetcher.calls).toHaveLength(1); // coalesced

    fetcher.script(async (url) => ({ status: 200, blob: makeBlob(`bytes:${url}`), etag: null }));
    await cache.getImage('/img/1?size=128', 'v1');
    expect(fetcher.calls).toHaveLength(2); // nothing was cached
  });
});
