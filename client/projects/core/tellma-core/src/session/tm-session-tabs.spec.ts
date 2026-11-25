// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, EnvironmentInjector, signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TmConfirm, type TmConfirmYesNoOptions } from '@tellma/core-ui/confirm';

import { TmApi } from '../api/tm-api';
import { TM_NOW } from '../app/tm-now';
import { tmAppRig, tmNavigate, tmSettle, tmSettleUntil, TM_TEST_SUB } from '../routes/tm-app.testing.util';
import type { TmBusMessage } from '../state/bus/tm-bus-messages';
import { TmTabBus } from '../state/bus/tm-tab-bus';
import { TmRecordingTabBus } from '../state/bus/tm-tab-bus.testing.util';
import { TM_DRAFTS_STORAGE, tmDraftPrefix, tmStorageKeys, TM_DRAFT_KEY_PREFIX } from '../state/memory/tm-draft-keys';
import { TM_STATE_PARTS } from '../state/tm-state-dump';
import { TmFakeStorage } from '../state/tm-storage.testing.util';
import { tmProfileCookies } from './tm-profile.testing.util';
import { TmSessionFlows } from './tm-session-flows';
import { TM_SESSION_HOST } from './tm-session-host';
import { TmSessionPaths } from './tm-session-paths';
import { TmFakeSessionHost } from './tm-session.testing.util';

// Several tabs of one user in one test app: each tab is an environment
// injector of its own, with its own session paths and flows, tab bus,
// browser, session storage and API, and the tabs' buses deliver each post
// to every other tab, a task later, as BroadcastChannel does.

/** The tab buses of the user's tabs, which deliver each tab's posts to the others. */
class TmBusFabric {
  /** The tabs' buses. */
  readonly buses: TmFabricBus[] = [];

  /** Delivers a post to every tab but its sender's, a task later. */
  deliver(from: TmFabricBus, message: TmBusMessage): void {
    for (const bus of this.buses) {
      if (bus !== from) {
        setTimeout(() => bus.receive(message));
      }
    }
  }
}

/** One tab's bus on the fabric. */
class TmFabricBus extends TmRecordingTabBus {
  readonly #fabric: TmBusFabric;

  /**
   * Joins the fabric.
   *
   * @param fabric - The fabric.
   */
  constructor(fabric: TmBusFabric) {
    super();
    this.#fabric = fabric;
    fabric.buses.push(this);
  }

  /** Records a post and delivers it to the other tabs. */
  override post(message: TmBusMessage): void {
    super.post(message);
    this.#fabric.deliver(this, message);
  }
}

/** One tab: its browser, its session storage, its writes and its session's paths. */
interface TmTab {
  readonly host: TmFakeSessionHost;
  readonly storage: TmFakeStorage;
  readonly writesInFlight: WritableSignal<number>;
  readonly writes: string[];
  readonly paths: TmSessionPaths;
  readonly injector: EnvironmentInjector;
}

/** The confirm, recorded, answering yes. */
class TmRecordingConfirm {
  /** What each confirm asked. */
  readonly asked: TmConfirmYesNoOptions[] = [];

  /** Records the question and answers yes. */
  confirm(options: TmConfirmYesNoOptions): Promise<boolean> {
    this.asked.push(options);
    return Promise.resolve(true);
  }
}

/**
 * Opens a tab of the test user on the fabric.
 *
 * @param fabric - The fabric.
 * @returns The tab.
 */
function openTab(fabric: TmBusFabric): TmTab {
  const host = new TmFakeSessionHost();
  host.cookieJar = tmProfileCookies(TM_TEST_SUB, 'en');
  const storage = new TmFakeStorage();
  const writesInFlight = signal(0);
  const writes: string[] = [];
  const api = {
    writesInFlight,
    write: (route: string) => {
      writes.push(route);
      return Promise.resolve(null);
    },
  };
  const injector = createEnvironmentInjector(
    [
      TmSessionPaths,
      TmSessionFlows,
      { provide: TmTabBus, useValue: new TmFabricBus(fabric) },
      { provide: TM_SESSION_HOST, useValue: host },
      { provide: TM_DRAFTS_STORAGE, useValue: storage },
      { provide: TM_STATE_PARTS, useValue: {} },
      { provide: TmApi, useValue: api },
    ],
    TestBed.inject(EnvironmentInjector),
  );
  const paths = injector.get(TmSessionPaths);
  paths.ɵstart();
  return { host, storage, writesInFlight, writes, paths, injector };
}

describe('Sign-out across tabs', () => {
  it('waits for a busy tab, asks about a dirty tab, and takes every tab to the signed-out page', async () => {
    const confirm = new TmRecordingConfirm();
    await tmAppRig({
      providers: [
        { provide: TmConfirm, useValue: confirm },
        // Read through the global, as the flows' waits are.
        { provide: TM_NOW, useValue: () => Date.now() },
      ],
    });
    await tmNavigate('/17/centers');
    await tmSettle();
    const fabric = new TmBusFabric();
    const [acting, busy, dirty] = [openTab(fabric), openTab(fabric), openTab(fabric)];
    busy.writesInFlight.set(1);
    dirty.storage.setItem(`${tmDraftPrefix(TM_TEST_SUB)}17:gl.Center:5`, '{"v":1}');

    void acting.paths.ɵsignOut();
    // The busy tab holds the sign-out: no question and no sign-out call while its save runs.
    await new Promise((resolve) => setTimeout(resolve, 1_500));
    expect([confirm.asked, acting.writes]).toEqual([[], []]);
    busy.writesInFlight.set(0);

    // The dirty tab's draft reaches the one question, at the waiting sign-out's next ask.
    await tmSettleUntil(() => confirm.asked.length > 0, 'the sign-out asked about the draft');
    expect(confirm.asked).toHaveLength(1);
    expect(confirm.asked[0]).toMatchObject({ title: 'Sign out and discard unsaved changes?', message: 'You have unsaved changes in 1 tab.' });

    // Every tab ends at the signed-out page, its session storage cleared.
    await tmSettleUntil(() => acting.host.assigned.length > 0, 'the acting tab left for the signed-out page');
    expect(acting.host.assigned).toEqual(['/signed-out']);
    expect(acting.writes).toEqual(['/bff/logout']);
    for (const tab of [busy, dirty]) {
      // Each tab's own flows, a chunk the tab loads on the announcement, take it there.
      await tmSettleUntil(() => tab.host.replaced.length > 0, 'the other tab left for the signed-out page');
      expect(tab.host.replaced).toEqual(['/signed-out']);
      expect(tab.paths.state()).toBe('signing-out');
    }
    expect([acting, busy, dirty].map((tab) => tmStorageKeys(tab.storage, TM_DRAFT_KEY_PREFIX).length)).toEqual([0, 0, 0]);
    [acting, busy, dirty].forEach((tab) => tab.injector.destroy());
  });

  it('leaves every tab signed in, with its drafts, when the user cancels', async () => {
    const confirm = new TmRecordingConfirm();
    confirm.confirm = (options) => (confirm.asked.push(options), Promise.resolve(false));
    await tmAppRig({ providers: [{ provide: TmConfirm, useValue: confirm }, { provide: TM_NOW, useValue: () => Date.now() }] });
    await tmNavigate('/17/centers');
    await tmSettle();
    const fabric = new TmBusFabric();
    const [acting, dirty] = [openTab(fabric), openTab(fabric)];
    dirty.storage.setItem(`${tmDraftPrefix(TM_TEST_SUB)}17:gl.Center:5`, '{"v":1}');
    await acting.paths.ɵsignOut();
    expect(confirm.asked).toHaveLength(1);
    // Nothing was announced, so no tab turned signing-out, and the dirty tab keeps its draft.
    expect([acting.paths.state(), dirty.paths.state()]).toEqual(['active', 'active']);
    expect(tmStorageKeys(dirty.storage, TM_DRAFT_KEY_PREFIX)).toHaveLength(1);
    expect(acting.writes).toEqual([]);
    [acting, dirty].forEach((tab) => tab.injector.destroy());
  });
});
