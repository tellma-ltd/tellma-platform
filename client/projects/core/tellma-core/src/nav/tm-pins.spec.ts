// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { tmAppRig, tmNavigate, tmSettle, TM_T17_DOCS } from '../routes/tm-app.testing.util';
import { TmPreferences } from '../state/prefs/tm-preferences';
import { tmPinnedBarVisibleKey, tmPinsKey } from './tm-pins';

// The pins model's keys: how each is stored, and a stored value of the wrong
// shape reading as the fallback, alone and in a whole test app.

describe('the pins model', () => {
  describe('the preference keys', () => {
    it('stores the pins and the bar switch per tenant, as curated preferences', () => {
      expect([tmPinsKey.key, tmPinsKey.kind, tmPinsKey.fallback]).toEqual(['nav.pinned', 'curated', { v: 1, items: [] }]);
      expect([tmPinnedBarVisibleKey.key, tmPinnedBarVisibleKey.kind, tmPinnedBarVisibleKey.fallback]).toEqual(['nav.pinnedBarVisible', 'curated', true]);
    });

    it('reads a value of the wrong shape as no pins', () => {
      expect(tmPinsKey.validate?.({ v: 1, items: [] })).toBe(true);
      expect(tmPinsKey.validate?.({ v: 2, items: [] })).toBe(false);
      expect(tmPinsKey.validate?.({ v: 1, items: {} })).toBe(false);
      expect(tmPinsKey.validate?.([])).toBe(false);
      expect(tmPinnedBarVisibleKey.validate?.('false')).toBe(false);
    });
  });

  describe('in the app', () => {
    it('reads a stored value of the wrong shape as no pins', async () => {
      const me = { ...TM_T17_DOCS.me, preferences: { 'nav.pinned': JSON.stringify({ v: 2, pins: [] }) } };
      await tmAppRig({ docs: { ...TM_T17_DOCS, me } });
      await tmNavigate('/17/centers');
      await tmSettle();
      expect(TestBed.inject(TmPreferences).get(tmPinsKey)()).toEqual({ v: 1, items: [] });
    });
  });
});
