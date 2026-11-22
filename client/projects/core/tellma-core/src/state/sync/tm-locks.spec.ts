// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TmLocks } from './tm-locks';

// The browser's Web Locks, real in this page, and the in-tab queue a page
// without them falls back to.

/** Work the spec settles, recording when it starts. */
function step(log: string[], name: string): { readonly work: () => Promise<string>; finish(): void; fail(error: Error): void } {
  let finish!: () => void;
  let fail!: (error: Error) => void;
  const done = new Promise<string>((resolve, reject) => {
    finish = () => resolve(name);
    fail = reject;
  });
  return {
    work: () => {
      log.push(name);
      return done;
    },
    finish: () => finish(),
    fail: (error) => fail(error),
  };
}

/** Lets the browser grant any lock it would grant now; a grant crosses to the browser and back. */
async function pause(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 50));
}

/** Checks one implementation of the locks. */
function behavesAsLocks(configure: () => void): void {
  it('runs the requests of one name one at a time, in order, however each settles', async () => {
    configure();
    const locks = TestBed.inject(TmLocks);
    const log: string[] = [];
    const name = `tm:spec:${crypto.randomUUID()}:sync`;
    const a = step(log, 'a');
    const b = step(log, 'b');
    const c = step(log, 'c');
    const first = locks.run(name, a.work);
    const second = locks.run(name, b.work);
    const third = locks.run(name, c.work);
    await vi.waitFor(() => expect(log).toEqual(['a']));
    await pause();
    expect(log).toEqual(['a']);

    a.fail(new Error('The work failed.'));
    await expect(first).rejects.toThrow('The work failed.');
    await vi.waitFor(() => expect(log).toEqual(['a', 'b']));
    await pause();
    expect(log).toEqual(['a', 'b']);
    b.finish();
    await expect(second).resolves.toBe('b');
    await vi.waitFor(() => expect(log).toEqual(['a', 'b', 'c']));
    c.finish();
    await expect(third).resolves.toBe('c');
  });

  it('runs requests of different names side by side', async () => {
    configure();
    const locks = TestBed.inject(TmLocks);
    const log: string[] = [];
    const a = step(log, 'a');
    const b = step(log, 'b');
    void locks.run(`tm:spec:${crypto.randomUUID()}:sync`, a.work);
    void locks.run(`tm:spec:${crypto.randomUUID()}:sync`, b.work);
    await vi.waitFor(() => expect([...log].sort()).toEqual(['a', 'b']));
    a.finish();
    b.finish();
  });
}

describe('TmLocks', () => {
  describe('over Web Locks', () => {
    behavesAsLocks(() => {
      expect(navigator.locks).toBeDefined();
    });

    it("queues behind another holder of the browser's lock", async () => {
      const locks = TestBed.inject(TmLocks);
      const name = `tm:spec:${crypto.randomUUID()}:sync`;
      let release!: () => void;
      const held = new Promise<void>((resolve) => (release = resolve));
      // Another tab's holder, as the browser sees it.
      void navigator.locks.request(name, () => held);
      const log: string[] = [];
      const ours = locks.run(name, () => Promise.resolve(log.push('ours')));
      await pause();
      expect(log).toEqual([]);
      release();
      await ours;
      expect(log).toEqual(['ours']);
    });
  });

  describe('in a page without Web Locks', () => {
    behavesAsLocks(() => {
      // A window whose navigator has no locks, as a page served over plain HTTP.
      const view = { navigator: {} } as Window;
      TestBed.configureTestingModule({ providers: [{ provide: DOCUMENT, useValue: { defaultView: view } }] });
    });
  });
});
