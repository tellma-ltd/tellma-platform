// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TmFakeSession } from '@tellma/core-testing';
import { TmApiError } from '@tellma/core';

import { TmFakeApiUpdates, tmFakeUpdatesProviders } from '../../src/api/tm-api.testing.util';
import { tmAppRig, tmNavigate, tmSettle, tmSettleUntil, type TmAppRig } from '../../src/routes/tm-app.testing.util';
import { TmErrorPresenter } from './tm-error-presenter';
import { TmErrorState } from './tm-error-state';
import { TM_RELOAD_HOST } from './tm-error-updates';

// The inline error state in a whole test app on `/17/centers`, rendering
// the presenter's views.

/** The pieces a spec drives. */
interface TmStateRig extends TmAppRig {
  readonly updates: TmFakeApiUpdates;
  readonly presenter: TmErrorPresenter;
}

/** Starts the test app on `/17/centers`. */
async function setUp(): Promise<TmStateRig> {
  const updates = new TmFakeApiUpdates();
  const rig = await tmAppRig({ providers: [...tmFakeUpdatesProviders(updates), { provide: TM_RELOAD_HOST, useValue: updates }] });
  await tmNavigate('/17/centers');
  // The centers' search page lands with its lazily loaded chunk.
  await tmSettleUntil(() => document.querySelector('tm-search-host') !== null, 'the search page lands');
  return { ...rig, updates, presenter: TestBed.inject(TmErrorPresenter) };
}

/** Renders the state of an error. */
async function render(rig: TmStateRig, error: TmApiError): Promise<ComponentFixture<TmErrorState>> {
  const fixture = TestBed.createComponent(TmErrorState);
  fixture.componentRef.setInput('view', rig.presenter.describe(error, { kind: 'read' }));
  await tmSettle();
  return fixture;
}

/** The state's buttons' labels. */
function buttons(fixture: ComponentFixture<TmErrorState>): string[] {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
    .map((button) => button.textContent?.trim() ?? '')
    .filter((label) => label !== '');
}

/** The state's button of a label. */
function button(fixture: ComponentFixture<TmErrorState>, label: string): HTMLButtonElement {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)!;
}

describe('TmErrorState', () => {
  it("shows the words, the reference and the refused permission's raw pair in a left-to-right isolate", async () => {
    const rig = await setUp();
    const fixture = await render(
      rig,
      new TmApiError({
        kind: 'forbidden',
        status: 403,
        code: 'forbidden',
        traceId: '0af7651916cd43dd8448eb211c80319c',
        problem: {
          type: 'about:blank',
          title: 'Forbidden',
          status: 403,
          code: 'forbidden',
          instance: '/api/17/centers/query',
          arguments: { resource: 'gl.Center', action: 'Read', reason: 'no-grant' },
        },
      }),
    );
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain("You don't have permission for this");
    const pair = element.querySelector('bdi');
    expect(pair?.textContent).toBe('gl.Center · Read');
    expect(pair?.getAttribute('dir')).toBe('ltr');
    expect(element.querySelector('tm-reference')?.textContent).toContain('0af7651916cd43dd8448eb211c80319c');
    expect(buttons(fixture).filter((label) => label !== 'Copy reference')).toEqual([]);
  });

  it('hands Try again to its host', async () => {
    const rig = await setUp();
    const fixture = await render(rig, new TmApiError({ kind: 'unavailable', status: 503 }));
    const retried = vi.fn();
    fixture.componentInstance.retry.subscribe(retried);
    button(fixture, 'Try again').click();
    expect(retried).toHaveBeenCalledOnce();
  });

  it('gets the update on Reload, keeps the state on a failed download, and offers Reload anyway from the second failure', async () => {
    const rig = await setUp();
    rig.updates.answer = 'unreachable';
    const fixture = await render(rig, new TmApiError({ kind: 'client-outdated', status: 412, code: 'client-outdated' }));
    const element = fixture.nativeElement as HTMLElement;
    button(fixture, 'Reload').click();
    await tmSettle();
    expect(element.textContent).toContain("Couldn't download the update. Check your connection and try again.");
    expect(buttons(fixture)).not.toContain('Reload anyway');
    button(fixture, 'Reload').click();
    await tmSettle();
    button(fixture, 'Reload anyway').click();
    expect([rig.updates.checks, rig.updates.reloads]).toEqual([2, 1]);
  });

  it('keeps Reload busy, named "Getting the update…", while the download runs', async () => {
    const rig = await setUp();
    rig.updates.answer = null;
    const fixture = await render(rig, new TmApiError({ kind: 'client-outdated', status: 412, code: 'client-outdated' }));
    button(fixture, 'Reload').click();
    await tmSettle();
    const reload = button(fixture, 'Reload');
    expect(reload.getAttribute('aria-label')).toBe('Getting the update…');
    expect(reload.getAttribute('aria-busy')).toBe('true');
  });

  it("switches tenant from a suspended tenant's page, and signs in again from a step-up", async () => {
    const rig = await setUp();
    const suspended = TestBed.createComponent(TmErrorState);
    const presenter = rig.presenter;
    suspended.componentRef.setInput('view', presenter.describe(new TmApiError({ kind: 'tenant-unavailable', status: 403 }), { kind: 'read' }));
    await tmSettle();
    expect((suspended.nativeElement as HTMLElement).textContent).toContain('Acme is suspended');
    button(suspended, 'Switch tenant').click();
    await vi.waitFor(() => expect(rig.router.url).toBe('/tenants'));

    const challenge = { acr: 'mfa', maxAge: 300 };
    const stepUp = await render(rig, new TmApiError({ kind: 'step-up', status: 401, code: 'step-up-required', challenge }));
    button(stepUp, 'Continue').click();
    await vi.waitFor(() => expect(TestBed.inject(TmFakeSession).signIns()).toEqual([{ stepUp: challenge }]));
  });
});
