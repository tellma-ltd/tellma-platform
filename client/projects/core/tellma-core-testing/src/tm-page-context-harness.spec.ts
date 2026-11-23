// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, DestroyRef, EnvironmentInjector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { type TmActionResult, TmPageActions, type TmPageContext, TmPageContexts } from '@tellma/core';

import { provideTellmaStateTesting } from './provide-tellma-state-testing';
import { provideTellmaTesting } from './provide-tellma-testing';
import { TmPageContextHarness } from './tm-page-context-harness';

/** A context with no members beyond the required ones. */
function context(routeTemplate: string, overlay = false): TmPageContext {
  return {
    kind: 'custom',
    routeTemplate,
    overlay,
    title: signal(routeTemplate),
    status: signal('ready'),
    state: signal({ query: { q: 'main' } }),
    errors: signal([]),
    actions: signal([]),
  };
}

/** A lifetime the test ends. */
function lifetime(): EnvironmentInjector {
  return createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
}

describe('TmPageContextHarness', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
  });

  it('reads no context before one registers', () => {
    const harness = TestBed.inject(TmPageContextHarness);
    expect([harness.current(), harness.overlays(), harness.top(), harness.actions()]).toEqual([null, [], null, []]);
  });

  it('snapshots the routed and dialog contexts, with page data unless told otherwise', () => {
    const contexts = TestBed.inject(TmPageContexts);
    contexts.register(context('/:t/a'), lifetime().get(DestroyRef));
    const dialog = lifetime();
    contexts.register(context('panel:bell', true), dialog.get(DestroyRef));
    const harness = TestBed.inject(TmPageContextHarness);

    expect(harness.current()).toMatchObject({ routeTemplate: '/:t/a', overlay: false, state: { query: { q: 'main' } } });
    expect(harness.current(false)!.state).toEqual({ query: { q: '…' } });
    expect(harness.overlays().map((snapshot) => snapshot.routeTemplate)).toEqual(['panel:bell']);
    expect(harness.top()!.routeTemplate).toBe('panel:bell');
    dialog.destroy();
    expect(harness.top()!.routeTemplate).toBe('/:t/a');
  });

  it("lists the top context's actions and invokes them through the dispatcher's rules", async () => {
    TestBed.inject(TmPageContexts).register(context('/:t/a'), lifetime().get(DestroyRef));
    const invoke = vi.fn(() => Promise.resolve<TmActionResult>({ outcome: 'done' }));
    TestBed.inject(TmPageActions).register(
      { id: 'acme.a.suggest', labelKey: 'Acme_Suggest', enabled: signal(false), invoke },
      lifetime().get(DestroyRef),
    );
    const harness = TestBed.inject(TmPageContextHarness);

    expect(harness.actions().map((action) => action.id)).toEqual(['acme.a.suggest']);
    expect(harness.action('acme.a.missing')).toBeUndefined();
    expect(harness.action('acme.a.suggest')!.enabled()).toBe(false);
    expect(await harness.invoke('acme.a.suggest')).toEqual({ outcome: 'refused', reason: 'disabled' });
    expect(invoke).not.toHaveBeenCalled();
  });

  it('defaults to a user invocation with no arguments, and reports the running ids', async () => {
    TestBed.inject(TmPageContexts).register(context('/:t/a'), lifetime().get(DestroyRef));
    let finish!: (result: TmActionResult) => void;
    const invoke = vi.fn(() => new Promise<TmActionResult>((resolve) => (finish = resolve)));
    TestBed.inject(TmPageActions).register({ id: 'acme.a.suggest', labelKey: 'Acme_Suggest', invoke }, lifetime().get(DestroyRef));
    const harness = TestBed.inject(TmPageContextHarness);

    const pending = harness.invoke('acme.a.suggest');
    expect(harness.running()).toEqual(new Set(['acme.a.suggest']));
    expect(invoke).toHaveBeenCalledWith({}, 'user');
    finish({ outcome: 'done' });
    expect(await pending).toEqual({ outcome: 'done' });
    expect(harness.running()).toEqual(new Set());

    void harness.invoke('acme.a.suggest', { limit: 3 }, 'assistant');
    expect(invoke).toHaveBeenLastCalledWith({ limit: 3 }, 'assistant');
    finish({ outcome: 'done' });
  });
});
