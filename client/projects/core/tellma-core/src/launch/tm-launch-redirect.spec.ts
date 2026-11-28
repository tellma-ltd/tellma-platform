// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Location } from '@angular/common';
import { TestBed } from '@angular/core/testing';

import { TmApiError } from '../api/tm-api-error';
import { tmAppRig, tmNavigate, tmSettle, tmSettleUntil, TM_TEST_SUB, tmTestMembership, type TmAppRig, type TmAppRigOptions } from '../routes/tm-app.testing.util';
import { TmClientStore } from '../state/store/tm-client-store';
import { TmLaunch } from './tm-launch';

describe('the launch redirect', () => {
  let rig: TmAppRig;

  /** Starts the app, patches the boot record, and opens `/`. */
  async function launch(options: TmAppRigOptions & { readonly lastTenantId?: number; readonly sub?: string } = {}): Promise<void> {
    rig = await tmAppRig(options);
    TestBed.inject(TmClientStore).boot.patch({ lastTenantId: options.lastTenantId ?? null, sub: options.sub ?? TM_TEST_SUB });
    await tmNavigate('/');
    await tmSettle();
  }

  /** The tenant of the automatic entry, if any. */
  const automatic = (): number | null => TestBed.inject(TmLaunch).automatic();

  afterEach(() => TestBed.resetTestingModule());

  it('replaces / with the last tenant when the boot record names one for this user', async () => {
    // Two live tenants would lead to the picker; the last tenant wins.
    await launch({ lastTenantId: 17, tenants: [tmTestMembership(17), tmTestMembership(18)] });
    expect(rig.router.url).toBe('/17');
    expect(automatic()).toBe(17);
  });

  it('ignores a last tenant another user left', async () => {
    await launch({ lastTenantId: 18, sub: 'someone-else', tenants: [tmTestMembership(17)] });
    expect(rig.router.url).toBe('/17');
    expect(automatic()).toBe(17);
  });

  it('replaces / with the one live tenant of the cached memberships', async () => {
    await launch({ tenants: [tmTestMembership(17), tmTestMembership(30, { category: 'Sandbox' })] });
    expect(rig.router.url).toBe('/17');
    expect(automatic()).toBe(17);
  });

  it('replaces / with the picker when several tenants are live', async () => {
    await launch({ tenants: [tmTestMembership(17), tmTestMembership(18)] });
    expect(rig.router.url).toBe('/tenants');
    expect(automatic()).toBeNull();
  });

  it('reads the memberships when none are cached, then decides', async () => {
    rig = await tmAppRig();
    rig.api.respond('/bff/user', { tenants: [tmTestMembership(17)] });
    const harness = await tmNavigate('/');
    expect(harness.routeNativeElement?.querySelector('main[aria-busy="true"] tm-skeleton')).not.toBeNull();
    // The redirect follows the memberships' read, and its page loads the shell's chunk.
    await tmSettleUntil(() => rig.router.url !== '/');
    expect(rig.router.url).toBe('/17');
    expect(automatic()).toBe(17);
  });

  it('replaces / with the picker when the memberships cannot be read', async () => {
    rig = await tmAppRig();
    rig.api.respond('/bff/user', () => {
      throw new TmApiError({ kind: 'offline' });
    });
    await tmNavigate('/');
    // The redirect follows the memberships' read, and its page loads the shell's chunk.
    await tmSettleUntil(() => rig.router.url !== '/');
    expect(rig.router.url).toBe('/tenants');
    expect(automatic()).toBeNull();
  });

  it('pushes the redirect of an in-app link to /, so Back returns to the page it left', async () => {
    rig = await tmAppRig();
    TestBed.inject(TmClientStore).boot.patch({ lastTenantId: 17, sub: TM_TEST_SUB });
    await tmNavigate('/17/centers');
    await tmSettle();
    expect(await rig.router.navigateByUrl('/')).toBe(true);
    expect(rig.router.url).toBe('/17');
    // The history keeps the page the link left, one step back.
    const location = TestBed.inject(Location);
    location.back();
    expect(location.path()).toBe('/17/centers');
  });

  it('navigates nowhere once the user leaves / before the memberships are read', async () => {
    rig = await tmAppRig();
    let answer!: (value: unknown) => void;
    rig.api.respond('/bff/user', () => new Promise((resolve) => (answer = resolve)));
    const harness = await tmNavigate('/');
    expect(harness.routeNativeElement?.querySelector('main[aria-busy="true"]')).not.toBeNull();
    await rig.router.navigateByUrl('/signed-out');
    answer({ tenants: [tmTestMembership(17)] });
    await tmSettle();
    expect(rig.router.url).toBe('/signed-out');
  });
});
