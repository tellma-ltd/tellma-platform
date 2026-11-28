// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTellmaStateTesting, provideTellmaTesting } from '@tellma/core-testing';

import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TmPageActions } from './tm-page-actions';
import { TmPageContexts } from './tm-page-contexts';
import { tmIds, tmLifetime, tmTestAction, tmTestContext, tmTestInput, TmTestScope } from './tm-page.testing.util';

// The registry over contexts and actions registered in page scopes (test
// components providing one) and outside any: which context is current, the
// dialogs above it, replacement, the merged action lists and lifetimes.

/** Configures the registry's dependencies. */
function setUp(dev = true): TmPageContexts {
  TestBed.configureTestingModule({
    providers: [provideTellmaTesting(), provideTellmaStateTesting(), { provide: TM_DEV_MODE, useValue: () => dev }],
  });
  return TestBed.inject(TmPageContexts);
}

/** A component with a page scope of its own. */
function scope(): { scope: TmTestScope; destroy: () => void } {
  const fixture = TestBed.createComponent(TmTestScope);
  return { scope: fixture.componentInstance, destroy: () => fixture.destroy() };
}

describe('TmPageContexts', () => {
  describe('current, overlays and top', () => {
    it('is empty until a context registers', () => {
      const contexts = setUp();
      expect([contexts.current(), contexts.overlays(), contexts.top()]).toEqual([null, [], null]);
    });

    it('publishes the newest routed context as current, the dialogs above it in registration order, and the topmost as top', () => {
      const contexts = setUp();
      const page = scope();
      page.scope.contexts.register(tmTestContext('/:t/centers'), page.scope.destroyRef);
      const bell = scope();
      bell.scope.contexts.register(tmTestContext('panel:bell', { overlay: true }), bell.scope.destroyRef);
      const confirm = scope();
      confirm.scope.contexts.register(tmTestContext('dialog:error', { overlay: true }), confirm.scope.destroyRef);

      expect(contexts.current()?.routeTemplate).toBe('/:t/centers');
      expect(contexts.overlays().map((context) => context.routeTemplate)).toEqual(['panel:bell', 'dialog:error']);
      expect(contexts.top()?.routeTemplate).toBe('dialog:error');

      // Dialogs never replace each other: closing the top one shows the one beneath.
      confirm.destroy();
      expect(contexts.top()?.routeTemplate).toBe('panel:bell');
      bell.destroy();
      expect(contexts.top()?.routeTemplate).toBe('/:t/centers');
    });

    it('publishes a context as registered, its members kept and its actions merged', () => {
      const contexts = setUp();
      const page = scope();
      const context = tmTestContext('/:t/centers', { kind: 'search', entity: 'gl.Center', helpKey: 'gl.center' });
      page.scope.contexts.register(context, page.scope.destroyRef);
      const published = contexts.current()!;
      expect(published).not.toBe(context);
      expect(published).toMatchObject({ kind: 'search', entity: 'gl.Center', helpKey: 'gl.center', routeTemplate: '/:t/centers' });
      expect([published.title, published.status, published.state]).toEqual([context.title, context.status, context.state]);
      // The same published object while the context stays current.
      expect(contexts.current()).toBe(published);
    });

    it('reads no signal of a context when it registers', () => {
      const contexts = setUp();
      const read = vi.fn(() => 'x');
      const context = { ...tmTestContext('/:t/a'), title: read } as never;
      contexts.register(context, tmLifetime().get(DestroyRef));
      expect(read).not.toHaveBeenCalled();
    });
  });

  describe('scope resolution', () => {
    it("adds a page scope's actions to its context, even those registered before it", () => {
      setUp();
      const page = scope();
      page.scope.actions.register(tmTestInput('acme.page.early'), page.scope.destroyRef);
      page.scope.contexts.register(tmTestContext('/:t/a'), page.scope.destroyRef);
      page.scope.actions.register(tmTestInput('acme.page.late'), page.scope.destroyRef);
      expect(tmIds(page.scope.contexts.current)).toEqual(['acme.page.early', 'acme.page.late']);
    });

    it("adds a dialog scope's actions to the dialog's context, never to the page beneath", () => {
      const contexts = setUp();
      const page = scope();
      page.scope.contexts.register(tmTestContext('/:t/a'), page.scope.destroyRef);
      page.scope.actions.register(tmTestInput('acme.page.save'), page.scope.destroyRef);
      const dialog = scope();
      dialog.scope.contexts.register(tmTestContext('panel:bell', { overlay: true }), dialog.scope.destroyRef);
      dialog.scope.actions.register(tmTestInput('attention.bell.read-all'), dialog.scope.destroyRef);

      expect(tmIds(contexts.current)).toEqual(['acme.page.save']);
      expect(tmIds(contexts.top)).toEqual(['attention.bell.read-all']);
    });

    it('adds an action registered outside any scope to the routed context, whichever it is', () => {
      const contexts = setUp();
      const root = TestBed.inject(TmPageActions);
      root.register(tmTestInput('acme.app.refresh'), tmLifetime().get(DestroyRef));
      expect(contexts.current()).toBeNull();

      const first = scope();
      first.scope.contexts.register(tmTestContext('/:t/a'), first.scope.destroyRef);
      const dialog = scope();
      dialog.scope.contexts.register(tmTestContext('panel:bell', { overlay: true }), dialog.scope.destroyRef);
      expect(tmIds(contexts.current)).toEqual(['acme.app.refresh']);
      expect(tmIds(contexts.top)).toEqual([]);

      // A newer routed page takes it over.
      const second = scope();
      second.scope.contexts.register(tmTestContext('/:t/b'), second.scope.destroyRef);
      expect(tmIds(contexts.current)).toEqual(['acme.app.refresh']);
      first.destroy();
      expect(contexts.current()?.routeTemplate).toBe('/:t/b');
    });

    it('registers a context outside any scope with no scope actions of its own', () => {
      const contexts = setUp();
      const page = scope();
      page.scope.actions.register(tmTestInput('acme.page.save'), page.scope.destroyRef);
      contexts.register(tmTestContext('/:t/loose', {}, [tmTestAction('acme.loose.own')]), tmLifetime().get(DestroyRef));
      expect(tmIds(contexts.current)).toEqual(['acme.loose.own']);
    });
  });

  describe('replacement', () => {
    it("replaces a scope's routed context with a second one, which keeps the first one's actions after its own", () => {
      const contexts = setUp();
      const page = scope();
      const host = tmTestContext('/:t/credit-review', {}, [tmTestAction('acme.host.refresh')]);
      page.scope.contexts.register(host, page.scope.destroyRef);
      page.scope.actions.register(tmTestInput('acme.scope.export'), page.scope.destroyRef);
      const own = tmTestContext('/:t/credit-review', { state: signal({ threshold: 5 }) }, [tmTestAction('acme.page.suggest')]);
      const lifetime = tmLifetime();
      page.scope.contexts.register(own, lifetime.get(DestroyRef));

      expect(contexts.current()!.state()).toEqual({ threshold: 5 });
      expect(tmIds(contexts.current)).toEqual(['acme.page.suggest', 'acme.host.refresh', 'acme.scope.export']);

      // The replaced context comes back when its replacement ends.
      lifetime.destroy();
      expect(contexts.current()!.state()).toEqual({});
      expect(tmIds(contexts.current)).toEqual(['acme.host.refresh', 'acme.scope.export']);
    });

    it("never carries another scope's actions, nor a dialog's", () => {
      const contexts = setUp();
      const first = scope();
      first.scope.contexts.register(tmTestContext('/:t/a', {}, [tmTestAction('acme.a.own')]), first.scope.destroyRef);
      const second = scope();
      second.scope.contexts.register(tmTestContext('/:t/b', {}, [tmTestAction('acme.b.own')]), second.scope.destroyRef);
      second.scope.contexts.register(
        tmTestContext('panel:x', { overlay: true }, [tmTestAction('acme.x.own')]),
        second.scope.destroyRef,
      );
      expect(tmIds(contexts.current)).toEqual(['acme.b.own']);
    });

    it("follows the publisher's own action list as it changes", () => {
      const contexts = setUp();
      const page = scope();
      const context = tmTestContext('/:t/a', {}, [tmTestAction('acme.a.view')]);
      page.scope.contexts.register(context, page.scope.destroyRef);
      context.actions.set([tmTestAction('acme.a.view'), tmTestAction('acme.a.edit')]);
      expect(tmIds(contexts.current)).toEqual(['acme.a.view', 'acme.a.edit']);
    });
  });

  describe('merged order', () => {
    it('orders toolbar actions first, then by order (default 100), then by registration', () => {
      const contexts = setUp();
      const page = scope();
      page.scope.contexts.register(
        tmTestContext('/:t/a', {}, [
          tmTestAction('acme.own.menu-early', { group: 'menu', order: 10 }),
          tmTestAction('acme.own.primary', { group: 'primary' }),
          tmTestAction('acme.own.menu'),
        ]),
        page.scope.destroyRef,
      );
      page.scope.actions.register(tmTestInput('acme.scope.menu'), page.scope.destroyRef);
      page.scope.actions.register(tmTestInput('acme.scope.primary-first', { group: 'primary', order: 1 }), page.scope.destroyRef);
      page.scope.actions.register(tmTestInput('acme.scope.primary'), page.scope.destroyRef);
      expect(tmIds(contexts.current)).toEqual([
        'acme.scope.primary-first',
        'acme.own.primary',
        'acme.own.menu-early',
        'acme.own.menu',
        'acme.scope.menu',
        'acme.scope.primary',
      ]);
    });
  });

  describe('lifetime', () => {
    it("removes a context at once when its DestroyRef ends, and an action when its DestroyRef does", () => {
      const contexts = setUp();
      const page = scope();
      const context = tmLifetime();
      page.scope.contexts.register(tmTestContext('/:t/a'), context.get(DestroyRef));
      const action = tmLifetime();
      page.scope.actions.register(tmTestInput('acme.a.save'), action.get(DestroyRef));
      expect(tmIds(contexts.current)).toEqual(['acme.a.save']);
      action.destroy();
      expect(tmIds(contexts.current)).toEqual([]);
      context.destroy();
      expect(contexts.current()).toBeNull();
    });

    it('removes everything registered in a scope when the scope ends, whatever its DestroyRef', () => {
      const contexts = setUp();
      const page = scope();
      const longer = tmLifetime().get(DestroyRef);
      page.scope.contexts.register(tmTestContext('/:t/a'), longer);
      page.scope.actions.register(tmTestInput('acme.a.save'), longer);
      page.destroy();
      expect(contexts.current()).toBeNull();

      // The action left with its scope: a new context in another scope does not get it.
      const next = scope();
      next.scope.contexts.register(tmTestContext('/:t/b'), next.scope.destroyRef);
      expect(tmIds(contexts.current)).toEqual([]);
    });

    it("defaults a registration's DestroyRef to the caller's injection context's", () => {
      const contexts = setUp();
      const lifetime = tmLifetime();
      lifetime.runInContext(() => contexts.register(tmTestContext('/:t/a')));
      expect(contexts.current()).not.toBeNull();
      lifetime.destroy();
      expect(contexts.current()).toBeNull();
    });
  });

  describe('development checks', () => {
    it('throws for a malformed help key in development, and registers it in production', () => {
      expect(() => setUp().register(tmTestContext('/:t/a', { helpKey: 'GL Center' }), tmLifetime().get(DestroyRef))).toThrow(
        'The help key "GL Center" must be lowercase kebab-case parts separated by dots, such as "gl.center".',
      );
      TestBed.resetTestingModule();
      const contexts = setUp(false);
      contexts.register(tmTestContext('/:t/a', { helpKey: 'GL Center' }), tmLifetime().get(DestroyRef));
      expect(contexts.current()?.helpKey).toBe('GL Center');
    });
  });
});
