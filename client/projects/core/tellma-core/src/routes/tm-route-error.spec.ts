// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed, type ComponentFixture } from '@angular/core/testing';

import { TM_ERRORS_LOAD, type TmErrorsModule } from '../api/tm-api-seams';
import { TmFakeApiUpdates, tmFakeUpdatesProviders } from '../api/tm-api.testing.util';
import { TM_SESSION_HOST } from '../session/tm-session-host';
import { TmFakeSessionHost } from '../session/tm-session.testing.util';
import { tmAppRig, tmSettle } from './tm-app.testing.util';
import { TM_ROUTE_ERROR_VIEWS, TmRouteError } from './tm-route-error';

// The route's client-outdated view in a whole test app: Reload's download
// flow is the error views' inline state, from their own entry point, with
// the update service and the page faked.

/** The pieces a spec drives. */
interface TmRouteErrorRig {
  readonly updates: TmFakeApiUpdates;
  readonly page: TmFakeSessionHost;
  readonly fixture: ComponentFixture<TmRouteError>;
}

/**
 * Starts the test app and renders the client-outdated view.
 *
 * @param load - The error views' loader; by default, the real one.
 */
async function setUp(load?: () => Promise<TmErrorsModule>): Promise<TmRouteErrorRig> {
  const updates = new TmFakeApiUpdates();
  const page = new TmFakeSessionHost();
  const { ɵTM_RELOAD_HOST } = await import('@tellma/core/errors');
  await tmAppRig({
    providers: [
      ...tmFakeUpdatesProviders(updates),
      { provide: ɵTM_RELOAD_HOST, useValue: updates },
      { provide: TM_SESSION_HOST, useValue: page },
      ...(load === undefined ? [] : [{ provide: TM_ERRORS_LOAD, useValue: load }]),
    ],
  });
  const fixture = TestBed.createComponent(TmRouteError);
  fixture.componentRef.setInput('view', TM_ROUTE_ERROR_VIEWS['client-outdated']);
  await tmSettle();
  return { updates, page, fixture };
}

/** The button of a label, anywhere on the page. */
function button(label: string): HTMLButtonElement | undefined {
  return [...document.querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label);
}

describe('TmRouteError', () => {
  it('gets the update on Reload, busy while it downloads, says when it fails, and offers Reload anyway from the second failure', async () => {
    const { updates } = await setUp();
    expect(document.querySelector('tm-error-state')?.textContent).toContain('Acme was updated');
    updates.answer = null;
    button('Reload')!.click();
    await tmSettle();
    expect(button('Reload')!.getAttribute('aria-label')).toBe('Getting the update…');
    expect(button('Reload')!.getAttribute('aria-busy')).toBe('true');
    TestBed.resetTestingModule();

    const failing = await setUp();
    failing.updates.answer = 'unreachable';
    button('Reload')!.click();
    await tmSettle();
    expect(document.querySelector('tm-error-state')?.textContent).toContain("Couldn't download the update. Check your connection and try again.");
    expect(button('Reload anyway')).toBeUndefined();
    button('Reload')!.click();
    await tmSettle();
    button('Reload anyway')!.click();
    expect([failing.updates.checks, failing.updates.reloads]).toEqual([2, 1]);
  });

  it('hands a Reload clicked before the error views load to their state', async () => {
    let loaded!: () => void;
    const gate = new Promise<void>((resolve) => (loaded = resolve));
    const { updates } = await setUp(async () => {
      await gate;
      return (await import('@tellma/core/errors')) as unknown as TmErrorsModule;
    });
    updates.answer = 'ready';
    expect(document.querySelector('tm-error-state')).toBeNull();
    button('Reload')!.click();
    loaded();
    await vi.waitFor(() => expect([updates.checks, updates.reloads]).toEqual([1, 1]));
  });

  it('reloads as the browser would when the error views cannot load', async () => {
    const { page, updates } = await setUp(() => Promise.reject(new TypeError('Failed to fetch dynamically imported module')));
    expect(document.querySelector('tm-error-state')).toBeNull();
    button('Reload')!.click();
    await vi.waitFor(() => expect(page.reloads).toBe(1));
    expect(updates.checks).toBe(0);
  });
});
