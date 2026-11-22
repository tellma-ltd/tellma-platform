// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmClientStore, type TmId, type TmPartition, ɵTM_API_HOST } from '@tellma/core';
import { provideTellmaStateTesting, provideTellmaTesting, TmFakeSession } from '@tellma/core-testing';

import { TmFakeApiHost } from '../../src/api/tm-api.testing.util';
import { TmTreeExpansion } from './tm-tree-expansion';

// Tree expansion over an in-memory store and faked timers: the session
// partition's `tree:<page>` documents.

const PAGE = '/17/centers';
const SESSION: TmPartition = { kind: 'session', sub: 'test-user', sessionHandle: 'test-session' };

describe('TmTreeExpansion', () => {
  let trees: TmTreeExpansion;
  let store: TmClientStore;
  let host: TmFakeApiHost;

  beforeEach(() => {
    host = new TmFakeApiHost();
    TestBed.configureTestingModule({
      providers: [provideTellmaTesting(), provideTellmaStateTesting(), { provide: ɵTM_API_HOST, useValue: host }],
    });
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    trees = TestBed.inject(TmTreeExpansion);
    store = TestBed.inject(TmClientStore);
  });

  afterEach(() => vi.useRealTimers());

  /** The page's stored ids. */
  const stored = (): readonly TmId[] | undefined => store.doc<{ ids: TmId[] }>(SESSION, `tree:${PAGE}`)()?.data.ids;

  it('shows a toggle at once, and writes it to the session partition 250 ms after the last one', async () => {
    const expanded = trees.expanded(PAGE);
    trees.toggle(PAGE, 5, true);
    await vi.advanceTimersByTimeAsync(200);
    trees.toggle(PAGE, 7, true);
    expect([...expanded()]).toEqual([5, 7]);
    await vi.advanceTimersByTimeAsync(249);
    expect(stored()).toBeUndefined();
    await vi.advanceTimersByTimeAsync(1);
    expect(stored()).toEqual([5, 7]);
    trees.toggle(PAGE, 5, false);
    expect([...expanded()]).toEqual([7]);
    await vi.advanceTimersByTimeAsync(250);
    expect(stored()).toEqual([7]);
    expect([...expanded()]).toEqual([7]);
  });

  it('keeps the expansion order, oldest first, and a page apart from another', async () => {
    trees.toggle(PAGE, 9, true);
    trees.toggle(PAGE, 4, true);
    trees.toggle(PAGE, 9, true);
    trees.toggle('/17/agents', 1, true);
    await vi.advanceTimersByTimeAsync(250);
    expect(stored()).toEqual([9, 4]);
    expect([...trees.expanded('/17/agents')()]).toEqual([1]);
  });

  it('replaces the expansion in one write', async () => {
    trees.toggle(PAGE, 1, true);
    trees.toggle(PAGE, 2, true);
    await vi.advanceTimersByTimeAsync(250);
    trees.replace(PAGE, [3, 1, 3]);
    expect([...trees.expanded(PAGE)()]).toEqual([3, 1]);
    await vi.advanceTimersByTimeAsync(250);
    expect(stored()).toEqual([3, 1]);
  });

  it('keeps the 500 newest ids: a 501st drops the oldest', async () => {
    trees.replace(PAGE, Array.from({ length: 500 }, (_, i) => i + 1));
    trees.toggle(PAGE, 501, true);
    await vi.advanceTimersByTimeAsync(250);
    expect(stored()).toHaveLength(500);
    expect(stored()?.[0]).toBe(2);
    expect(stored()?.at(-1)).toBe(501);
    trees.replace(PAGE, Array.from({ length: 600 }, (_, i) => i + 1));
    expect(trees.expanded(PAGE)().size).toBe(500);
    expect([...trees.expanded(PAGE)()][0]).toBe(101);
  });

  it('keeps a change made while two slow writes are in flight, each write settling only its own', async () => {
    // Every write waits for the gate, then commits in order, as one
    // transaction queued behind a slow one does.
    const patch = store.patch.bind(store);
    let open!: () => void;
    let queue = new Promise<void>((resolve) => (open = resolve));
    const writes = vi.spyOn(store, 'patch').mockImplementation(((...args: Parameters<typeof patch>) => {
      queue = queue.then(() => patch(...args));
      return queue;
    }) as typeof patch);
    trees.toggle(PAGE, 1, true);
    await vi.advanceTimersByTimeAsync(250);
    trees.toggle(PAGE, 2, true);
    await vi.advanceTimersByTimeAsync(250);
    // The second write sends only the change the first does not carry.
    expect(writes).toHaveBeenCalledTimes(2);
    expect(writes.mock.calls[1]![2](undefined)).toEqual({ ids: [2] });
    trees.toggle(PAGE, 3, true);
    await vi.advanceTimersByTimeAsync(100);
    open();
    await vi.advanceTimersByTimeAsync(0);
    expect(stored()).toEqual([1, 2]);
    expect([...trees.expanded(PAGE)()]).toEqual([1, 2, 3]);
    await vi.advanceTimersByTimeAsync(150);
    expect(writes).toHaveBeenCalledTimes(3);
    expect(stored()).toEqual([1, 2, 3]);
    expect([...trees.expanded(PAGE)()]).toEqual([1, 2, 3]);
  });

  it('writes what is pending at once when the page goes away', async () => {
    trees.toggle(PAGE, 5, true);
    host.fire('window', 'pagehide');
    await vi.advanceTimersByTimeAsync(0);
    expect(stored()).toEqual([5]);
  });

  it('shows nothing, and keeps nothing, with no signed-in user', async () => {
    TestBed.inject(TmFakeSession).setProfile(null);
    trees.toggle(PAGE, 5, true);
    await vi.advanceTimersByTimeAsync(250);
    expect(trees.expanded(PAGE)().size).toBe(0);
  });
});
