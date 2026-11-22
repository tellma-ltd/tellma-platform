// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TM_NOW, type TmDecimal, TmSession, TmTelemetry, ɵTM_API_HOST } from '@tellma/core';

import { TmFakeApiHost } from '../../src/api/tm-api.testing.util';
import { tmProfileCookies } from '../../src/session/tm-profile.testing.util';
import { TmFakeSessionHost, TmRecordingTelemetry, tmSessionTestingProviders } from '../../src/session/tm-session.testing.util';
import { TmFakeStorage } from '../../src/state/tm-storage.testing.util';
import { TmDrafts } from './tm-drafts';

// Drafts over the real session and a faked browser: its session storage,
// which a spec may cap, and its page events. A reload is a new app over the
// same session storage; a new tab is a new app over a new one.

const NOW = 1_700_000_000_000;
const FARAH = '0b4f';
const SARA = '7c21';

/** A session storage that refuses, as a full one does, any write past its capacity in characters. */
class TmCappedStorage extends TmFakeStorage {
  /** The characters it holds at most, keys and values together. */
  capacity = Infinity;

  /** Writes one item, unless it runs over the capacity. */
  override setItem(key: string, value: string): void {
    let used = key.length + value.length;
    for (let i = 0; i < this.length; i++) {
      const other = this.key(i)!;
      used += other === key ? 0 : other.length + this.getItem(other)!.length;
    }
    if (used > this.capacity) {
      throw new DOMException('The quota has been exceeded.', 'QuotaExceededError');
    }
    super.setItem(key, value);
  }
}

/** A browser tab: its session's browser, with a capped session storage, and its page. */
interface TmTab {
  readonly session: TmFakeSessionHost & { readonly sessionStorage: TmCappedStorage };
  readonly page: TmFakeApiHost;
}

/** A new tab, signed in as a user. */
function newTab(sub = FARAH): TmTab {
  const session = new TmFakeSessionHost() as TmTab['session'];
  Object.defineProperty(session, 'sessionStorage', { value: new TmCappedStorage() });
  session.cookieJar = tmProfileCookies(sub);
  return { session, page: new TmFakeApiHost() };
}

/** What a spec drives and inspects. */
interface TmRig {
  readonly drafts: TmDrafts;
  readonly telemetry: TmRecordingTelemetry;
  /** The tab's session storage. */
  readonly storage: TmCappedStorage;
  /** Moves the clock. */
  tick(ms: number): void;
}

/** Starts the app in a tab: a first launch, or a reload when the tab already ran one. */
function launch(tab: TmTab): TmRig {
  TestBed.resetTestingModule();
  let clock = NOW;
  TestBed.configureTestingModule({
    providers: [
      tmSessionTestingProviders(tab.session, null, NOW),
      { provide: ɵTM_API_HOST, useValue: tab.page },
      { provide: TM_NOW, useValue: () => clock },
    ],
  });
  return {
    drafts: TestBed.inject(TmDrafts),
    telemetry: TestBed.inject(TmTelemetry) as TmRecordingTelemetry,
    storage: tab.session.sessionStorage,
    tick: (ms) => (clock += ms),
  };
}

describe('TmDrafts', () => {
  afterEach(() => vi.useRealTimers());

  it("keys a record's draft by the signed-in user, the tenant, the entity and the id", () => {
    const { drafts } = launch(newTab());
    expect(drafts.key(17, 'gl.Center', 5)).toBe(`tm.draft:${FARAH}:17:gl.Center:5`);
    expect(drafts.key(17, 'gl.Center', 'new')).toBe(`tm.draft:${FARAH}:17:gl.Center:new`);
  });

  it('writes a save 300 ms after the last one, and loads the pending copy meanwhile', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const { drafts, storage } = launch(newTab());
    const key = drafts.key(17, 'gl.Center', 5);
    drafts.save(key, { name: 'Jed' });
    vi.advanceTimersByTime(200);
    drafts.save(key, { name: 'Jeddah' });
    vi.advanceTimersByTime(299);
    expect(storage.getItem(key)).toBeNull();
    expect(drafts.load(key)).toEqual({ name: 'Jeddah' });
    vi.advanceTimersByTime(1);
    expect(storage.json(key)).toEqual({ v: 1, at: NOW, data: { name: 'Jeddah' } });
  });

  it('flushes at once, synchronously, when the page hides or goes away', () => {
    const tab = newTab();
    const { drafts, storage } = launch(tab);
    const center = drafts.key(17, 'gl.Center', 5);
    const agent = drafts.key(17, 'gl.Agent', 'new');
    drafts.save(center, { name: 'Jeddah' });
    tab.page.visibleNow = false;
    tab.page.fire('document', 'visibilitychange');
    expect(storage.json(center)).toMatchObject({ data: { name: 'Jeddah' } });
    drafts.save(agent, { name: 'Sara' });
    tab.page.fire('window', 'pagehide');
    expect(storage.json(agent)).toMatchObject({ data: { name: 'Sara' } });
  });

  it('round-trips every value exactly, decimals and longs being strings', () => {
    const { drafts } = launch(newTab());
    const key = drafts.key(17, 'acme.SalesInvoice', '9007199254740993' as TmDecimal);
    const value = { id: '9007199254740993', amount: '1234.5600', lines: [{ qty: '2', note: null }], ok: true };
    drafts.save(key, value);
    drafts.ɵflush();
    expect(drafts.load(key)).toEqual(value);
  });

  it("refuses another user's key, and counts only the signed-in user's drafts", () => {
    const tab = newTab();
    tab.session.sessionStorage.setItem(`tm.draft:${SARA}:17:gl.Center:5`, JSON.stringify({ v: 1, at: NOW, data: { name: 'Sara' } }));
    const { drafts, storage } = launch(tab);
    expect(drafts.load(`tm.draft:${SARA}:17:gl.Center:5`)).toBeUndefined();
    drafts.save(`tm.draft:${SARA}:17:gl.Center:6`, { name: 'x' });
    drafts.ɵflush();
    expect(storage.getItem(`tm.draft:${SARA}:17:gl.Center:6`)).toBeNull();
    expect(drafts.dirtyCount()).toBe(0);
    drafts.save(drafts.key(17, 'gl.Center', 5), { name: 'Farah' });
    expect(drafts.dirtyCount()).toBe(1);
  });

  it("clears one draft, or every draft of a user, pending ones included", () => {
    const tab = newTab();
    tab.session.sessionStorage.setItem(`tm.draft:${SARA}:17:gl.Center:5`, '{}');
    const { drafts, storage } = launch(tab);
    const one = drafts.key(17, 'gl.Center', 5);
    const two = drafts.key(17, 'gl.Center', 6);
    drafts.save(one, { name: 'a' });
    drafts.ɵflush();
    drafts.save(two, { name: 'b' });
    expect(drafts.dirtyCount()).toBe(2);
    drafts.clear(one);
    expect([drafts.load(one), storage.getItem(one), drafts.dirtyCount()]).toEqual([undefined, null, 1]);
    drafts.clearAll(FARAH);
    drafts.ɵflush();
    expect([drafts.load(two), storage.getItem(two), drafts.dirtyCount()]).toEqual([undefined, null, 0]);
    // Another user's drafts stay for their own clean-up.
    expect(storage.getItem(`tm.draft:${SARA}:17:gl.Center:5`)).toBe('{}');
  });

  it("gives up this tab's oldest other drafts to its memory when the storage is full, and retries once", () => {
    const { drafts, storage, telemetry, tick } = launch(newTab());
    const keys = [1, 2, 3].map((id) => drafts.key(17, 'gl.Center', id));
    for (const key of keys) {
      drafts.save(key, { name: 'x'.repeat(100) });
      drafts.ɵflush();
      tick(1_000);
    }
    storage.capacity = storage.getItem(keys[0]!)!.length * 3 + keys[0]!.length * 3 + 10;
    const fourth = drafts.key(17, 'gl.Center', 4);
    drafts.save(fourth, { name: 'y'.repeat(100) });
    drafts.ɵflush();
    expect(storage.getItem(keys[0]!)).toBeNull();
    expect([keys[1], keys[2], fourth].map((key) => storage.getItem(key!) !== null)).toEqual([true, true, true]);
    // The given-up draft still loads in this tab.
    expect(drafts.load(keys[0]!)).toEqual({ name: 'x'.repeat(100) });
    expect(drafts.dirtyCount()).toBe(4);
    expect(telemetry.events).toEqual([]);
  });

  it('keeps a draft in memory alone when the retry fails too', () => {
    const { drafts, storage, telemetry } = launch(newTab());
    storage.capacity = 50;
    const key = drafts.key(17, 'gl.Center', 5);
    drafts.save(key, { name: 'x'.repeat(100) });
    drafts.ɵflush();
    expect(storage.getItem(key)).toBeNull();
    expect(drafts.load(key)).toEqual({ name: 'x'.repeat(100) });
    expect(telemetry.events).toEqual([{ name: 'tm.drafts.quota-exceeded', props: undefined }]);
  });

  it('survives a reload of the tab, but never reaches a new tab', () => {
    const tab = newTab();
    const first = launch(tab);
    const key = first.drafts.key(17, 'gl.Center', 5);
    first.drafts.save(key, { name: 'Jeddah' });
    tab.page.fire('window', 'pagehide');

    const reloaded = launch(tab);
    expect(reloaded.drafts.load(key)).toEqual({ name: 'Jeddah' });
    expect(reloaded.drafts.dirtyCount()).toBe(1);

    const other = launch(newTab());
    expect(other.drafts.load(key)).toBeUndefined();
    expect(other.drafts.dirtyCount()).toBe(0);
  });

  it('leaves only drafts and the sign-in mark in session storage', () => {
    const tab = newTab();
    const { drafts, storage } = launch(tab);
    drafts.save(drafts.key(17, 'gl.Center', 5), { name: 'Jeddah' });
    drafts.save(drafts.key(18, 'acme.Customer', 'new'), { name: 'Acme' });
    tab.page.fire('window', 'pagehide');
    void TestBed.inject(TmSession).signIn({ returnUrl: '/17/centers/5' });
    const keys = Array.from({ length: storage.length }, (_, i) => storage.key(i)!);
    expect(keys.length).toBe(3);
    expect(keys.every((key) => key === 'tm.login' || key.startsWith('tm.draft:'))).toBe(true);
  });
});
