// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ɵtmDeferTriggers } from './tm-details-defer';

// The deferred blocks' triggers: the prefetch runs on the browser's idle
// callback, before the host is visible, and the idle callback and the
// viewport observer both end with the host. The browser's idle callback is
// replaced by one the test runs, and restored after each test.

/** Prefetches handed to the next component made, one per test. */
let prefetch: () => Promise<unknown> = () => Promise.resolve();

/** A host below a tall spacer, outside the viewport, with the triggers of a block inside it. */
@Component({
  selector: 'tm-test-deferred',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: block; margin-block-start: 4000px; block-size: 10px' },
  template: '',
})
class TmTestDeferred {
  /** The block's triggers. */
  readonly defer = ɵtmDeferTriggers(() => prefetch());
}

describe("the details kit's deferred-block triggers", () => {
  /** The idle callbacks requested, by id. */
  let idle: Map<number, IdleRequestCallback>;

  beforeEach(() => {
    idle = new Map();
    let next = 0;
    vi.spyOn(globalThis, 'requestIdleCallback').mockImplementation((callback) => {
      idle.set(++next, callback);
      return next;
    });
    vi.spyOn(globalThis, 'cancelIdleCallback').mockImplementation((id) => void idle.delete(id));
  });

  afterEach(() => {
    vi.restoreAllMocks();
    prefetch = () => Promise.resolve();
  });

  /**
   * Renders the host and runs its first render's hooks.
   *
   * @returns The fixture.
   */
  function render(): ReturnType<typeof TestBed.createComponent<TmTestDeferred>> {
    const fixture = TestBed.createComponent(TmTestDeferred);
    fixture.detectChanges();
    TestBed.tick();
    return fixture;
  }

  /** Runs every idle callback requested so far. */
  function runIdle(): void {
    const callbacks = [...idle.values()];
    idle.clear();
    callbacks.forEach((callback) => callback({ didTimeout: false, timeRemaining: () => 50 }));
  }

  it("imports the block's module once the browser is idle, while the host is still outside the viewport", () => {
    const load = vi.fn(() => Promise.resolve());
    prefetch = load;
    const fixture = render();

    expect(load).not.toHaveBeenCalled();
    expect(idle.size).toBe(1);
    runIdle();
    expect(load).toHaveBeenCalledTimes(1);
    expect(fixture.componentInstance.defer.visible()).toBe(false);
    fixture.destroy();
  });

  it('cancels the idle callback and stops observing once the host goes away', () => {
    const load = vi.fn(() => Promise.resolve());
    prefetch = load;
    const disconnect = vi.spyOn(IntersectionObserver.prototype, 'disconnect');
    const fixture = render();

    fixture.destroy();
    expect(globalThis.cancelIdleCallback).toHaveBeenCalledWith(1);
    expect(idle.size).toBe(0);
    expect(disconnect).toHaveBeenCalled();
    runIdle();
    expect(load).not.toHaveBeenCalled();
  });

  it('says nothing when the prefetch fails, leaving the error to the block', async () => {
    const failed = vi.fn(() => Promise.reject(new Error('The chunk failed to load.')));
    prefetch = failed;
    const fixture = render();

    runIdle();
    await Promise.resolve();
    expect(failed).toHaveBeenCalledTimes(1);
    fixture.destroy();
  });
});
