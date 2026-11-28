// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmBuildVerdict } from './tm-api-verdict';

// The outdated-client verdict's build rules: the side each order of the two
// builds blames. The order itself is TmAppVersion.compare's, pinned in its
// own spec.

describe('tmBuildVerdict', () => {
  const windowMs = 30_000;

  it('blames the client when the server is newer, and calls the identical build the same', () => {
    expect(tmBuildVerdict(1, '1.5.0', new Map(), 0, windowMs)).toBe('client-outdated');
    expect(tmBuildVerdict(0, '1.4.0+3f9c2ab', new Map(), 0, windowMs)).toBe('same');
  });

  it('blames an older server for its first verdict and within its retry window, then leaves it to the worker', () => {
    const judged = new Map<string, number>();
    expect(tmBuildVerdict(-1, '1.3.0', judged, 1_000, windowMs)).toBe('server-outdated');
    expect(judged.get('1.3.0')).toBe(1_000);
    expect(tmBuildVerdict(-1, '1.3.0', judged, 30_999, windowMs)).toBe('server-outdated');
    expect(tmBuildVerdict(-1, '1.3.0', judged, 31_000, windowMs)).toBeNull();
    expect(judged.get('1.3.0')).toBe(1_000);
  });

  it('leaves unordered builds, and a missing one, to the worker', () => {
    expect(tmBuildVerdict(null, '1.4.0+9e8d7c6', new Map(), 0, windowMs)).toBeNull();
    expect(tmBuildVerdict(null, null, new Map(), 0, windowMs)).toBeNull();
  });
});
