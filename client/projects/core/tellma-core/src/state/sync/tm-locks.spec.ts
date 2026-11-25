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

  it('runs a try at once when the name is free, and nothing while another request holds or awaits it', async () => {
    configure();
    const locks = TestBed.inject(TmLocks);
    const log: string[] = [];
    const name = `tm:spec:${crypto.randomUUID()}:zone-prompt`;
    const a = step(log, 'a');
    const first = locks.run(name, a.work);
    const queued = locks.run(name, () => Promise.resolve(log.push('queued')));
    await vi.waitFor(() => expect(log).toEqual(['a']));
    await expect(locks.tryRun(name, () => Promise.resolve(void log.push('tried')))).resolves.toBe(false);

    a.finish();
    await first;
    await queued;
    await pause();
    const b = step(log, 'b');
    const tried = locks.tryRun(name, () => b.work().then(() => undefined));
    await vi.waitFor(() => expect(log).toEqual(['a', 'queued', 'b']));
    // While the try holds the name, a second try runs nothing.
    await expect(locks.tryRun(name, () => Promise.resolve(void log.push('again')))).resolves.toBe(false);
    b.finish();
    await expect(tried).resolves.toBe(true);
    expect(log).toEqual(['a', 'queued', 'b']);
  });

  it("rejects a try with its work's failure, and frees the name", async () => {
    configure();
    const locks = TestBed.inject(TmLocks);
    const name = `tm:spec:${crypto.randomUUID()}:zone-prompt`;
    await expect(locks.tryRun(name, () => Promise.reject(new Error('The work failed.')))).rejects.toThrow('The work failed.');
    await pause();
    await expect(locks.tryRun(name, () => Promise.resolve())).resolves.toBe(true);
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

    it("runs no try while another holder has the browser's lock", async () => {
      const locks = TestBed.inject(TmLocks);
      const name = `tm:spec:${crypto.randomUUID()}:zone-prompt`;
      let release!: () => void;
      const held = new Promise<void>((resolve) => (release = resolve));
      // Another tab's holder, as the browser sees it.
      void navigator.locks.request(name, () => held);
      await pause();
      const log: string[] = [];
      await expect(locks.tryRun(name, () => Promise.resolve(void log.push('ours')))).resolves.toBe(false);
      release();
      await pause();
      await expect(locks.tryRun(name, () => Promise.resolve(void log.push('ours')))).resolves.toBe(true);
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
