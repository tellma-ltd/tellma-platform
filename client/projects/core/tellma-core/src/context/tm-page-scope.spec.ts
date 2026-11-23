// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideTellmaStateTesting, provideTellmaTesting } from '@tellma/core-testing';

import { TmPageActions } from './tm-page-actions';
import { TmPageContexts } from './tm-page-contexts';
import { tmTestContext } from './tm-page.testing.util';
import { provideTmPageScope } from './tm-page-scope';

describe('provideTmPageScope', () => {
  /** A routed page with a scope of its own, which registers its context in its constructor. */
  @Component({ selector: 'tm-scoped-page', template: '', providers: [provideTmPageScope()] })
  class ScopedPage {
    readonly contexts = inject(TmPageContexts);
    readonly actions = inject(TmPageActions);

    constructor() {
      this.contexts.register(tmTestContext('/:t/scoped'));
    }
  }

  it("gives a component its own contexts and actions, bound to its scope, which read the app's registry", () => {
    TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaStateTesting()] });
    const fixture = TestBed.createComponent(ScopedPage);
    const page = fixture.componentInstance;
    const root = TestBed.inject(TmPageContexts);
    expect(page.contexts).not.toBe(root);
    expect(page.actions).not.toBe(TestBed.inject(TmPageActions));
    expect(root.current()?.routeTemplate).toBe('/:t/scoped');

    // The scope ends with the component, and its context with it.
    fixture.destroy();
    expect(root.current()).toBeNull();
  });
});
