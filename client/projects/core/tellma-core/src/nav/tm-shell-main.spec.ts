// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router, type CanActivateFn } from '@angular/router';

import { TmShellMain } from './tm-shell-main';

/** A routed page. */
@Component({ selector: 'tm-test-page', template: '' })
class TestPage {}

/** A page that replaces the route host. */
@Component({ selector: 'tm-test-tenant-state', template: '' })
class TestTenantState {}

/** Configures the router over a few pages, the last of which replaces `main` while it navigates. */
function rig(): { main: TmShellMain; router: Router } {
  const replaceOnTheWay: CanActivateFn = () => {
    TestBed.inject(TmShellMain).replace(TestTenantState, { state: 'suspended' });
    return true;
  };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([
        { path: 'a', component: TestPage },
        { path: 'b', component: TestPage },
        { path: 'suspended', component: TestPage, canActivate: [replaceOnTheWay] },
      ]),
    ],
  });
  return { main: TestBed.inject(TmShellMain), router: TestBed.inject(Router) };
}

describe('TmShellMain', () => {
  it('shows the route host until something replaces it', () => {
    const { main } = rig();
    expect(main.override()).toBeNull();
  });

  it('shows a replacement with its inputs, until it is restored', async () => {
    const { main, router } = rig();
    await router.navigateByUrl('/a');

    main.replace(TestTenantState, { state: 'suspended' });
    expect(main.override()).toEqual({ component: TestTenantState, inputs: { state: 'suspended' } });

    main.restore();
    expect(main.override()).toBeNull();
  });

  it('keeps a replacement without inputs as the component alone', async () => {
    const { main, router } = rig();
    await router.navigateByUrl('/a');
    main.replace(TestTenantState);
    expect(main.override()).toEqual({ component: TestTenantState });
  });

  it('keeps the replacement through a change of query or fragment, and restores the route host on another path', async () => {
    const { main, router } = rig();
    await router.navigateByUrl('/a');
    main.replace(TestTenantState);

    await router.navigateByUrl('/a?tab=2#notes');
    expect(main.override()?.component).toBe(TestTenantState);

    await router.navigateByUrl('/b');
    expect(main.override()).toBeNull();

    // Back on the first path, the route host stays: the replacement ended.
    await router.navigateByUrl('/a');
    expect(main.override()).toBeNull();
  });

  it('gives a replacement asked for during a navigation to the page being navigated to', async () => {
    const { main, router } = rig();
    await router.navigateByUrl('/a');

    await router.navigateByUrl('/suspended');
    expect(main.override()).toEqual({ component: TestTenantState, inputs: { state: 'suspended' } });

    await router.navigateByUrl('/b');
    expect(main.override()).toBeNull();
  });
});
