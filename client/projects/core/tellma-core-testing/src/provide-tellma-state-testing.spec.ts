// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Injector } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  TmClientStore,
  TmDeviceZone,
  TmLeader,
  TmSession,
  TmStoreRefusedError,
  TmTabBus,
  type TmBusMessage,
  type TmPartition,
  ɵTM_LEADER_LOAD,
  ɵTM_ZONE_CHECK_LOAD,
} from '@tellma/core';
import { TmDrafts } from '@tellma/core/drafts';
import { ɵTM_HUB_FACTORY } from '@tellma/core/leader';

import { provideTellmaStateTesting } from './provide-tellma-state-testing';
import { provideTellmaTesting } from './provide-tellma-testing';
import { TmFakeHub } from './tm-fake-hub';
import { TmFakeSession, tmTestProfile } from './tm-fake-session';
import { TmFakeTabBus } from './tm-fake-tab-bus';
import { TmFakeTelemetry } from './tm-fake-telemetry';

const USER: TmPartition = { kind: 'user', sub: tmTestProfile().sub };
// Saved now: an older user document reads as absent once past the idle window.
const TENANTS = { tag: null, savedAt: Date.now(), data: ['acme'] };

describe('provideTellmaStateTesting', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
  });

  it('puts the fakes behind the tab bus and the session', () => {
    expect(TestBed.inject(TmTabBus)).toBe(TestBed.inject(TmFakeTabBus));
    expect(TestBed.inject(TmSession)).toBe(TestBed.inject(TmFakeSession));
  });

  it("opens the leader's realtime connections on the fake hub, which reports through the leader's sink", async () => {
    const factory = TestBed.inject(ɵTM_HUB_FACTORY)!;
    const reports: string[] = [];
    const sink = {
      event: (event: { readonly name: string }) => reports.push(`event ${event.name}`),
      state: (state: string) => reports.push(`state ${state}`),
      connected: () => reports.push('connected'),
      refused: (status: number) => reports.push(`refused ${status}`),
      ended: (reason: string) => reports.push(`ended ${reason}`),
    };
    const connection = await factory(17, sink, TestBed.inject(Injector));
    const hub = TestBed.inject(TmFakeHub);
    expect(hub.connections).toEqual([connection]);
    connection.start();
    connection.nudge();
    await connection.stop();
    const fake = hub.connections[0]!;
    expect([fake.tenantId, fake.starts, fake.nudges, fake.stops]).toEqual([17, 1, 1, 1]);
    // A test may hold the stop open.
    fake.stopResult = new Promise<void>(() => undefined);
    expect(fake.stop()).toBe(fake.stopResult);
    fake.connect();
    fake.emit('acme.jobs.changed', { id: 4 });
    fake.drop();
    fake.refuse(403);
    fake.end('access-revoked');
    expect(reports).toEqual([
      'state connected',
      'connected',
      'event acme.jobs.changed',
      'state reconnecting',
      'refused 403',
      'event session.ended',
      'state reconnecting',
      'ended access-revoked',
    ]);
  });

  it('runs the store in memory, with the guard passing for the test user', async () => {
    const store = TestBed.inject(TmClientStore);
    await store.put(USER, 'tenants', TENANTS);
    expect(store.mode).toBe('memory');
    expect(store.doc(USER, 'tenants')()).toEqual(TENANTS);
    // In memory, no other tab shares the store, so nothing is posted.
    expect(TestBed.inject(TmFakeTabBus).posted()).toEqual([]);
  });

  it('keeps the boot record in memory, starting from the defaults', () => {
    const store = TestBed.inject(TmClientStore);
    expect(store.boot.read()).toEqual({ v: 1, theme: 'system', lang: 'en', dir: 'ltr', sub: null, lastTenantId: null });
    store.boot.patch({ theme: 'dark' });
    expect(store.boot.read().theme).toBe('dark');
  });

  it('lets the test refuse writes through the session', async () => {
    const store = TestBed.inject(TmClientStore);
    const session = TestBed.inject(TmFakeSession);
    session.setCheck('changed');
    await expect(store.put(USER, 'tenants', TENANTS)).rejects.toThrow(TmStoreRefusedError);
    session.setCheck(null);
    session.setState('expired');
    await expect(store.put(USER, 'tenants', TENANTS)).rejects.toMatchObject({ reason: 'session' });
    const refusals = TestBed.inject(TmFakeTelemetry)
      .recent()
      .filter((item) => item.name === 'tm.store.refused');
    expect(refusals.map((item) => item.props['reason'])).toEqual(['cookie', 'session']);
  });

  it('starts no leader election, so the tab reports a follower', async () => {
    await expect(TestBed.inject(ɵTM_LEADER_LOAD)()).resolves.toBeNull();
    expect(TestBed.inject(TmLeader).isLeader()).toBe(false);
  });

  it("checks no device time zone, so a test app's prompt never opens", async () => {
    await expect(TestBed.inject(ɵTM_ZONE_CHECK_LOAD)()).resolves.toBeNull();
    await expect(TestBed.inject(TmDeviceZone).check()).resolves.toBeUndefined();
  });

  it("keeps drafts in a session storage of the test's own, never the browser's", () => {
    const drafts = TestBed.inject(TmDrafts);
    const key = drafts.key(17, 'gl.Center', 5);
    drafts.save(key, { name: 'Jeddah' });
    drafts.ɵflush();
    expect(drafts.load(key)).toEqual({ name: 'Jeddah' });
    expect(drafts.dirtyCount()).toBe(1);
    expect(sessionStorage.getItem(key)).toBeNull();
  });
});

describe('provideTellmaTesting', () => {
  it('runs the store in memory too, so no test touches IndexedDB', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting()] });
    // With no session, a read is all a test can do; it finds nothing.
    await TestBed.inject(TmClientStore).readAll({ kind: 'device' });
    expect(TestBed.inject(TmClientStore).mode).toBe('memory');
  });
});

describe('TmFakeTabBus', () => {
  it('records posts and delivers played messages', () => {
    const bus = new TmFakeTabBus();
    const received: TmBusMessage[] = [];
    bus.messages.subscribe((message) => received.push(message));
    bus.post({ type: 'leader.probe', tenantId: 17 });
    bus.receive({ type: 'store.changed', partition: 'u:test-user/', names: ['tenants@1'] });
    expect(bus.posted()).toEqual([{ type: 'leader.probe', tenantId: 17 }]);
    expect(received).toEqual([{ type: 'store.changed', partition: 'u:test-user/', names: ['tenants@1'] }]);
    bus.clear();
    expect(bus.posted()).toEqual([]);
  });
});

describe('TmFakeSession', () => {
  it('starts signed in as the test user, follows the profile in its check, and records sign-ins', () => {
    const session = new TmFakeSession();
    expect([session.profile()?.sub, session.state(), session.check()]).toEqual(['test-user', 'active', 'same']);
    session.setProfile(null);
    expect(session.check()).toBe('missing');
    void session.signIn({ returnUrl: '/17' });
    expect(session.signIns()).toEqual([{ returnUrl: '/17' }]);
  });
});
