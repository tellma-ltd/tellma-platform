// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { afterEveryRender, Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { TmLaunchCache } from '@tellma/core';

import { tmAppRig, tmNavigate, tmSettle, tmTestMembership } from '../../src/routes/tm-app.testing.util';

// The tenant picker's stand-in: it renders the cached memberships first and
// revalidates them only once it has rendered.

describe('the tenant picker', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('renders the cached memberships before it reads them again', async () => {
    await tmAppRig({ tenants: [tmTestMembership(17), tmTestMembership(18)] });
    const revalidate = vi.spyOn(TestBed.inject(TmLaunchCache), 'revalidateUser');
    // A render hook registered ahead of the picker's own sees, at the render that first holds
    // the picker, the reads asked for before it.
    let readsAtRender: number | undefined;
    afterEveryRender(
      () => {
        if (readsAtRender === undefined && document.querySelector('tm-tenant-picker li') !== null) {
          readsAtRender = revalidate.mock.calls.length;
        }
      },
      { injector: TestBed.inject(Injector) },
    );

    await tmNavigate('/tenants');
    await tmSettle();

    expect(readsAtRender).toBe(0);
    expect(revalidate).toHaveBeenCalledTimes(1);
  });
});
