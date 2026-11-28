// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TM_SESSION_HOST } from './tm-session-host';

// The real browser behind the session, with no fake: the cookie jar, the
// document's language, the session storage and the address bar, as the
// default host reaches them in production. Nothing here navigates.

describe('TM_SESSION_HOST', () => {
  const language = document.documentElement.lang;

  afterEach(() => {
    document.cookie = 'tellma.profile=; Max-Age=0; Path=/; Secure';
    document.documentElement.lang = language;
  });

  it('reads the cookie jar, and deletes the profile cookie with the attributes the server sets it with', () => {
    document.cookie = 'tellma.profile=abc; Path=/; Secure';
    const host = TestBed.inject(TM_SESSION_HOST);
    expect(host.cookies()).toContain('tellma.profile=abc');
    host.writeCookie('tellma.profile=; Max-Age=0; Path=/; Secure');
    expect(host.cookies()).not.toContain('tellma.profile=');
    expect(document.cookie).not.toContain('tellma.profile=');
  });

  it("reads the document's language, and holds the window's session storage and address bar", () => {
    document.documentElement.lang = 'ar';
    const host = TestBed.inject(TM_SESSION_HOST);
    expect(host.language()).toBe('ar');
    expect(host.sessionStorage).toBe(window.sessionStorage);
    expect(host.location).toBe(window.location);
  });
});
