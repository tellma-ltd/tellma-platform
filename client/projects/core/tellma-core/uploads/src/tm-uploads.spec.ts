// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, EnvironmentInjector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { TmBlobDescriptor, TmDecimal } from '@tellma/core';

import { TmUploads } from './tm-uploads';

// A form's uploads: the paths in flight, the blob each staged, the share
// each has sent, a second upload replacing the first, a removed child row
// moving the later rows' entries up, and the host's end aborting what is
// left.

/** A blob an upload stages. */
const blob = (id: number): TmBlobDescriptor => ({ id, kind: 'customer-image', contentType: 'image/webp', size: '120' as TmDecimal });

/** An upload the test settles, and the signal it was given. */
function deferred(): {
  run: (signal: AbortSignal, progress: (sent: number) => void) => Promise<TmBlobDescriptor>;
  resolve(value: TmBlobDescriptor): void;
  reject(error: unknown): void;
  signal(): AbortSignal;
  progress(sent: number): void;
} {
  let resolve!: (value: TmBlobDescriptor) => void;
  let reject!: (error: unknown) => void;
  let given: AbortSignal | undefined;
  let report: ((sent: number) => void) | undefined;
  const promise = new Promise<TmBlobDescriptor>((yes, no) => ((resolve = yes), (reject = no)));
  return {
    run: (signal, progress) => ((given = signal), (report = progress), promise),
    resolve,
    reject,
    signal: () => given!,
    progress: (sent) => report!(sent),
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

  it('reports the share an upload has sent, clamped, until it settles', async () => {
    const { uploads } = host();
    const upload = deferred();
    const done = uploads.start('fileId', upload.run);
    expect(uploads.progress('fileId')).toBeNull();
    upload.progress(0.25);
    expect(uploads.progress('fileId')).toBe(0.25);
    upload.progress(1.5);
    expect(uploads.progress('fileId')).toBe(1);
    upload.resolve(blob(7));
    await done;
    expect(uploads.progress('fileId')).toBeNull();
  });

  it('moves the later rows’ uploads and blobs up when a child row is removed, aborting the removed row’s', async () => {
    const { uploads } = host();
    const removed = deferred();
    const moved = deferred();
    const gone = uploads.start('attachments.1.fileId', removed.run);
    const kept = uploads.start('attachments.2.fileId', moved.run);
    uploads.stage('attachments.0.fileId', blob(10));
    uploads.stage('attachments.1.fileId', blob(11));
    uploads.stage('attachments.3.fileId', blob(13));
    uploads.stage('imageId', blob(1));

    uploads.removeRow('attachments', 1);
    expect(removed.signal().aborted).toBe(true);
    expect([...uploads.running()]).toEqual(['attachments.1.fileId']);
    expect(uploads.staged('attachments.0.fileId')).toEqual(blob(10));
    expect(uploads.staged('attachments.2.fileId')).toEqual(blob(13));
    expect(uploads.staged('attachments.3.fileId')).toBeUndefined();
    expect(uploads.staged('imageId')).toEqual(blob(1));

    // The moved upload reports and stages under its row's new path.
    moved.progress(0.5);
    expect(uploads.progress('attachments.1.fileId')).toBe(0.5);
    moved.resolve(blob(12));
    await kept;
    expect(uploads.staged('attachments.1.fileId')).toEqual(blob(12));
    expect(uploads.running().size).toBe(0);
    removed.resolve(blob(99));
    await expect(gone).rejects.toThrow();
    expect(uploads.staged('attachments.0.fileId')).toEqual(blob(10));
  });

  it('discards every upload and staged blob at once, and a result arriving after stages nothing', async () => {
    const { uploads } = host();
    const image = deferred();
    const file = deferred();
    uploads.stage('logoId', blob(5));
    const imageRun = uploads.start('imageId', image.run);
    const fileRun = uploads.start('attachments.0.fileId', file.run);

    uploads.cancelAll();
    expect(image.signal().aborted).toBe(true);
    expect(file.signal().aborted).toBe(true);
    expect(uploads.running().size).toBe(0);
    expect(uploads.staged('logoId')).toBeUndefined();

    // An upload that ignores its signal and resolves anyway is still refused, and stages nothing.
    image.resolve(blob(1));
    file.resolve(blob(2));
    await expect(imageRun).rejects.toMatchObject({ name: 'AbortError' });
    await expect(fileRun).rejects.toMatchObject({ name: 'AbortError' });
    expect(uploads.staged('imageId')).toBeUndefined();
    expect(uploads.staged('attachments.0.fileId')).toBeUndefined();
    expect(uploads.running().size).toBe(0);
  });

  it('lists a path whose upload failed, counting it, until it uploads again, is cancelled, cleared or dismissed; an abort is no failure', async () => {
    const { uploads } = host();
    const fail = async (path: string): Promise<void> => {
      const upload = deferred();
      const run = uploads.start(path, upload.run);
      upload.reject(new Error('The connection dropped.'));
      await expect(run).rejects.toThrow('The connection dropped.');
    };

    await fail('imageId');
    await fail('logoId');
    await fail('coverId');
    await fail('iconId');
    expect([...uploads.failed()].sort()).toEqual(['coverId', 'iconId', 'imageId', 'logoId']);
    expect(uploads.failures()).toBe(4);

    // An upload again, a cancel, a cleared path and a dismissal each clear one; the count only grows.
    const again = deferred();
    const retried = uploads.start('imageId', again.run);
    expect(uploads.failed().has('imageId')).toBe(false);
    uploads.cancel('logoId');
    uploads.stage('coverId', null);
    uploads.dismiss('iconId');
    expect(uploads.failed().size).toBe(0);
    expect(uploads.failures()).toBe(4);

    // The retry aborted by a later pick fails nothing, though its upload rejects as it honours the abort.
    void uploads.start('imageId', deferred().run).catch(() => undefined);
    expect(again.signal().aborted).toBe(true);
    again.reject(new DOMException('Aborted', 'AbortError'));
    await expect(retried).rejects.toThrow('Aborted');
    expect(uploads.failed().size).toBe(0);
    expect(uploads.failures()).toBe(4);
  });

  it('moves a failed row up with its row, drops the removed row’s failure, and clears every failure on cancelAll', async () => {
    const { uploads } = host();
    for (const index of [0, 1, 2]) {
      const upload = deferred();
      const run = uploads.start(`attachments.${index}.fileId`, upload.run);
      upload.reject(new Error('Failed'));
      await expect(run).rejects.toThrow();
    }

    uploads.removeRow('attachments', 1);
    expect([...uploads.failed()].sort()).toEqual(['attachments.0.fileId', 'attachments.1.fileId']);
    uploads.cancelAll();
    expect(uploads.failed().size).toBe(0);
  });

  it('aborts what is in flight when its host ends', () => {
    const { uploads, end } = host();
    const upload = deferred();
    void uploads.start('imageId', upload.run).catch(() => undefined);
    end();
    expect(upload.signal().aborted).toBe(true);
  });
});
