// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmPreferenceKey } from './tm-preference-key';

// The key declaration: its members, and the grammar it checks in development.

describe('tmPreferenceKey', () => {
  it('declares a key with its kind, fallback and check', () => {
    const isNumber = (value: unknown): value is number => typeof value === 'number';
    expect(tmPreferenceKey('acme.creditReview.threshold', 'write-behind', 90, isNumber)).toEqual({
      key: 'acme.creditReview.threshold',
      kind: 'write-behind',
      fallback: 90,
      validate: isNumber,
    });
    expect(tmPreferenceKey('nav.pinnedBarVisible', 'curated', true)).toEqual({
      key: 'nav.pinnedBarVisible',
      kind: 'curated',
      fallback: true,
    });
  });

  it("accepts the server's grammar: an entity name and hyphens in later segments", () => {
    expect(() => tmPreferenceKey('search.acme.Customer.view', 'write-behind', {})).not.toThrow();
    expect(() => tmPreferenceKey('report.42-a.params', 'write-behind', {})).not.toThrow();
  });

  it('throws in development for a key that breaks the grammar or runs over 128 characters', () => {
    for (const key of ['Nav.pinned', 'nav..pinned', 'nav.pinned.', 'nav-x.pinned', 'nav.-x', '', `a.${'b'.repeat(127)}`]) {
      expect(() => tmPreferenceKey(key, 'curated', 0), key).toThrow(/breaks the key grammar/);
    }
  });
});
