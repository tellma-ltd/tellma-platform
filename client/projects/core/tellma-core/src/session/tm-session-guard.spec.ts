// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import type { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { TmTelemetry } from '../observe/tm-telemetry';
import { tmProfileCookies } from './tm-profile.testing.util';
import { TmSession } from './tm-session';
import { tmSessionGuard } from './tm-session-guard';
import { TmFakeSessionHost, TmRecordingTelemetry, tmSessionTestingProviders } from './tm-session.testing.util';

/** Runs the guard once, as the router would. */
function guard(): unknown {
  return TestBed.runInInjectionContext(() =>
    tmSessionGuard({} as ActivatedRouteSnapshot, { url: '/17/centers' } as RouterStateSnapshot),
  );
}

describe('tmSessionGuard', () => {
  let host: TmFakeSessionHost;

  beforeEach(() => {
    host = new TmFakeSessionHost();
    host.cookieJar = tmProfileCookies('0b4f');
    TestBed.configureTestingModule({ providers: tmSessionTestingProviders(host, null, 0) });
    // The session reads the cookie at its first injection, as at startup.
    TestBed.inject(TmSession);
  });

  it('allows the same user, synchronously', () => {
    expect(guard()).toBe(true);
  });

  it('denies a missing session and runs the expiry path', () => {
    host.cookieJar = '';
    expect(guard()).toBe(false);
    expect(TestBed.inject(TmSession).state()).toBe('expired');
    expect((TestBed.inject(TmTelemetry) as TmRecordingTelemetry).events).toEqual([
      { name: 'tm.session.expired', props: { source: 'cookie' } },
    ]);
  });

  it('denies another user and runs the changed-user path', () => {
    host.cookieJar = tmProfileCookies('7c1e');
    expect(guard()).toBe(false);
    expect(host.replaced).toEqual(['/']);
    expect(TestBed.inject(TmSession).state()).toBe('active');
  });
});
