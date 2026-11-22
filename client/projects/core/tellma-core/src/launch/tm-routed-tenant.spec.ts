// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmRoutedTenant, tmTenantSegment } from './tm-routed-tenant';

describe('the routed tenant', () => {
  it.each<[string, number | null]>([
    ['/17', 17],
    ['/17/centers', 17],
    ['/17?x=1', 17],
    ['/17#top', 17],
    [`/${Number.MAX_SAFE_INTEGER}/centers`, Number.MAX_SAFE_INTEGER],
    ['/', null],
    ['', null],
    ['/tenants', null],
    ['/017', null],
    ['/0', null],
    ['/17x', null],
    ['/17;x=1/centers', null],
    ['/99999999999999999999', null],
    [`/${Number.MAX_SAFE_INTEGER + 1}/centers`, null],
  ])('routes %s into tenant %s', (url, tenantId) => {
    expect(tmRoutedTenant(url)).toBe(tenantId);
  });

  it('follows the tenant segment rule of the route table for every first segment', () => {
    for (const segment of ['1', '17', '017', '0', '-1', '1.5', '1e3', '99999999999999999999', String(Number.MAX_SAFE_INTEGER)]) {
      expect(tmRoutedTenant(`/${segment}/centers`)).toBe(tmTenantSegment(segment));
    }
  });
});
