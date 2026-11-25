// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { provideTellmaStateTesting, provideTellmaTesting, TmFakeSession, TmFakeTelemetry } from '@tellma/core-testing';

import { TmClientStore } from '../state/store/tm-client-store';
import { TM_ZONE_CHECK_LOAD, TmDeviceZone, type TmZoneDoc } from './tm-device-zone';
import { TM_ZONE_USERS_CAP, ɵtmZoneDocWith, ɵtmZoneUser } from './tm-zone-doc';

// The per-device option and the zone document it shares with the check: one
// entry per user, the cap that drops the users written longest ago, and a
// stored document that does not read well.

/** The device's zone document as stored. */
function stored(store: TmClientStore): TmZoneDoc | undefined {
  return store.doc<TmZoneDoc>({ kind: 'device' }, 'zone')()?.data;
}

describe('TmDeviceZone', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] }));

  it("turns the user's option on and off on this device alone, keeping what the tenants have seen", async () => {
    const zone = TestBed.inject(TmDeviceZone);
    const store = TestBed.inject(TmClientStore);
    const sub = TestBed.inject(TmFakeSession).profile()!.sub;
    await store.put<TmZoneDoc>({ kind: 'device' }, 'zone', {
      tag: null,
      savedAt: Date.now(),
      data: { users: { [sub]: { auto: false, seen: { 17: 'Asia/Riyadh' }, at: 500 } } },
    });
    expect(zone.auto()).toBe(false);

    const before = Date.now();
    await zone.setAuto(true, 'settings');
    expect(zone.auto()).toBe(true);
    expect(stored(store)?.users[sub]).toEqual({ auto: true, seen: { 17: 'Asia/Riyadh' }, at: expect.any(Number) });
    expect(stored(store)!.users[sub]!.at).toBeGreaterThanOrEqual(before);

    await zone.setAuto(false, 'prompt');
    expect(zone.auto()).toBe(false);
    const events = TestBed.inject(TmFakeTelemetry)
      .recent()
      .filter((item) => item.name === 'tm.l10n.zone-option');
    expect(events.map((item) => item.props)).toEqual([
      { on: true, source: 'settings' },
      { on: false, source: 'prompt' },
    ]);
  });

  it('reads the option as off, and writes nothing, while no one is signed in', async () => {
    const zone = TestBed.inject(TmDeviceZone);
    TestBed.inject(TmFakeSession).setProfile(null);
    expect(zone.auto()).toBe(false);
    await zone.setAuto(true, 'settings');
    expect(stored(TestBed.inject(TmClientStore))).toBeUndefined();
    expect(TestBed.inject(TmFakeTelemetry).recent().some((item) => item.name === 'tm.l10n.zone-option')).toBe(false);
  });

  it('reports a check chunk that fails to load, and loads it again on the next call', async () => {
    let loads = 0;
    TestBed.overrideProvider(TM_ZONE_CHECK_LOAD, {
      useValue: () => (++loads === 1 ? Promise.reject(new Error('The chunk failed to load.')) : Promise.resolve(null)),
    });
    const zone = TestBed.inject(TmDeviceZone);
    await expect(zone.check()).resolves.toBeUndefined();
    expect(TestBed.inject(TmFakeTelemetry).errors()).toEqual([new Error('The chunk failed to load.')]);
    await expect(zone.check()).resolves.toBeUndefined();
    expect(loads).toBe(2);
  });
});

describe('The zone document', () => {
  it("changes one user's entry and stamps it", () => {
    const doc = ɵtmZoneDocWith(undefined, 'farah', 10, { seen: { tenantId: 17, zone: 'Asia/Riyadh' } });
    expect(doc).toEqual({ users: { farah: { auto: false, seen: { 17: 'Asia/Riyadh' }, at: 10 } } });
    const next = ɵtmZoneDocWith(doc, 'farah', 20, { seen: { tenantId: 18, zone: 'Europe/London' } });
    expect(next.users['farah']).toEqual({ auto: false, seen: { 17: 'Asia/Riyadh', 18: 'Europe/London' }, at: 20 });
    expect(ɵtmZoneDocWith(next, 'farah', 30, { auto: true }).users['farah']).toEqual({
      auto: true,
      seen: { 17: 'Asia/Riyadh', 18: 'Europe/London' },
      at: 30,
    });
  });

  it('keeps at most twenty users, dropping those written longest ago and never the one just written', () => {
    let doc: TmZoneDoc | undefined;
    for (let i = 0; i < TM_ZONE_USERS_CAP; i++) {
      // Written newest first, so the cap must sort by the stamp, not by the order of the keys.
      doc = ɵtmZoneDocWith(doc, `user-${i}`, 1_000 - i, { auto: false });
    }
    expect(Object.keys(doc!.users)).toHaveLength(TM_ZONE_USERS_CAP);
    const next = ɵtmZoneDocWith(doc, 'newcomer', 0, { auto: true });
    expect(Object.keys(next.users)).toHaveLength(TM_ZONE_USERS_CAP);
    expect(next.users['newcomer']).toBeDefined();
    expect(next.users[`user-${TM_ZONE_USERS_CAP - 1}`]).toBeUndefined();
    expect(next.users['user-0']).toBeDefined();
  });

  it('reads a malformed document or entry as none, and drops it on the next write', () => {
    const malformed = { users: { farah: null, omar: { auto: true }, sara: { auto: true, seen: {}, at: 5 } } } as unknown as TmZoneDoc;
    expect(ɵtmZoneUser(malformed, 'farah')).toBeUndefined();
    expect(ɵtmZoneUser(malformed, 'omar')).toBeUndefined();
    expect(ɵtmZoneUser(malformed, 'sara')).toEqual({ auto: true, seen: {}, at: 5 });
    expect(ɵtmZoneUser({ users: 'none' } as unknown as TmZoneDoc, 'farah')).toBeUndefined();
    expect(ɵtmZoneDocWith(malformed, 'farah', 9, { auto: true })).toEqual({
      users: { farah: { auto: true, seen: {}, at: 9 }, sara: { auto: true, seen: {}, at: 5 } },
    });
  });
});
