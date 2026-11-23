// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmBuildVerdict, tmCompareBuilds } from './tm-api-verdict';

// The outdated-client verdict's build rules: the SemVer order, and the side
// each order blames.

describe('tmBuildVerdict', () => {
  const ui = '1.4.0+3f9c2ab';
  const windowMs = 30_000;

  it('blames the client when the server is newer, and calls the identical build the same', () => {
    expect(tmBuildVerdict(ui, '1.5.0', new Map(), 0, windowMs)).toBe('client-outdated');
    expect(tmBuildVerdict(ui, ui, new Map(), 0, windowMs)).toBe('same');
  });

  it('blames an older server for its first verdict and within its retry window, then leaves it to the worker', () => {
    const judged = new Map<string, number>();
    expect(tmBuildVerdict(ui, '1.3.0', judged, 1_000, windowMs)).toBe('server-outdated');
    expect(judged.get('1.3.0')).toBe(1_000);
    expect(tmBuildVerdict(ui, '1.3.0', judged, 30_999, windowMs)).toBe('server-outdated');
    expect(tmBuildVerdict(ui, '1.3.0', judged, 31_000, windowMs)).toBeNull();
    expect(judged.get('1.3.0')).toBe(1_000);
  });

  it('leaves unordered builds, and a missing one, to the worker', () => {
    expect(tmBuildVerdict(ui, '1.4.0+9e8d7c6', new Map(), 0, windowMs)).toBeNull();
    expect(tmBuildVerdict(ui, null, new Map(), 0, windowMs)).toBeNull();
  });
});

describe('tmCompareBuilds', () => {
  const ui = '1.4.0+3f9c2ab';

  it('orders by SemVer precedence, ignoring the commit', () => {
    expect(tmCompareBuilds(ui, '1.4.1+0000000')).toBe(1);
    expect(tmCompareBuilds(ui, '1.10.0')).toBe(1);
    expect(tmCompareBuilds(ui, '1.3.9+3f9c2ab')).toBe(-1);
    expect(tmCompareBuilds(ui, '1.4.0-rc.2')).toBe(-1);
    expect(tmCompareBuilds('1.4.0-rc.2', '1.4.0-rc.10')).toBe(1);
    expect(tmCompareBuilds('1.4.0-rc.2', '1.4.0-rc')).toBe(-1);
    expect(tmCompareBuilds('1.4.0-2', '1.4.0-beta')).toBe(1);
    expect(tmCompareBuilds('1.4.0-beta', '1.4.0-alpha')).toBe(-1);
  });

  it('is 0 only for the identical build', () => {
    expect(tmCompareBuilds(ui, ui)).toBe(0);
  });

  it('is unordered for the same version with another commit, or for no version', () => {
    expect(tmCompareBuilds(ui, '1.4.0+9e8d7c6')).toBeNull();
    expect(tmCompareBuilds(ui, '1.4.0')).toBeNull();
    expect(tmCompareBuilds(ui, null)).toBeNull();
    expect(tmCompareBuilds(ui, 'nightly')).toBeNull();
    expect(tmCompareBuilds('dev', '1.4.0')).toBeNull();
  });
});
