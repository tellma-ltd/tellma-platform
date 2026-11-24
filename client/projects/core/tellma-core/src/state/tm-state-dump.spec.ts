// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { provideTellmaStateTesting, provideTellmaTesting } from '@tellma/core-testing';

import { tmAppRig, tmNavigate, TM_T17 } from '../routes/tm-app.testing.util';
import { TmLeader } from './leader/tm-leader';
import { TM_DRAFTS_STORAGE } from './memory/tm-draft-keys';
import { tmPreferenceKey } from './prefs/tm-preference-key';
import { TmPreferences } from './prefs/tm-preferences';
import { TM_STATE_PARTS, TmStateDump } from './tm-state-dump';

// The state's dump section, in a whole test app warm in tenant 17 and on its
// own. The tab's memory is another entry point's, which a page loads with
// import().

describe('TmStateDump', () => {
  it('lists names and tags, never values', async () => {
    const rig = await tmAppRig();
    await tmNavigate('/17/centers');
    const { TmDrafts, TmPageState, tmPageStateKey } = await import('@tellma/core/memory');
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
    expect(dump.leader).toEqual({ isLeader: false, hubStatus: TestBed.inject(TmLeader).hubStatus() });
    expect(dump.refresher.lastSyncAt).toBe(rig.store.doc<{ at: number }>(TM_T17, 'sync')()?.data.at);
    expect(dump.preferences).toEqual({ dirty: ['nav.pinnedBarVisible'] });
    expect(dump.drafts).toEqual(['17:gl.Center:5']);
    expect(dump.pageState).toEqual(['/17/centers']);
    expect(JSON.stringify(dump)).not.toMatch(/Secret|auto/);
  });

  it("reports the leader as the election last wrote it: whether this tab leads, and the leader's connection", () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
    const leader = TestBed.inject(TmLeader);
    leader.ɵisLeader.set(true);
    leader.ɵhubStatus.set({ state: 'reconnecting', since: 1_700_000_000_000, leaderVisible: false });
    expect(TestBed.inject(TmStateDump).read().leader).toEqual({
      isLeader: true,
      hubStatus: { state: 'reconnecting', since: 1_700_000_000_000, leaderVisible: false },
    });
  });

  it('reads the parts of services not yet created as empty, and creates none of them', () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
    const parts = TestBed.inject(TM_STATE_PARTS);
    const dump = TestBed.inject(TmStateDump).read();
    expect(dump).toEqual(expect.objectContaining({ preferences: { dirty: [] }, drafts: [], pageState: [] }));
    expect(dump.leader).toEqual({ isLeader: false, hubStatus: { state: 'connecting', since: expect.any(Number), leaderVisible: true } });
    // No service reported, so none was created by the read.
    expect(parts).toEqual({});
  });

  it('lists the drafts session storage holds from before a reload while the drafts service is not loaded', () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
    const storage = TestBed.inject(TM_DRAFTS_STORAGE)!;
    storage.setItem('tm.draft:test-user:17:gl.Center:5', '{"name":"Secret name"}');
    storage.setItem('tm.draft:test-user:17:gl.Center:new:2', '{}');
    storage.setItem('tm.unrelated', 'x');
    const dump = TestBed.inject(TmStateDump).read();
    expect([...dump.drafts].sort()).toEqual(['17:gl.Center:5', '17:gl.Center:new:2']);
    expect(JSON.stringify(dump)).not.toMatch(/Secret/);
    expect(TestBed.inject(TM_STATE_PARTS)).toEqual({});
  });
});
