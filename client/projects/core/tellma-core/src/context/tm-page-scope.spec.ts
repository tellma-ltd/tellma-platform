// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, Injector, inject } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTmPageScope } from './tm-page-scope';

describe('provideTmPageScope', () => {
  it('gives a component a page scope of its own without changing what it can inject', () => {
    @Component({ selector: 'tm-scoped-page', template: '', providers: [provideTmPageScope()] })
    class ScopedPage {
      readonly injector = inject(Injector);
    }

    const fixture = TestBed.createComponent(ScopedPage);
    expect(fixture.componentInstance.injector).toBeTruthy();
    expect(provideTmPageScope()).toEqual([]);
  });
});
