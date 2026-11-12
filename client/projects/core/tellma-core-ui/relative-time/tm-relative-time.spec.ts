// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import {
  provideTellmaUi,
  TM_ACTIVE_LOCALE,
  TM_DEVICE_TIME_ZONE,
  TM_TIME_ZONE,
} from '@tellma/core-ui';
import { TmRelativeTimeHarness } from '@tellma/core-ui-testing';

import { TmMinuteTicker } from './internal/minute-ticker';
import { TmRelativeTime } from './tm-relative-time';

/** 2026-10-20 10:40:20 UTC, twenty seconds into a minute. */
const START = Date.UTC(2026, 9, 20, 10, 40, 20);

/** An instant two minutes before the start. */
const TWO_MINUTES_AGO = '2026-10-20T10:38:20Z';

@Component({
  imports: [TmRelativeTime],
  template: `
    @if (shown()) {
      <tm-relative-time [value]="value()" />
      <tm-relative-time [value]="value()" textStyle="long" />
      <tm-relative-time [value]="value()" textStyle="narrow" />
    }
  `,
})
class Host {
  readonly value = signal(TWO_MINUTES_AGO);
  readonly shown = signal(true);
}

/** Pins the page's visibility for the visibility tests. */
function setVisibility(state: DocumentVisibilityState): void {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state);
  document.dispatchEvent(new Event('visibilitychange'));
}

async function setup() {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
  vi.setSystemTime(START);
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: TM_ACTIVE_LOCALE, useValue: signal('en-GB') },
      { provide: TM_TIME_ZONE, useValue: signal('UTC') },
      { provide: TM_DEVICE_TIME_ZONE, useValue: signal('UTC') },
    ],
  });
  const fixture = TestBed.createComponent(Host);
  fixture.detectChanges();
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const [short, long, narrow] = await loader.getAllHarnesses(TmRelativeTimeHarness);
  return { fixture, host: fixture.componentInstance, short, long, narrow };
}

describe('tm-relative-time', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it('shows the age in a time element, in the style asked for', async () => {
    const { short, long, narrow } = await setup();
    expect(await short.getText()).toBe('2 min ago');
    expect(await long.getText()).toBe('2 minutes ago');
    expect(await narrow.getText()).toMatch(/^2\s?min\.? ago$/);
    expect(await long.getText()).toBe('2 minutes ago');
    expect(await short.getDateTime()).toBe(TWO_MINUTES_AGO);
  });

  it('describes the absolute date and time', async () => {
    const { short } = await setup();
    expect(await short.getAbsoluteText()).toBe('20 Oct 2026, 10:38');
  });

  it('ticks once a minute, on the minute boundary', async () => {
    const { fixture, short } = await setup();
    // 40 s to the boundary at 10:41:00: still two minutes.
    vi.advanceTimersByTime(39_999);
    fixture.detectChanges();
    expect(await short.getText()).toBe('2 min ago');
    vi.advanceTimersByTime(1);
    fixture.detectChanges();
    // 10:41:00 is 2 min 40 s after the instant; the next boundary passes 3.
    vi.advanceTimersByTime(60_000);
    fixture.detectChanges();
    expect(await short.getText()).toBe('3 min ago');
    vi.advanceTimersByTime(60_000);
    fixture.detectChanges();
    expect(await short.getText()).toBe('4 min ago');
  });

  it('runs one timer for every instance on the page', async () => {
    await setup();
    expect(vi.getTimerCount()).toBe(1);
  });

  it('stops while the page is hidden and catches up when it shows again', async () => {
    const { fixture, short } = await setup();
    setVisibility('hidden');
    expect(vi.getTimerCount()).toBe(0);
    vi.setSystemTime(START + 10 * 60_000);
    fixture.detectChanges();
    expect(await short.getText()).toBe('2 min ago');

    setVisibility('visible');
    fixture.detectChanges();
    expect(await short.getText()).toBe('12 min ago');
    expect(vi.getTimerCount()).toBe(1);
  });

  it('stops the timer when the last instance goes', async () => {
    const { fixture, host } = await setup();
    expect(vi.getTimerCount()).toBe(1);
    host.shown.set(false);
    fixture.detectChanges();
    expect(vi.getTimerCount()).toBe(0);
  });
});

describe('TmMinuteTicker', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('re-arms on the next boundary after a tick, and a stop is idempotent', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'Date'] });
    vi.setSystemTime(START);
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const ticker = TestBed.inject(TmMinuteTicker);
    const first = ticker.subscribe();
    const second = ticker.subscribe();
    expect(ticker.tick()).toBe(0);
    vi.advanceTimersByTime(40_000);
    expect(ticker.tick()).toBe(1);
    vi.advanceTimersByTime(60_000);
    expect(ticker.tick()).toBe(2);
    first();
    first();
    expect(vi.getTimerCount()).toBe(1);
    second();
    expect(vi.getTimerCount()).toBe(0);
  });
});
