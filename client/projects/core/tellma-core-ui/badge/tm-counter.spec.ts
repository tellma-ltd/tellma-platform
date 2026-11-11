// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi, TM_ACTIVE_LOCALE } from '@tellma/core-ui';
import { TmCounterHarness } from '@tellma/core-ui-testing';

import { TmCounter, type TmCounterPlacement, type TmCounterTone } from './tm-counter';

@Component({
  imports: [TmCounter],
  template: `
    <tm-counter
      [value]="value()"
      [max]="max()"
      [tone]="tone()"
      [placement]="placement()"
      [showZero]="showZero()"
    />
  `,
})
class Host {
  readonly value = signal<number | null>(3);
  readonly max = signal(99);
  readonly tone = signal<TmCounterTone>('neutral');
  readonly placement = signal<TmCounterPlacement>('inline');
  readonly showZero = signal(false);
}

async function setup(locale = 'en-US') {
  TestBed.configureTestingModule({
    providers: [provideTellmaUi(), { provide: TM_ACTIVE_LOCALE, useValue: signal(locale) }],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const counter = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmCounterHarness);
  return { fixture, host: fixture.componentInstance, counter };
}

describe('tm-counter', () => {
  it('shows the count in a left-to-right run, read as text when inline', async () => {
    const { fixture, counter } = await setup();
    expect(await counter.getText()).toBe('3');
    expect(await counter.isShown()).toBe(true);
    expect(await counter.getPlacement()).toBe('inline');
    expect(await counter.isHiddenFromAssistiveTechnology()).toBe(false);
    const element = fixture.nativeElement.querySelector('tm-counter') as HTMLElement;
    expect(element.getAttribute('dir')).toBe('ltr');
  });

  it('renders nothing for null and for zero, unless zero is asked for', async () => {
    const { fixture, host, counter } = await setup();
    host.value.set(null);
    fixture.detectChanges();
    expect(await counter.isShown()).toBe(false);
    expect(await counter.getText()).toBe('');

    host.value.set(0);
    fixture.detectChanges();
    expect(await counter.isShown()).toBe(false);

    host.showZero.set(true);
    fixture.detectChanges();
    expect(await counter.isShown()).toBe(true);
    expect(await counter.getText()).toBe('0');
  });

  it('caps the text at max: 120 reads "99+", and a custom max moves the cap', async () => {
    const { fixture, host, counter } = await setup();
    host.value.set(99);
    fixture.detectChanges();
    expect(await counter.getText()).toBe('99');
    host.value.set(120);
    fixture.detectChanges();
    expect(await counter.getText()).toBe('99+');
    host.max.set(999);
    host.value.set(1200);
    fixture.detectChanges();
    expect(await counter.getText()).toBe('999+');
  });

  it('writes digits in the active locale, the cap included', async () => {
    const { fixture, host, counter } = await setup('ar-SA');
    host.value.set(12);
    fixture.detectChanges();
    expect(await counter.getText()).toBe('١٢');
    host.value.set(120);
    fixture.detectChanges();
    expect(await counter.getText()).toBe('٩٩+');
  });

  it('hides an overlay counter from assistive technology', async () => {
    const { fixture, host, counter } = await setup();
    host.placement.set('overlay');
    fixture.detectChanges();
    expect(await counter.getPlacement()).toBe('overlay');
    expect(await counter.isHiddenFromAssistiveTechnology()).toBe(true);
  });

  it('reports the tone', async () => {
    const { fixture, host, counter } = await setup();
    expect(await counter.getTone()).toBe('neutral');
    host.tone.set('attention');
    fixture.detectChanges();
    expect(await counter.getTone()).toBe('attention');
    host.tone.set('accent');
    fixture.detectChanges();
    expect(await counter.getTone()).toBe('accent');
  });
});
