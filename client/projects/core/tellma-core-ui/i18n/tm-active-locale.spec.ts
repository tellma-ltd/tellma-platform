// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, inject, LOCALE_ID, type Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { TM_ACTIVE_LOCALE } from './tm-active-locale';

@Component({ template: `` })
class Host {
  readonly locale: Signal<string> = inject(TM_ACTIVE_LOCALE);
}

describe('TM_ACTIVE_LOCALE', () => {
  it('falls back to the static LOCALE_ID when Transloco is absent', () => {
    TestBed.configureTestingModule({ providers: [{ provide: LOCALE_ID, useValue: 'de-AT' }] });
    const fixture = TestBed.createComponent(Host);
    expect(fixture.componentInstance.locale()).toBe('de-AT');
  });

  it('tracks the active Transloco language, live', async () => {
    TestBed.configureTestingModule({
      providers: [provideTellmaUi({ availableLangs: ['en', 'ar'] })],
    });
    const fixture = TestBed.createComponent(Host);
    const locale = fixture.componentInstance.locale;
    expect(locale()).toBe('en');

    TestBed.inject(TranslocoService).setActiveLang('ar');
    await fixture.whenStable();
    expect(locale()).toBe('ar');
  });
});
