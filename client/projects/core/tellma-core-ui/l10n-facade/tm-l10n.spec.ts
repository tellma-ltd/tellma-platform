// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, computed, inject, type Signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { TmL10n } from './tm-l10n';

@Component({ template: `` })
class Host {
  private readonly l10n = inject(TmL10n);
  /** A consumer-style computed — must re-evaluate on locale switch. */
  readonly display: Signal<string> = computed(() =>
    this.l10n.formatNumber(1234.5, { minDecimals: 2 }),
  );
}

describe('TmL10n', () => {
  it('formats with the active locale and re-renders reactive callers on switch', async () => {
    TestBed.configureTestingModule({
      providers: [provideTellmaUi({ availableLangs: ['en', 'de'] })],
    });
    const fixture = TestBed.createComponent(Host);
    const display = fixture.componentInstance.display;
    expect(display()).toBe('1,234.50');

    TestBed.inject(TranslocoService).setActiveLang('de');
    await fixture.whenStable();
    expect(display()).toBe('1.234,50');
  });

  it('formats with LOCALE_ID when Transloco is absent', () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(Host);
    // The TestBed default LOCALE_ID is en-US.
    expect(fixture.componentInstance.display()).toBe('1,234.50');
  });
});
