// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmAppRig, tmSettle } from '../routes/tm-app.testing.util';
import { tmShellComponent, tmShellRoute } from '../routes/tm-route-hosts';

// The preload of a tenant route warms the tenant shell (spec 0024 §4.4,
// §9.4), so the first entry into a tenant from the picker activates the
// shell within the click's task and paints the route skeleton in the click's
// frame. A shell component stays loaded for the rest of a file once any load
// brings it in, and the TmUrls spec enters a tenant before each of its tests,
// so this spec lives in a file of its own that enters no tenant.

describe('the preload of a tenant route', () => {
  afterEach(() => vi.restoreAllMocks());

  it('loads the tenant shell at idle, so the route returns the shell itself rather than its load', async () => {
    const rig = await tmAppRig();
    // The shell's module is in memory, as it is on the picker, whose page
    // came through it; only the tenant shell's own load is still to run.
    await tmShellComponent('TmPagePlaceholder')();
    const idle: (() => void)[] = [];
    vi.spyOn(window, 'requestIdleCallback').mockImplementation((callback) => {
      idle.push(() => callback({ didTimeout: false, timeRemaining: () => 50 }));
      return idle.length;
    });

    rig.urls.preload('/17');
    idle.splice(0).forEach((run) => run());
    await tmSettle();

    const shell = tmShellRoute('TmTenantShell')();
    expect(shell).not.toBeInstanceOf(Promise);
    expect(shell).toBe(await tmShellComponent('TmTenantShell')());
  });
});
