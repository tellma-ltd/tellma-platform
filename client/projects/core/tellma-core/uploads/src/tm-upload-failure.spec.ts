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
// failure a retry may mend.

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

  it('offers a retry for a server error, a lost connection, or an answer without a problem', () => {
    expect(tmUploadFailure(new TmApiError({ kind: 'server', status: 500 }), text)).toEqual({ kind: 'failed' });
    expect(tmUploadFailure(new TmApiError({ kind: 'offline', status: 0 }), text)).toEqual({ kind: 'failed' });
    expect(tmUploadFailure(new TmApiError({ kind: 'validation', status: 400 }), text)).toEqual({ kind: 'failed' });
    expect(tmUploadFailure(new Error('Unexpected'), text)).toEqual({ kind: 'failed' });
  });
});
