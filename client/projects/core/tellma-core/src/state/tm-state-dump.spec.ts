// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { tmAppRig, tmNavigate, TM_T17 } from '../routes/tm-app.testing.util';
import { TmDrafts } from './memory/tm-drafts';
import { TmPageState, tmPageStateKey } from './memory/tm-page-state';
import { tmPreferenceKey } from './prefs/tm-preference-key';
import { TmPreferences } from './prefs/tm-preferences';
import { TmStateDump } from './tm-state-dump';

// The state's dump section in a whole test app, warm in tenant 17.

describe('TmStateDump', () => {
  it('lists names and tags, never values', async () => {
    const rig = await tmAppRig();
    await tmNavigate('/17/centers');
    await rig.store.put({ kind: 'device' }, 'zone', { tag: null, savedAt: 1, data: { auto: true } });
    rig.api.respond('users/me/preferences/set', () => new Promise(() => undefined));
    TestBed.inject(TmPreferences).set(tmPreferenceKey('nav.pinnedBarVisible', 'curated', true), false);
    const drafts = TestBed.inject(TmDrafts);
    drafts.save(drafts.key(17, 'gl.Center', 5), { name: 'Secret name' });
    TestBed.inject(TmPageState).remember(tmPageStateKey('/17/centers'), { rows: ['Secret row'] });

    const dump = TestBed.inject(TmStateDump).read();
    expect(dump.store.mode).toBe('memory');
    expect(dump.store.docs).toEqual(
      expect.arrayContaining([
        { partition: 'device', name: 'zone@1', tag: null, savedAt: 1 },
        expect.objectContaining({ partition: 'tenant', name: 'me@1' }),
        expect.objectContaining({ partition: 'tenant', name: 'settings@1' }),
      ]),
    );
    expect(dump.refresher.lastSyncAt).toBe(rig.store.doc<{ at: number }>(TM_T17, 'sync')()?.data.at);
    expect(dump.preferences).toEqual({ dirty: ['nav.pinnedBarVisible'] });
    expect(dump.drafts).toEqual(['17:gl.Center:5']);
    expect(dump.pageState).toEqual(['/17/centers']);
    expect(JSON.stringify(dump)).not.toMatch(/Secret|auto/);
  });
});
