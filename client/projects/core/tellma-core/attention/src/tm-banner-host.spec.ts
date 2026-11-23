// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { signal } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { TmBanners, TmInputIdle, type TmBannerOptions } from '@tellma/core';
import { provideTellmaTesting } from '@tellma/core-testing';
import { provideTellmaUi } from '@tellma/core-ui';

import { TmBannerHost } from './tm-banner-host';

// The banner host over the real banner service and a scripted input-idle
// clock: the row it renders after the gate, the buttons it wires back, and
// the live region that speaks at queue time, before the row opens.

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

/** The rendered host. */
interface TmHostRig {
  readonly fixture: ComponentFixture<TmBannerHost>;
  readonly banners: TmBanners;
  readonly idle: TmScriptedIdle;
  /** The host's element. */
  readonly element: HTMLElement;
  /** Runs change detection, after any pending promise. */
  render(): Promise<void>;
}

/** Renders the host with the clock faked and the tab busy. */
async function setUp(): Promise<TmHostRig> {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  const idle = new TmScriptedIdle();
  TestBed.configureTestingModule({ providers: [provideTellmaTesting(), provideTellmaUi(), { provide: TmInputIdle, useValue: idle }] });
  const fixture = TestBed.createComponent(TmBannerHost);
  const render = async (): Promise<void> => {
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
  };
  await render();
  return { fixture, banners: TestBed.inject(TmBanners), idle, element: fixture.nativeElement as HTMLElement, render };
}

/** A banner's options. */
function banner(id: string, level: TmBannerOptions['level'], members: Partial<TmBannerOptions> = {}): TmBannerOptions {
  return { id, level, tone: 'warning', message: signal(`${id} message`), dismissible: level > 2, ...members };
}

/** The live region. */
function live(element: HTMLElement): HTMLElement {
  return element.querySelector<HTMLElement>('.tm-banner-host__live')!;
}

/** The bar's message, without its hidden tone prefix. */
function message(element: HTMLElement): string | null {
  const text = element.querySelector('.tm-banner-bar__message');
  const prefix = element.querySelector('.tm-banner-bar__tone');
  return text === null ? null : (text.textContent ?? '').slice((prefix?.textContent ?? '').length).trim();
}

/** Clicks one of the bar's buttons. */
function click(element: HTMLElement, selector: string): void {
  element.querySelector<HTMLButtonElement>(selector)!.click();
}

describe('TmBannerHost', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('renders nothing but the empty live region while there is no banner', async () => {
    const { element } = await setUp();
    expect(element.querySelector('tm-banner-bar')).toBeNull();
    expect(element.querySelector('.tm-banner-host__empty')).toBeNull();
    expect(live(element).textContent).toBe('');
  });

  it('announces a banner at once, assertively for level 1, while the row waits for the gate', async () => {
    const { banners, element, idle, render } = await setUp();
    banners.show(banner('tm.offline', 1, { tone: 'error' }));
    await render();
    expect(live(element).getAttribute('aria-live')).toBe('assertive');
    expect(live(element).getAttribute('aria-atomic')).toBe('true');
    await vi.advanceTimersByTimeAsync(100);
    await render();
    expect(live(element).textContent).toBe('tm.offline message');
    expect(element.querySelector('tm-banner-bar')).toBeNull();

    idle.set(true);
    await render();
    expect(message(element)).toBe('tm.offline message');
    expect(element.querySelector('tm-banner-bar')?.getAttribute('data-tone')).toBe('error');
    expect(element.querySelector('tm-banner-bar')?.hasAttribute('aria-live')).toBe(false);
  });

  it('announces a lower level politely, and each banner once', async () => {
    const { banners, element, render } = await setUp();
    const ref = banners.show(banner('acme.closing', 4));
    await vi.advanceTimersByTimeAsync(100);
    await render();
    expect(live(element).getAttribute('aria-live')).toBe('polite');
    expect(live(element).textContent).toBe('acme.closing message');

    ref.update({ tone: 'info' });
    await render();
    expect(live(element).textContent).toBe('acme.closing message');
  });

  it('empties the live region when the last banner closes', async () => {
    const { banners, element, render } = await setUp();
    const ref = banners.show(banner('tm.offline', 1, { tone: 'error' }));
    await vi.advanceTimersByTimeAsync(100);
    await render();
    expect(live(element).textContent).toBe('tm.offline message');

    ref.close();
    await render();
    expect(live(element).textContent).toBe('');
  });

  it('never writes a banner that closed before its announcement was due', async () => {
    const { banners, element, render } = await setUp();
    const ref = banners.show(banner('acme.closing', 4));
    await render();
    ref.close();
    await render();
    await vi.advanceTimersByTimeAsync(100);
    await render();
    expect(live(element).textContent).toBe('');
  });

  it('wires the action, the pager and the dismiss button back to the banners', async () => {
    const { banners, element, idle, render } = await setUp();
    idle.set(true);
    const run = vi.fn();
    banners.show(banner('acme.a', 4, { action: { label: signal('Learn more'), run } }));
    banners.show(banner('acme.b', 4));
    await render();
    expect(element.querySelector('.tm-banner-bar__position')?.textContent?.trim()).toMatch(/1.*2/);

    click(element, '.tm-banner-bar__action');
    expect(run).toHaveBeenCalledOnce();

    click(element, '.tm-banner-bar__next');
    await render();
    expect(message(element)).toBe('acme.b message');
    click(element, '.tm-banner-bar__previous');
    await render();
    expect(message(element)).toBe('acme.a message');

    click(element, '.tm-banner-bar__dismiss');
    await render();
    expect(message(element)).toBe('acme.b message');
    expect(element.querySelector('.tm-banner-bar__position')).toBeNull();
  });

  it('keeps an emptied row open as an empty box until the gate closes it', async () => {
    const { banners, element, idle, render } = await setUp();
    idle.set(true);
    const ref = banners.show(banner('acme.a', 4));
    await render();
    idle.set(false);
    ref.close();
    await render();
    expect(element.querySelector('tm-banner-bar')).toBeNull();
    expect(element.querySelector('.tm-banner-host__empty')).not.toBeNull();

    idle.set(true);
    await render();
    expect(element.querySelector('.tm-banner-host__empty')).toBeNull();
  });
});
