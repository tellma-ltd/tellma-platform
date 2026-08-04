// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TmClientCache } from './tm-client-cache';

// Browser-mode vitest serves from localhost — a secure context, so the
// real CacheStorage is available and the sweep is tested end-to-end.
describe('TmClientCache', () => {
  afterEach(async () => {
    for (const key of await caches.keys()) {
      if (key.startsWith('tm-test-') || key === 'not-tm-test') {
        await caches.delete(key);
      }
    }
  });

  it('clearAll deletes every tm--prefixed cache and nothing else', async () => {
    await caches.open('tm-test-a');
    await caches.open('tm-test-b');
    await caches.open('not-tm-test');

    await TestBed.inject(TmClientCache).clearAll();

    const keys = await caches.keys();
    expect(keys).not.toContain('tm-test-a');
    expect(keys).not.toContain('tm-test-b');
    expect(keys).toContain('not-tm-test');
  });

  it('clearAll resolves (never throws) when there is nothing to sweep', async () => {
    await expect(TestBed.inject(TmClientCache).clearAll()).resolves.toBeUndefined();
  });
});
