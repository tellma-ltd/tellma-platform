// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// A canary for the unit runner's configuration, not for the library: the
// project's files share one browser page per worker, so a spy or a stub one
// test leaves behind would reach whatever runs after it in that page, on the
// schedules that put it there. The workspace's Vitest preset restores every
// spy and global stub before each test. The first test below leaves both
// behind on purpose; the second, which Vitest runs after it, fails at once
// if the restore ever stops reaching this project.

/** The global the first test stubs and leaves stubbed. */
const PROBE = 'tmUnitRunnerProbe';

describe('the unit runner', () => {
  it('lets a test leave a spy on a shared prototype and a stubbed global behind', () => {
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    vi.stubGlobal(PROBE, 'left behind');
    expect(vi.isMockFunction(HTMLAnchorElement.prototype.click)).toBe(true);
    expect(Reflect.get(globalThis, PROBE)).toBe('left behind');
  });

  it("starts the next test with the prototype's own method and without the stubbed global", () => {
    expect(vi.isMockFunction(HTMLAnchorElement.prototype.click)).toBe(false);
    // The real method dispatches the click; a leftover mock would dispatch nothing.
    const link = document.body.appendChild(document.createElement('a'));
    try {
      let clicks = 0;
      link.addEventListener('click', () => clicks++);
      link.click();
      expect(clicks).toBe(1);
    } finally {
      link.remove();
    }
    expect(PROBE in globalThis).toBe(false);
  });
});
