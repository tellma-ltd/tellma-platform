// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { ɵTM_UPDATES_LOAD, type ɵTmUpdatesModule } from '@tellma/core';

import { TmFakeApiUpdates } from '../../src/api/tm-api.testing.util';
import { TM_RELOAD_HOST, TmErrorUpdates } from './tm-error-updates';

// The error views' Reload on its own: the page behind it and the update
// service's loader are faked, and the loads are counted.

/**
 * Configures the service over a faked page and loader.
 *
 * @param load - The loader; by default, one that loads the faked update service.
 */
function setUp(load?: () => Promise<ɵTmUpdatesModule | null>): { updates: TmFakeApiUpdates; loads: () => number; reload: TmErrorUpdates } {
  const updates = new TmFakeApiUpdates();
  const base = load ?? (() => Promise.resolve({ TmUpdates: TmFakeApiUpdates }));
  let loads = 0;
  TestBed.configureTestingModule({
    providers: [
      { provide: TmFakeApiUpdates, useValue: updates },
      { provide: ɵTM_UPDATES_LOAD, useValue: () => (loads++, base()) },
      { provide: TM_RELOAD_HOST, useValue: updates },
    ],
  });
  return { updates, loads: () => loads, reload: TestBed.inject(TmErrorUpdates) };
}

describe('TmErrorUpdates', () => {
  it('reloads at once, loading nothing, when no worker is in the way', async () => {
    const { updates, loads, reload } = setUp();
    updates.worker = false;
    expect(await reload.update()).toBe('disabled');
    expect([updates.reloads, updates.checks, loads()]).toEqual([1, 0, 0]);
  });

  it('downloads the new version through the update service when a worker controls the page', async () => {
    const { updates, reload } = setUp();
    updates.answer = 'failed';
    expect(await reload.update()).toBe('failed');
    expect([updates.checks, updates.reloads]).toEqual([1, 0]);
    updates.answer = 'ready';
    expect(await reload.update()).toBe('ready');
    expect([updates.checks, updates.reloads]).toEqual([2, 1]);
  });

  it('reloads when the app has no update service, and keeps the page when the service does not load', async () => {
    const absent = setUp(() => Promise.resolve(null));
    expect(await absent.reload.update()).toBe('disabled');
    expect(absent.updates.reloads).toBe(1);

    TestBed.resetTestingModule();
    const failing = setUp(() => Promise.reject(new TypeError('Failed to fetch dynamically imported module')));
    expect(await failing.reload.update()).toBe('unreachable');
    expect(failing.updates.reloads).toBe(0);
  });

  it('reloads as the browser would on Reload anyway', () => {
    const { updates, reload } = setUp();
    reload.reload();
    expect(updates.reloads).toBe(1);
  });
});
