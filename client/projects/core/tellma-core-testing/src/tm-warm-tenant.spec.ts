// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmClientStore, TmLaunchCache, type TmLaunchDocs, type TmPartition } from '@tellma/core';

import { provideTellmaStateTesting } from './provide-tellma-state-testing';
import { provideTellmaTesting } from './provide-tellma-testing';
import { TmFakeApi } from './tm-fake-api';
import { tmTestProfile } from './tm-fake-session';
import { tmWarmTenant } from './tm-warm-tenant';

const TENANT: TmPartition = { kind: 'tenant', sub: tmTestProfile().sub, tenantId: 17 };

/** Tenant 17's launch documents. */
const DOCS: TmLaunchDocs = {
  settings: {
    tenantId: 17,
    names: ['Acme', null],
    languages: [{ code: 'en', nativeName: 'English', symbol: 'En', isRightToLeft: false }],
    calendars: ['gc'],
    timeZone: 'Asia/Riyadh',
    entries: {},
  },
  me: {
    user: { id: 42, kind: 'Individual', state: 'Joined', name: 'Farah', modifiedAt: '2026-11-01T00:00:00.0000000+00:00' },
    preferencesTag: 'P1',
    preferences: {},
    access: { tag: 'A1', formatVersion: 1, isSystem: false, securables: [], problems: [] },
    tags: { settings: 'S1', preferences: 'P1', permissions: 'A1', entities: 'E1' },
  },
  tenant: { id: 17, category: 'Live', state: 'Active', realtime: true },
};

describe('tmWarmTenant', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] }));

  it('makes a tenant entry warm: the documents read as current, and entering it calls no server', async () => {
    const store = TestBed.inject(TmClientStore);
    await tmWarmTenant(store, TENANT, DOCS, { entityTags: { 'gl.Center': 'C1' } });

    expect(store.doc(TENANT, 'me')()?.data).toEqual(DOCS.me);
    expect(store.doc(TENANT, 'sync')()?.data).toEqual({ at: expect.any(Number), entities: 'E1', entityTags: { 'gl.Center': 'C1' } });
    await expect(TestBed.inject(TmLaunchCache).ɵenter(17)).resolves.toBe(false);
    expect(TestBed.inject(TmFakeApi).calls()).toEqual([]);
  });

  it('leaves the documents absent without the stamp, which is why it writes one', async () => {
    const store = TestBed.inject(TmClientStore);
    await store.put(TENANT, 'me', { tag: null, savedAt: Date.now(), data: DOCS.me });
    expect(store.doc(TENANT, 'me')()).toBeUndefined();
  });
});
