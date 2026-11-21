// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { ɵTmLocks } from '@tellma/core';

import { provideTellmaStateTesting } from './provide-tellma-state-testing';
import { TmFakeLocks } from './tm-fake-locks';

// The in-memory locks, as the state testing providers install them.

describe('TmFakeLocks', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideTellmaStateTesting()] }));

  it("stands in for the browser's locks", () => {
    expect(TestBed.inject(ɵTmLocks)).toBe(TestBed.inject(TmFakeLocks));
  });

  it('queues the requests of one name, however each settles, and shows which are held', async () => {
    const locks = TestBed.inject(TmFakeLocks);
    let fail!: (error: Error) => void;
    const first = locks.run('tm:acme:test-user:17:sync', () => new Promise<never>((_, reject) => (fail = reject)));
    const log: string[] = [];
    const second = locks.run('tm:acme:test-user:17:sync', () => Promise.resolve(log.push('second')));
    const other = locks.run('tm:acme:test-user:18:sync', () => Promise.resolve(log.push('other')));
    await other;
    expect(log).toEqual(['other']);
    expect(locks.held()).toEqual(['tm:acme:test-user:17:sync']);

    fail(new Error('The work failed.'));
    await expect(first).rejects.toThrow('The work failed.');
    await second;
    expect(log).toEqual(['other', 'second']);
    expect(locks.held()).toEqual([]);
  });
});
