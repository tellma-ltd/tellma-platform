// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmApiError, TmServerText } from '@tellma/core';

import { tmEntityRig } from '../../src/entity/tm-entity.testing.util';
import { tmUploadFailure } from './tm-upload-failure';

// What a failed upload says: an abort nothing, a refusal the server's
// reason (its first item, else the problem's title), anything else a
// failure a retry may mend, decided by the error's kind rather than its
// status.

describe('a failed upload', () => {
  /** The server's text, as the field reads it. */
  let text: TmServerText;

  beforeEach(async () => {
    await tmEntityRig();
    text = TestBed.inject(TmServerText);
  });

  it('says nothing when it was aborted, by its signal or by the API', () => {
    expect(tmUploadFailure(new DOMException('Stopped', 'AbortError'), text)).toEqual({ kind: 'aborted' });
    expect(tmUploadFailure(new TmApiError({ kind: 'cancelled', status: 0 }), text)).toEqual({ kind: 'aborted' });
  });

  it("gives a refused file's reason: the server's message for its first item", () => {
    const failure = tmUploadFailure(
      new TmApiError({
        kind: 'validation',
        status: 415,
        code: 'blob-rejected',
        problem: {
          code: 'blob-rejected',
          status: 415,
          instance: 'trace-1',
          title: 'The file was refused',
          errors: [
            { path: '', code: 'Acme.Unsigned', message: 'The PDF must be signed.' },
            { path: '', code: 'Acme.Other', message: 'Another reason.' },
          ],
        },
      }),
      text,
    );
    expect(failure.kind).toBe('rejected');
    expect(failure.kind === 'rejected' && failure.message()).toBe('The PDF must be signed.');
  });

  it("gives the problem's title for a refusal without items", () => {
    const failure = tmUploadFailure(
      new TmApiError({ kind: 'validation', status: 413, code: 'payload-too-large', problem: { code: 'payload-too-large', status: 413, instance: 'trace-2', title: 'The file is too large' } }),
      text,
    );
    expect(failure.kind === 'rejected' && failure.message()).toBe('The file is too large');
  });

  it('offers a retry for a busy server or a step-up, though each answers in the 4xx range with a problem', () => {
    const busy = new TmApiError({
      kind: 'unavailable',
      status: 429,
      code: 'rate-limited',
      retryAfterMs: 2000,
      problem: { code: 'rate-limited', status: 429, instance: 'trace-3', title: 'Too many requests' },
    });
    const stepUp = new TmApiError({
      kind: 'step-up',
      status: 401,
      code: 'step-up-required',
      problem: { code: 'step-up-required', status: 401, instance: 'trace-4', title: 'Sign in again to continue' },
    });
    expect(tmUploadFailure(busy, text)).toEqual({ kind: 'failed' });
    expect(tmUploadFailure(stepUp, text)).toEqual({ kind: 'failed' });
  });

  it('refuses a request the server turned down as sent: too large, malformed or forbidden', () => {
    for (const kind of ['limit', 'bad-request', 'forbidden'] as const) {
      const failure = tmUploadFailure(new TmApiError({ kind, status: 400, problem: { code: kind, status: 400, instance: 'trace-5', title: `Refused: ${kind}` } }), text);
      expect(failure.kind === 'rejected' && failure.message()).toBe(`Refused: ${kind}`);
    }
  });

  it('offers a retry for a server error, a lost connection, or an answer without a problem', () => {
    expect(tmUploadFailure(new TmApiError({ kind: 'server', status: 500 }), text)).toEqual({ kind: 'failed' });
    expect(tmUploadFailure(new TmApiError({ kind: 'offline', status: 0 }), text)).toEqual({ kind: 'failed' });
    expect(tmUploadFailure(new TmApiError({ kind: 'validation', status: 400 }), text)).toEqual({ kind: 'failed' });
    expect(tmUploadFailure(new Error('Unexpected'), text)).toEqual({ kind: 'failed' });
  });
});
