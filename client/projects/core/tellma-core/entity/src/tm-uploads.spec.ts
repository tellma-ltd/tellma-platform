// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { TmBlobDescriptor, TmDecimal } from '@tellma/core';

import { TmUploads } from './tm-uploads';

// A form's uploads: the paths in flight, the blob each staged, a second
// upload replacing the first, and the host's end aborting what is left.

/** A blob an upload stages. */
const blob = (id: number): TmBlobDescriptor => ({ id, kind: 'customer-image', contentType: 'image/webp', size: '120' as TmDecimal });

/** An upload the test settles, and the signal it was given. */
function deferred(): { run: (signal: AbortSignal) => Promise<TmBlobDescriptor>; resolve(value: TmBlobDescriptor): void; reject(error: unknown): void; signal(): AbortSignal } {
  let resolve!: (value: TmBlobDescriptor) => void;
  let reject!: (error: unknown) => void;
  let given: AbortSignal | undefined;
  const promise = new Promise<TmBlobDescriptor>((yes, no) => ((resolve = yes), (reject = no)));
  return {
    run: (signal) => ((given = signal), promise),
    resolve,
    reject,
    signal: () => given!,
  };
}

/** The uploads of a host the test can end. */
function host(): { uploads: TmUploads; end(): void } {
  const injector = createEnvironmentInjector([TmUploads], TestBed.inject(EnvironmentInjector));
  return { uploads: injector.get(TmUploads), end: () => injector.destroy() };
}

describe('TmUploads', () => {
  it('lists a path while its upload runs and stages the blob it returns', async () => {
    const { uploads } = host();
    const upload = deferred();
    const done = uploads.start('imageId', upload.run);
    expect([...uploads.running()]).toEqual(['imageId']);
    expect(uploads.staged('imageId')).toBeUndefined();

    upload.resolve(blob(88));
    await expect(done).resolves.toEqual(blob(88));
    expect(uploads.running().size).toBe(0);
    expect(uploads.staged('imageId')).toEqual(blob(88));
  });

  it('clears a path whose upload fails, staging nothing', async () => {
    const { uploads } = host();
    const upload = deferred();
    const done = uploads.start('fileId', upload.run);
    upload.reject(new Error('offline'));
    await expect(done).rejects.toThrow('offline');
    expect(uploads.running().size).toBe(0);
    expect(uploads.staged('fileId')).toBeUndefined();
  });

  it('aborts a path’s upload when another starts, keeping the later one listed', async () => {
    const { uploads } = host();
    const first = deferred();
    const second = deferred();
    const earlier = uploads.start('imageId', first.run);
    const later = uploads.start('imageId', second.run);
    expect(first.signal().aborted).toBe(true);
    expect([...uploads.running()]).toEqual(['imageId']);

    // The aborted upload's late answer stages nothing.
    first.resolve(blob(1));
    await expect(earlier).rejects.toThrow();
    expect(uploads.staged('imageId')).toBeUndefined();
    expect([...uploads.running()]).toEqual(['imageId']);

    second.resolve(blob(2));
    await later;
    expect(uploads.staged('imageId')).toEqual(blob(2));
  });

  it('cancels a path, and stages or clears one directly', () => {
    const { uploads } = host();
    const upload = deferred();
    void uploads.start('imageId', upload.run).catch(() => undefined);
    uploads.cancel('imageId');
    expect(upload.signal().aborted).toBe(true);
    expect(uploads.running().size).toBe(0);

    uploads.stage('imageId', blob(5));
    expect(uploads.staged('imageId')).toEqual(blob(5));
    uploads.stage('imageId', null);
    expect(uploads.staged('imageId')).toBeUndefined();
  });

  it('aborts what is in flight when its host ends', () => {
    const { uploads, end } = host();
    const upload = deferred();
    void uploads.start('imageId', upload.run).catch(() => undefined);
    end();
    expect(upload.signal().aborted).toBe(true);
  });
});
