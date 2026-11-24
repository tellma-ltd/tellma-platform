// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';
import { signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { TmBanners, TmInputIdle, type TmBannerOptions } from '@tellma/core';
import { TmBannerHost } from '@tellma/core/attention';
import { provideTellmaUi } from '@tellma/core-ui';

import { provideTellmaTesting } from './provide-tellma-testing';
import { TmBannerHostHarness } from './tm-banner-host-harness';

// The banner host's harness over the real host and banners, with an
// input-idle clock the test opens: the row's states, the shown banner
// through the banner-bar harness, and the live region's text and politeness.

/** An input-idle clock the test opens and closes. */
class TmScriptedIdle {
  readonly #idle = signal(false);
  #wake: (() => void) | null = null;

  /** Whether the tab is idle. */
  readonly idle = this.#idle.asReadonly();

  /** Resolves at once while idle, else when the test next opens the gate. */
  whenIdle(): Promise<void> {
    return this.#idle() ? Promise.resolve() : new Promise((resolve) => (this.#wake = resolve));
  }

  /** Sets whether the tab is idle; idle wakes the waiter. */
  set(idle: boolean): void {
    this.#idle.set(idle);
    if (idle) {
      this.#wake?.();
      this.#wake = null;
    }
  }
}

/** A banner's options. */
function banner(id: string, level: TmBannerOptions['level'], members: Partial<TmBannerOptions> = {}): TmBannerOptions {
  return { id, level, tone: 'warning', message: signal(`${id} message`), dismissible: level > 2, ...members };
}

/** Renders the host and loads its harness. */
async function setUp(): Promise<{
  readonly fixture: ComponentFixture<TmBannerHost>;
  readonly banners: TmBanners;
  readonly idle: TmScriptedIdle;
  readonly host: TmBannerHostHarness;
}> {
  const idle = new TmScriptedIdle();
  TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaUi(), { provide: TmInputIdle, useValue: idle }] });
  const fixture = TestBed.createComponent(TmBannerHost);
  const host = await TestbedHarnessEnvironment.harnessForFixture(fixture, TmBannerHostHarness);
  return { fixture, banners: TestBed.inject(TmBanners), idle, host };
}

describe('TmBannerHostHarness', () => {
  it('reads a closed row with nothing to announce', async () => {
    const { host } = await setUp();
    expect(await host.isOpen()).toBe(false);
    expect(await host.isEmpty()).toBe(false);
    expect(await host.getBar()).toBeNull();
    expect(await host.getAnnouncement()).toBe('');
    expect(await host.getPoliteness()).toBe('polite');
  });

  it('reads the announcement and its politeness before the row opens, then the shown banner', async () => {
    const { banners, idle, host } = await setUp();
    banners.show(banner('tm.offline', 1, { tone: 'error' }));
    // The announcement follows its short pause.
    await new Promise((resolve) => setTimeout(resolve, 150));
    expect(await host.getPoliteness()).toBe('assertive');
    expect(await host.getAnnouncement()).toBe('tm.offline message');
    expect(await host.isOpen()).toBe(false);

    // The gate's waiter resumes on its own turn, after which the row opens.
    idle.set(true);
    await new Promise((resolve) => setTimeout(resolve));
    expect(await host.isOpen()).toBe(true);
    const bar = await host.getBar();
    expect(await bar?.getMessage()).toBe('tm.offline message');
    expect(await bar?.getTone()).toBe('error');
  });

  it('dismisses through the bar, and reads an emptied row while the gate holds it open', async () => {
    const { banners, idle, host } = await setUp();
    idle.set(true);
    banners.show(banner('acme.closing', 4));
    expect(await host.isOpen()).toBe(true);

    idle.set(false);
    await (await host.getBar())?.dismiss();
    // The user's own dismiss applies at once, so the row is closed rather than emptied.
    expect(await host.getBar()).toBeNull();
    expect(await host.isOpen()).toBe(false);

    idle.set(true);
    const ref = banners.show(banner('acme.other', 4));
    expect(await host.isOpen()).toBe(true);
    idle.set(false);
    ref.close();
    expect(await host.isEmpty()).toBe(true);
    expect(await host.isOpen()).toBe(true);
  });
});
