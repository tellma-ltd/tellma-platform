// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';

import { tmAppRig, tmSettle, TM_T17, TM_T17_DOCS, type TmAppRig } from '../../routes/tm-app.testing.util';
import type { TmMeResult, TmTenantSettingsForClient } from '../launch/tm-launch-docs';
import { TmPageState, tmPageStateKey } from './tm-page-state';

// Page state in a whole test app, whose route table classifies the keys, over
// tenant 17's stored documents.

const CENTERS = tmPageStateKey('/17/centers');
const CENTER = tmPageStateKey('/17/centers/5');
const CENTER_PANEL = tmPageStateKey('/17/centers/5/panel');
const CUSTOMERS = tmPageStateKey('/17/customers');
const OTHER_TENANT = tmPageStateKey('/18/centers');

describe('TmPageState', () => {
  let rig: TmAppRig;
  let pageState: TmPageState;

  beforeEach(async () => {
    rig = await tmAppRig();
    pageState = TestBed.inject(TmPageState);
  });

  /** Stores tenant 17's envelope or settings, as a sync does, and lets the watch run. */
  async function sync(patch: { readonly me?: TmMeResult; readonly settings?: TmTenantSettingsForClient }): Promise<void> {
    const doc = <T>(data: T) => () => ({ tag: null, savedAt: Date.now(), data });
    await rig.store.ɵwrite(TM_T17, {
      ...(patch.me && { me: doc(patch.me) }),
      ...(patch.settings && { settings: doc(patch.settings) }),
    });
    await tmSettle();
  }

  /** Remembers every key with its own path as its state. */
  function rememberAll(): void {
    for (const key of [CENTERS, CENTER, CENTER_PANEL, CUSTOMERS, OTHER_TENANT]) {
      pageState.remember(key, { path: key });
    }
  }

  /** The stale keys among the ones {@link rememberAll} remembers. */
  function stale(): string[] {
    return [CENTERS, CENTER, CENTER_PANEL, CUSTOMERS, OTHER_TENANT].filter((key) => pageState.isStale(key));
  }

  it('keys a page by its path, without the query or fragment', () => {
    expect(tmPageStateKey('/17/centers?q=jed#d=abc')).toBe('/17/centers');
    expect(tmPageStateKey('/17/centers/5')).toBe('/17/centers/5');
  });

  it('recalls what a page remembered, held by reference, until it is forgotten', () => {
    const state = { scroll: 120 };
    pageState.remember(CENTERS, state);
    expect(pageState.recall(CENTERS)).toBe(state);
    expect(pageState.recall(CENTER)).toBeUndefined();
    pageState.forget(CENTERS);
    expect(pageState.recall(CENTERS)).toBeUndefined();
  });

  it('keeps the 20 most recently used pages, a recall counting as a use', () => {
    const keys = Array.from({ length: 21 }, (_, i) => tmPageStateKey(`/17/centers/${i + 1}`));
    keys.slice(0, 20).forEach((key) => pageState.remember(key, key));
    expect(pageState.recall(keys[0]!)).toBe(keys[0]);
    pageState.remember(keys[20]!, keys[20]);
    expect(pageState.recall(keys[0]!)).toBe(keys[0]);
    expect(pageState.recall(keys[1]!)).toBeUndefined();
    expect(pageState.ɵkeys()).toHaveLength(20);
  });

  it("marks the pages of a written entity stale, a hatch's key by its host's", () => {
    rememberAll();
    pageState.markStale({ tenantId: 17, entity: 'gl.Center' });
    expect(stale()).toEqual([CENTERS, CENTER, CENTER_PANEL]);
    // The rest of the state still restores.
    expect(pageState.recall(CENTER)).toEqual({ path: CENTER });
  });

  it("marks every page of the tenant stale without an entity, and no other tenant's", () => {
    rememberAll();
    pageState.markStale({ tenantId: 17 });
    expect(stale()).toEqual([CENTERS, CENTER, CENTER_PANEL, CUSTOMERS]);
  });

  it('clears the stale mark on a remember, unless the host keeps it', () => {
    rememberAll();
    pageState.markStale({ tenantId: 17 });
    pageState.remember(CENTERS, { path: CENTERS, scroll: 40 });
    pageState.remember(CENTER, { path: CENTER, scroll: 40 }, { keepStale: true });
    pageState.remember(CUSTOMERS, { path: CUSTOMERS }, { keepStale: false });
    expect(stale()).toEqual([CENTER, CENTER_PANEL]);
    // A fresh entry stays fresh when kept.
    pageState.remember(OTHER_TENANT, { path: OTHER_TENANT }, { keepStale: true });
    expect(pageState.isStale(OTHER_TENANT)).toBe(false);
  });

  it('marks every page of the tenant stale when its permissions tag moves', async () => {
    rememberAll();
    await tmSettle();
    const me = TM_T17_DOCS.me;
    await sync({ me: { ...me, access: { ...me.access, tag: 'A2' } } });
    expect(stale()).toEqual([CENTERS, CENTER, CENTER_PANEL, CUSTOMERS]);
  });

  it("forgets the tenant's pages when its languages change, and keeps them for any other settings move", async () => {
    rememberAll();
    await tmSettle();
    const settings = TM_T17_DOCS.settings;
    await sync({ settings: { ...settings, timeZone: 'Africa/Addis_Ababa' } });
    expect(pageState.ɵkeys()).toHaveLength(5);
    await sync({
      settings: {
        ...settings,
        timeZone: 'Africa/Addis_Ababa',
        languages: [...settings.languages, { code: 'ar', nativeName: 'العربية', symbol: 'ع', isRightToLeft: true }],
      },
    });
    expect(pageState.ɵkeys()).toEqual([OTHER_TENANT]);
    // Another tenant's pages stay.
    expect(pageState.recall(OTHER_TENANT)).toEqual({ path: OTHER_TENANT });
  });

  it('marks nothing for a tenant whose entries were remembered after the move', async () => {
    const me = TM_T17_DOCS.me;
    await sync({ me: { ...me, access: { ...me.access, tag: 'A2' } } });
    pageState.remember(CENTERS, { path: CENTERS });
    await tmSettle();
    expect(pageState.isStale(CENTERS)).toBe(false);
  });
});
