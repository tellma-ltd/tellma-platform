// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import vectors from './launch-landing-vectors.json';
import type { TmMembership } from './tm-membership';
import { tmSingleLiveTenant } from './tm-single-live-tenant';

// The single-membership rule over the vector file the server's landing test
// links too, so both sides answer every row the same.

describe('tmSingleLiveTenant', () => {
  it('has vectors to run', () => {
    expect(vectors.length).toBeGreaterThan(0);
  });

  for (const vector of vectors) {
    it(vector.name, () => {
      expect(tmSingleLiveTenant(vector.tenants as TmMembership[])).toBe(vector.expected);
    });
  }
});
