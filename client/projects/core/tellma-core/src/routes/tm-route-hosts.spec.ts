// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmShellComponent, tmShellRoute } from './tm-route-hosts';

// The shell routes' loader: a promise until the shell's chunk is in memory,
// then the component itself, so the router activates a first entry into the
// route without waiting a task.

describe('the shell route loader', () => {
  it('returns the component itself once the shell has loaded', async () => {
    const load = tmShellRoute('TmTenantShell');
    const first = load();
    const component = first instanceof Promise ? await first : first;

    expect(load()).toBe(component);
  });

  it("returns another route's component at once after one route loaded the shell", async () => {
    const picker = await tmShellComponent('TmTenantPicker')();

    const signedOut = tmShellRoute('TmSignedOutPage')();

    expect(signedOut).not.toBeInstanceOf(Promise);
    expect(signedOut).not.toBe(picker);
    expect(signedOut).toBe(await tmShellComponent('TmSignedOutPage')());
  });
});
