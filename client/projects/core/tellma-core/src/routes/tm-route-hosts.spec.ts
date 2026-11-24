// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmShellComponent, tmShellRoute } from './tm-route-hosts';

// The shell routes' loader: a promise until the component's own load has
// settled, then the component itself, so the router activates a later entry
// into the route without waiting a task.

describe('the shell route loader', () => {
  it('returns the component itself once its load has settled', async () => {
    const load = tmShellRoute('TmTenantShell');
    const first = load();
    const component = first instanceof Promise ? await first : first;

    expect(load()).toBe(component);
  });

  it('returns at once a component that another load brought in, such as a preload', async () => {
    const signedOut = await tmShellComponent('TmSignedOutPage')();

    expect(tmShellRoute('TmSignedOutPage')()).toBe(signedOut);
  });

  it("keeps each component apart: a tenantless page's load does not stand in for another's", async () => {
    const picker = await tmShellComponent('TmTenantPicker')();
    const signedOut = await tmShellComponent('TmSignedOutPage')();

    expect(tmShellRoute('TmTenantPicker')()).toBe(picker);
    expect(tmShellRoute('TmSignedOutPage')()).toBe(signedOut);
    expect(picker).not.toBe(signedOut);
  });
});
