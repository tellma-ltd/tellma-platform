// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmCallsRoute,
  type TmDroppedMail,
  tmIsServerPath,
  tmMailIsFor,
  tmProblemResponse,
  tmSignInCode,
} from './tm-e2e-wire';

describe('the e2e helpers wire rules', () => {
  // --- Which requests are the server's ---
  it('treats the APIs, the BFF, the hub, blobs and MCP as the server, under a tenant or not', () => {
    for (const path of [
      '/api/distribution-info',
      '/bff/user',
      '/mcp',
      '/17/api/web/client/sync',
      '/1/hub',
      '/2/blobs/a/b',
      '/3/mcp',
    ]) {
      expect(tmIsServerPath(path), path).toBe(true);
    }
  });

  it('lets pages, static files, health probes and the identity engine pass', () => {
    for (const path of [
      '/',
      '/1',
      '/1/customers',
      '/tenants',
      '/main-ABC.js',
      '/ngsw.json',
      '/health/live',
      '/id/connect/token',
      '/01/api/x',
      '/api2/x',
    ]) {
      expect(tmIsServerPath(path), path).toBe(false);
    }
  });

  // --- Which route a request calls ---
  it('matches a route after /api/ under any tenant, without the query string', () => {
    expect(tmCallsRoute('/17/api/web/client/sync', 'web/client/sync')).toBe(true);
    expect(tmCallsRoute('/api/web/client/sync', 'web/client/sync')).toBe(true);
    expect(tmCallsRoute('/17/api/web/client/sync', 'web/client/sync/')).toBe(true);
    expect(tmCallsRoute('/17/api/web/client/sync/more', 'web/client/sync')).toBe(false);
    expect(tmCallsRoute('/17/hub/web/client/sync', 'web/client/sync')).toBe(false);
  });

  it('matches a whole path exactly when the route starts with a slash', () => {
    expect(tmCallsRoute('/bff/user', '/bff/user')).toBe(true);
    expect(tmCallsRoute('/bff/user/x', '/bff/user')).toBe(false);
    expect(tmCallsRoute('/1/api/bff/user', '/bff/user')).toBe(false);
  });

  // --- The faked problem ---
  it('answers a problem in the server shape, with its headers and extensions', () => {
    const response = tmProblemResponse({
      status: 403,
      code: 'forbidden',
      headers: { 'Tellma-Build': 'b2' },
      extensions: { reason: 'permission', permission: 'Customers.Read' },
    });

    expect(response.status).toBe(403);
    expect(response.headers).toEqual({
      'content-type': 'application/problem+json',
      'Tellma-Build': 'b2',
    });
    expect(JSON.parse(response.body)).toEqual({
      type: 'https://tellma.com/problems/forbidden',
      title: 'The request was refused.',
      status: 403,
      code: 'forbidden',
      errors: [],
      errorDetails: null,
      reason: 'permission',
      permission: 'Customers.Read',
    });
  });

  it('keeps a given title', () => {
    expect(
      JSON.parse(tmProblemResponse({ status: 404, code: 'not-found', title: 'Gone' }).body).title,
    ).toBe('Gone');
  });

  // --- Sign-in codes in dropped mail ---
  it('reads the eight-digit sign-in code from a message, and nothing else', () => {
    expect(tmSignInCode('Your sign-in code is 01234567. It expires in 10 minutes.')).toBe(
      '01234567',
    );
    expect(tmSignInCode('Call 123456789 or 1234567.')).toBeNull();
    expect(tmSignInCode('No code here.')).toBeNull();
  });

  it('matches a primary recipient without regard to case', () => {
    const mail: TmDroppedMail = {
      version: 1,
      sentAt: '2026-11-23T10:00:00Z',
      channel: 'live',
      audience: 'internal',
      to: [{ address: 'Admin@Localhost', displayName: null }],
      cc: [{ address: 'sales@localhost', displayName: null }],
      bcc: [],
      subject: 'Your sign-in code',
      textBody: '12345678',
      htmlBody: null,
    };

    expect(tmMailIsFor(mail, 'admin@localhost')).toBe(true);
    expect(tmMailIsFor(mail, 'sales@localhost')).toBe(false);
  });
});
