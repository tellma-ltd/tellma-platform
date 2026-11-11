// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { TM_BLOB_FETCHER, type TmBlobFetcher } from './tm-blob-fetcher';

/** The default fetcher over a testing backend. */
function setup(): { fetcher: TmBlobFetcher; http: HttpTestingController } {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  return { fetcher: TestBed.inject(TM_BLOB_FETCHER), http: TestBed.inject(HttpTestingController) };
}

describe('TM_BLOB_FETCHER (default)', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('resolves the body, status and ETag of a GET', async () => {
    const { fetcher, http } = setup();
    const result = fetcher('/img/1?size=64');
    const request = http.expectOne('/img/1?size=64');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['png'], { type: 'image/png' }), { headers: { ETag: 'W/"e1"' } });

    const { status, blob, etag } = await result;
    expect(status).toBe(200);
    expect(await blob?.text()).toBe('png');
    expect(etag).toBe('W/"e1"');
  });

  it('sends If-None-Match and reports a 304 as not modified', async () => {
    const { fetcher, http } = setup();
    const result = fetcher('/img/1?size=64', { ifNoneMatch: 'W/"e1"' });
    const request = http.expectOne('/img/1?size=64');
    expect(request.request.headers.get('If-None-Match')).toBe('W/"e1"');
    request.flush(null, { status: 304, statusText: 'Not Modified', headers: { ETag: 'W/"e1"' } });

    expect(await result).toEqual({ status: 304, blob: null, etag: 'W/"e1"' });
  });

  it('rejects on any other HTTP failure', async () => {
    const { fetcher, http } = setup();
    const result = fetcher('/img/1?size=64');
    http.expectOne('/img/1?size=64').flush(null, { status: 404, statusText: 'Not Found' });
    await expect(result).rejects.toMatchObject({ status: 404 });
  });

  it('an abort cancels the request in flight and rejects with the reason', async () => {
    const { fetcher, http } = setup();
    const controller = new AbortController();
    const result = fetcher('/img/1?size=64', { signal: controller.signal });
    const request = http.expectOne('/img/1?size=64');

    const reason = new Error('left the screen');
    controller.abort(reason);
    await expect(result).rejects.toBe(reason);
    expect(request.cancelled).toBe(true);
  });

  it('a signal aborted before the call rejects at once without a request', async () => {
    const { fetcher, http } = setup();
    const reason = new Error('gone before asking');
    await expect(fetcher('/img/1?size=64', { signal: AbortSignal.abort(reason) })).rejects.toBe(
      reason,
    );
    http.expectNone('/img/1?size=64');
  });

  it('an abort after the response changes nothing', async () => {
    const { fetcher, http } = setup();
    const controller = new AbortController();
    const result = fetcher('/img/1?size=64', { signal: controller.signal });
    http.expectOne('/img/1?size=64').flush(new Blob(['png']));
    const { status } = await result;
    controller.abort();
    expect(status).toBe(200);
  });
});
