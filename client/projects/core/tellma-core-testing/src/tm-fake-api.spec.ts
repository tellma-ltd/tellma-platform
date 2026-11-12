// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmApi, TmApiError } from '@tellma/core';

import { provideTellmaTesting } from './provide-tellma-testing';
import { TmFakeApi } from './tm-fake-api';

describe('TmFakeApi', () => {
  let api: TmFakeApi;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
    api = TestBed.inject(TmFakeApi);
  });

  it('stands behind TmApi in provideTellmaTesting', () => {
    expect(TestBed.inject(TmApi)).toBe(api);
  });

  it('answers each route from its script and records every call', async () => {
    api.respond('centers/query', { result: [] });
    api.respond('centers/save', (call: { body: unknown }) => ({ saved: call.body }));
    const signal = new AbortController().signal;

    await expect(api.read('centers/query', { top: 5 }, { signal })).resolves.toEqual({ result: [] });
    await expect(api.write('centers/save', [1])).resolves.toEqual({ saved: [1] });
    api.send('users/me/preferences', { theme: 'dark' }, { background: true });

    expect(api.calls()).toEqual([
      { kind: 'read', route: 'centers/query', body: { top: 5 }, options: { signal } },
      { kind: 'write', route: 'centers/save', body: [1], options: {} },
      { kind: 'send', route: 'users/me/preferences', body: { theme: 'dark' }, options: { background: true } },
    ]);
  });

  it('rejects what the script throws, and a route with no script', async () => {
    const refusal = new TmApiError({ kind: 'validation', status: 422, code: 'validation' });
    api.respond('centers/save', () => {
      throw refusal;
    });
    await expect(api.write('centers/save', {})).rejects.toBe(refusal);
    await expect(api.write('centers/delete', {})).rejects.toThrow(/no answer for 'centers\/delete'/);
  });

  it('rejects an aborted call as cancelled', async () => {
    api.respond('centers/query', () => new Promise(() => undefined));
    const controller = new AbortController();
    const read = api.read('centers/query', {}, { signal: controller.signal });
    controller.abort();
    await expect(read).rejects.toMatchObject({ kind: 'cancelled' });
    await expect(api.read('centers/query', {}, { signal: controller.signal })).rejects.toBeInstanceOf(TmApiError);
  });

  it('scripts uploads under blobs/<kind>', async () => {
    const descriptor = { id: 51, kind: 'customer-image', contentType: 'image/png', size: '8' };
    api.respond('blobs/customer-image', descriptor);
    const file = new Blob(['12345678'], { type: 'image/png' });
    const upload = api.upload('customer-image', file, { fileName: 'logo.png', signal: new AbortController().signal });
    await expect(upload).resolves.toEqual(descriptor);
    expect(api.calls()[0]).toMatchObject({ kind: 'upload', route: 'blobs/customer-image', body: file });
  });

  it('counts the pending writes', async () => {
    let finish!: (value: unknown) => void;
    api.respond('centers/save', () => new Promise((resolve) => (finish = resolve)));
    const write = api.write('centers/save', {});
    expect(api.writesInFlight()).toBe(1);
    await Promise.resolve();
    finish({});
    await write;
    expect(api.writesInFlight()).toBe(0);
  });

  it('reports what the test sets for reachability and fingerprints', async () => {
    expect(api.online()).toBe(true);
    api.setOnline(false);
    expect(api.online()).toBe(false);
    api.setFingerprint('centers/save', 'fp-save');
    await expect(api.fingerprint('centers/save')).resolves.toBe('fp-save');
    api.setFingerprint('centers/save', null);
    await expect(api.fingerprint('centers/save')).resolves.toBeNull();
  });
});
