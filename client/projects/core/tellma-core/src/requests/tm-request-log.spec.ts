// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { TmRequestLog } from './tm-request-log';

describe('TmRequestLog', () => {
  it('keeps nothing by default, and its handles end quietly', () => {
    const log = TestBed.inject(TmRequestLog);
    const handle = log.begin({
      traceId: '0af7651916cd43dd8448eb211c80319c',
      route: 'centers/query',
      tenantId: 17,
      method: 'POST',
      background: false,
    });
    expect(() => handle.end({ outcome: 'ok', status: 200, serverBuild: '1.0.0' })).not.toThrow();
    expect(() => handle.end({ outcome: 'error', status: 500, code: 'server' })).not.toThrow();
    expect(log.recent()).toEqual([]);
    expect(log.diagnosticsText()).toBe('');
    expect(log.diagnosticsText(3)).toBe('');
    expect(() => log.copy()).not.toThrow();
  });

  it('is one instance per root injector', () => {
    expect(TestBed.inject(TmRequestLog)).toBe(TestBed.inject(TmRequestLog));
  });
});
