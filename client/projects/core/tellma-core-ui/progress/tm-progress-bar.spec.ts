// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed, type ComponentFixture } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmProgressBarHarness } from '@tellma/core-ui-testing';

import { TmProgressBar, type TmProgressTone } from './tm-progress-bar';

@Component({
  imports: [TmProgressBar],
  template: `
    <tm-progress-bar
      [value]="value()"
      [active]="active()"
      [tone]="tone()"
      label="Loading invoices"
    />
  `,
})
class Host {
  readonly value = signal<number | null>(null);
  readonly active = signal(true);
  readonly tone = signal<TmProgressTone>('accent');
}

async function setup(active = true) {
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  fixture.componentInstance.active.set(active);
  fixture.detectChanges();
  const bar = await TestbedHarnessEnvironment.loader(fixture).getHarness(TmProgressBarHarness);
  return { fixture, host: fixture.componentInstance, bar };
}

/** The host element, for the role checks the harness does not expose. */
function element(fixture: ComponentFixture<Host>): HTMLElement {
  return (fixture.nativeElement as HTMLElement).querySelector('tm-progress-bar')!;
}

describe('tm-progress-bar', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('is a named progress bar with a value from 0 to 100', async () => {
    const { fixture, host, bar } = await setup();
    host.value.set(40);
    fixture.detectChanges();
    expect(element(fixture).getAttribute('role')).toBe('progressbar');
    expect(await bar.getLabel()).toBe('Loading invoices');
    expect(await bar.getValue()).toBe(40);
    expect(element(fixture).getAttribute('aria-valuemin')).toBe('0');
    expect(element(fixture).getAttribute('aria-valuemax')).toBe('100');
  });

  it('is indeterminate for null, with no value range', async () => {
    const { fixture, bar } = await setup();
    expect(await bar.isIndeterminate()).toBe(true);
    expect(element(fixture).hasAttribute('aria-valuemin')).toBe(false);
    expect(element(fixture).querySelector('.tm-progress-bar__fill--sweep')).not.toBeNull();
  });

  it('clamps the value into 0 to 100', async () => {
    const { fixture, host, bar } = await setup();
    host.value.set(140);
    fixture.detectChanges();
    expect(await bar.getValue()).toBe(100);
    host.value.set(-5);
    fixture.detectChanges();
    expect(await bar.getValue()).toBe(0);
  });

  it('reports the tone', async () => {
    const { fixture, host, bar } = await setup();
    expect(await bar.getTone()).toBe('accent');
    host.tone.set('error');
    fixture.detectChanges();
    expect(await bar.getTone()).toBe('error');
  });

  it('renders nothing, with no role, when it starts inactive', async () => {
    const { fixture, bar } = await setup(false);
    expect(await bar.isShown()).toBe(false);
    expect(element(fixture).hasAttribute('role')).toBe(false);
    expect(element(fixture).querySelector('.tm-progress-bar__track')).toBeNull();
  });

  it('stays on screen for 250 ms after it appears, however soon the task ends', async () => {
    vi.useFakeTimers();
    const { fixture, host, bar } = await setup();
    vi.advanceTimersByTime(100);
    host.active.set(false);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(true);
    expect(element(fixture).getAttribute('role')).toBe('progressbar');

    vi.advanceTimersByTime(149);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(true);

    vi.advanceTimersByTime(1);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(false);
    expect(element(fixture).hasAttribute('role')).toBe(false);
  });

  it('hides at once when it has already been shown for the minimum time', async () => {
    vi.useFakeTimers();
    const { fixture, host, bar } = await setup();
    vi.advanceTimersByTime(300);
    host.active.set(false);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(false);
  });

  it('stays when the task restarts during the hold, timed from its first appearance', async () => {
    vi.useFakeTimers();
    const { fixture, host, bar } = await setup();
    vi.advanceTimersByTime(100);
    host.active.set(false);
    fixture.detectChanges();
    vi.advanceTimersByTime(50);
    host.active.set(true);
    fixture.detectChanges();
    vi.advanceTimersByTime(500);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(true);

    host.active.set(false);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(false);

    // A fresh appearance starts a fresh hold.
    host.active.set(true);
    fixture.detectChanges();
    vi.advanceTimersByTime(10);
    host.active.set(false);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(true);
    vi.advanceTimersByTime(240);
    fixture.detectChanges();
    expect(await bar.isShown()).toBe(false);
  });
});
