// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { provideLocationMocks } from '@angular/common/testing';
import type { ApplicationRef, Type } from '@angular/core';
import { Router } from '@angular/router';
import { provideTellmaStateTesting, provideTellmaTesting, TmFakeApi } from '@tellma/core-testing';

import type { TmBootResult, TmBootState } from '../boot/tm-boot-types';
import { tmLoadTestPage, tmRouteTestConfig, tmStoreLaunchDocs, TmTestPage } from '../routes/tm-app.testing.util';
import { TmClientStore } from '../state/store/tm-client-store';
import { bootstrapTellma } from './bootstrap-tellma';
import { provideTellma } from './provide-tellma';

/** The window, with the boot script's result. */
const host = globalThis as { __tmBoot?: TmBootResult };

/** A boot script's result. */
function bootResult(state: TmBootState): TmBootResult {
  return { state, profile: null, record: null, cold: false, auto: null, mark: null, showPage: vi.fn() };
}

/** Runs the pending tasks: the answers and loads a navigation started. */
async function settle(app?: ApplicationRef): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await new Promise((resolve) => setTimeout(resolve));
    app?.tick();
  }
}

describe('bootstrapTellma', () => {
  let element: HTMLElement;

  beforeEach(() => {
    element = document.body.appendChild(document.createElement('tm-app'));
  });

  afterEach(() => {
    delete host.__tmBoot;
    element.remove();
    vi.restoreAllMocks();
  });

  it.each<TmBootState>(['redirect', 'page'])('starts nothing while the boot script decided %s', async (state) => {
    host.__tmBoot = bootResult(state);
    const started = vi.fn();
    void bootstrapTellma({ providers: [] }).then(started, started);
    await settle();
    expect(started).not.toHaveBeenCalled();
    expect(element.childElementCount).toBe(0);
  });

  it('starts nothing, and says so in development, when no boot script ran', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const started = vi.fn();
    void bootstrapTellma({ providers: [] }).then(started, started);
    await settle();
    expect(started).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(expect.stringMatching(/no boot script ran/));
  });

  it('boots a fake-API app that routes and renders a skeleton, then a warm page, with no answer from the server', async () => {
    host.__tmBoot = bootResult('run');
    const app = await bootstrapTellma({
      providers: [provideTellma(tmRouteTestConfig()), provideTellmaTesting(), provideTellmaStateTesting(), provideLocationMocks()],
    });
    try {
      // No call to the server is ever answered.
      const api = app.injector.get(TmFakeApi);
      for (const route of ['client/sync', '/bff/user', '/api/distribution-info']) {
        api.respond(route, () => new Promise(() => undefined));
      }
      await tmStoreLaunchDocs(app.injector.get(TmClientStore));

      // The navigation's own frame draws the skeleton; the page follows once its chunk loads.
      let loaded!: (page: Type<unknown>) => void;
      tmLoadTestPage.mockImplementationOnce(() => new Promise((resolve) => (loaded = resolve)));
      await app.injector.get(Router).navigateByUrl('/17/credit-review');
      app.tick();
      expect(element.querySelector('tm-tenant-shell tm-route-host tm-page-skeleton')).not.toBeNull();
      loaded(TmTestPage);
      await settle(app);
      expect(element.querySelector('tm-route-host tm-test-page')).not.toBeNull();
      expect(element.querySelector('tm-page-skeleton')).toBeNull();
    } finally {
      app.destroy();
    }
  });
});
