// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { TM_BOOT_RESULT } from '../boot/tm-boot-result';
import { TmLaunch } from './tm-launch';

// The launch mark: seeded from the boot script, set by the launch redirect,
// and ended by a 2xx from its tenant, a route into another tenant, or a
// tenant-not-found that takes it.

/** A page for every route. */
@Component({ template: '' })
class TmPage {}

/** Configures the mark with the boot script's `auto`. */
function setUp(auto: number | null): TmLaunch {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([{ path: '**', component: TmPage }]),
      { provide: TM_BOOT_RESULT, useValue: auto === null ? null : { auto } },
    ],
  });
  return TestBed.inject(TmLaunch);
}

describe('TmLaunch', () => {
  it('is seeded from the boot script', () => {
    expect(setUp(17).automatic()).toBe(17);
  });

  it('starts empty without a boot result', () => {
    expect(setUp(null).automatic()).toBeNull();
  });

  it('is marked by the launch redirect', () => {
    const launch = setUp(null);
    launch.markAutomatic(1);
    expect(launch.automatic()).toBe(1);
  });

  it('ends at the first 2xx from its tenant only', () => {
    const launch = setUp(17);
    launch.settle(18);
    expect(launch.automatic()).toBe(17);
    launch.settle(17);
    expect(launch.automatic()).toBeNull();
  });

  it('is taken once by a tenant-not-found from its tenant', () => {
    const launch = setUp(17);
    expect(launch.takeAutomatic(18)).toBe(false);
    expect(launch.automatic()).toBe(17);
    expect(launch.takeAutomatic(17)).toBe(true);
    expect(launch.takeAutomatic(17)).toBe(false);
    expect(launch.automatic()).toBeNull();
  });

  it('survives routes within its tenant and outside any tenant, and ends at a route into another', async () => {
    const launch = setUp(17);
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/17/centers?x=1');
    await router.navigateByUrl('/tenants');
    await router.navigateByUrl('/170');
    expect(launch.automatic()).toBeNull();
    launch.markAutomatic(17);
    await router.navigateByUrl('/17');
    expect(launch.automatic()).toBe(17);
    await router.navigateByUrl('/18/centers');
    expect(launch.automatic()).toBeNull();
  });

  it('counts a navigation that ended before its first injection', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', component: TmPage }]), { provide: TM_BOOT_RESULT, useValue: { auto: 17 } }],
    });
    await TestBed.inject(Router).navigateByUrl('/18');
    expect(TestBed.inject(TmLaunch).automatic()).toBeNull();
  });
});
