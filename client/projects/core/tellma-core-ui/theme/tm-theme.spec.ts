// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { effect, Injector, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '../providers/provide-tellma-ui';
import { TmTheme, ɵTM_THEME_OPTIONS, type TmThemeOptions } from './tm-theme';

/**
 * A controllable `(prefers-color-scheme: dark)` list: the test flips the
 * emulated OS preference and counts the listeners the theme holds.
 */
class FakeDarkQuery {
  matches: boolean;
  readonly listeners = new Set<(event: MediaQueryListEvent) => void>();
  readonly media = '(prefers-color-scheme: dark)';

  constructor(matches: boolean) {
    this.matches = matches;
  }

  addEventListener(_type: 'change', listener: (event: MediaQueryListEvent) => void): void {
    this.listeners.add(listener);
  }

  removeEventListener(_type: 'change', listener: (event: MediaQueryListEvent) => void): void {
    this.listeners.delete(listener);
  }

  /** Emulates the OS switching its preference. */
  emulate(matches: boolean): void {
    this.matches = matches;
    for (const listener of this.listeners) {
      listener({ matches, media: this.media } as MediaQueryListEvent);
    }
  }
}

describe('TmTheme', () => {
  let query: FakeDarkQuery;
  const root = document.documentElement;

  /** Installs the fake OS preference and the optional theme options. */
  function setUp(osDark: boolean, options?: TmThemeOptions): void {
    query = new FakeDarkQuery(osDark);
    const original = window.matchMedia.bind(window);
    vi.spyOn(window, 'matchMedia').mockImplementation((media: string) =>
      media === query.media ? (query as unknown as MediaQueryList) : original(media),
    );
    TestBed.configureTestingModule({
      providers: options ? [{ provide: ɵTM_THEME_OPTIONS, useValue: options }] : [],
    });
  }

  afterEach(() => {
    vi.restoreAllMocks();
    root.removeAttribute('data-theme');
  });

  it('starts from system and paints the OS preference, never "system"', () => {
    setUp(true);
    const theme = TestBed.inject(TmTheme);
    expect(theme.setting()).toBe('system');
    expect(theme.effective()).toBe('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');
  });

  it('writes <html data-theme> in the same task as set()', () => {
    setUp(false);
    const theme = TestBed.inject(TmTheme);
    expect(root.getAttribute('data-theme')).toBe('light');

    // No await between the call and the read: the write is synchronous.
    theme.set('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(theme.setting()).toBe('dark');
    expect(theme.effective()).toBe('dark');

    theme.set('light');
    expect(root.getAttribute('data-theme')).toBe('light');
  });

  it('attaches the media listener only under system', () => {
    setUp(false);
    const theme = TestBed.inject(TmTheme);
    expect(query.listeners.size).toBe(1);

    theme.set('light');
    expect(query.listeners.size).toBe(0);
    theme.set('dark');
    expect(query.listeners.size).toBe(0);

    theme.set('system');
    expect(query.listeners.size).toBe(1);
    // Re-selecting system never stacks a second listener.
    theme.set('system');
    expect(query.listeners.size).toBe(1);
  });

  it('follows an OS change under system, and ignores it otherwise', () => {
    setUp(false);
    const theme = TestBed.inject(TmTheme);
    query.emulate(true);
    expect(theme.effective()).toBe('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');

    theme.set('light');
    query.emulate(false);
    query.emulate(true);
    expect(theme.effective()).toBe('light');
    expect(root.getAttribute('data-theme')).toBe('light');
  });

  it('re-reads the OS preference when switching back to system', () => {
    setUp(false);
    const theme = TestBed.inject(TmTheme);
    theme.set('light');
    // The OS changed while the listener was detached…
    query.matches = true;
    theme.set('system');
    // …so switching back must read it, not reuse a stale value.
    expect(theme.effective()).toBe('dark');
    expect(root.getAttribute('data-theme')).toBe('dark');
  });

  it('seeds from the initial option, as a value or a function read once', () => {
    setUp(false, { initial: 'dark' });
    expect(TestBed.inject(TmTheme).setting()).toBe('dark');
    expect(query.listeners.size).toBe(0);

    TestBed.resetTestingModule();
    vi.restoreAllMocks();
    const initial = vi.fn(() => 'light' as const);
    setUp(true, { initial });
    const theme = TestBed.inject(TmTheme);
    expect(theme.setting()).toBe('light');
    expect(theme.effective()).toBe('light');
    expect(initial).toHaveBeenCalledTimes(1);
  });

  it('never rewrites an attribute that already holds the scheme', async () => {
    // What a boot script painted before the app started.
    root.setAttribute('data-theme', 'dark');
    setUp(false, { initial: 'dark' });
    const mutations: MutationRecord[] = [];
    const observer = new MutationObserver((records) => mutations.push(...records));
    observer.observe(root, { attributes: true, attributeFilter: ['data-theme'] });
    TestBed.inject(TmTheme).set('dark');
    // MutationObserver callbacks run as microtasks; let them flush.
    await Promise.resolve();
    observer.disconnect();
    expect(mutations).toEqual([]);
  });

  it('starts at boot from provideTellmaUi({ theme }), before anything injects it', () => {
    query = new FakeDarkQuery(true);
    const original = window.matchMedia.bind(window);
    vi.spyOn(window, 'matchMedia').mockImplementation((media: string) =>
      media === query.media ? (query as unknown as MediaQueryList) : original(media),
    );
    TestBed.configureTestingModule({
      providers: [provideTellmaUi({ theme: { initial: 'system' } })],
    });
    // Creating the environment runs its initializers; nothing asks for TmTheme.
    TestBed.inject(Injector);
    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(query.listeners.size).toBe(1);
    query.emulate(false);
    expect(root.getAttribute('data-theme')).toBe('light');
  });

  it('is not created at boot without the theme option', () => {
    setUp(true);
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    TestBed.inject(Injector);
    expect(root.hasAttribute('data-theme')).toBe(false);
    expect(query.listeners.size).toBe(0);
  });

  it('never makes an effect that calls set() depend on the theme itself', () => {
    setUp(false);
    const theme = TestBed.inject(TmTheme);
    const seed = signal<'light' | 'dark'>('light');
    let runs = 0;
    TestBed.runInInjectionContext(() =>
      effect(() => {
        runs++;
        theme.set(seed());
      }),
    );
    TestBed.tick();
    expect(runs).toBe(1);

    // A choice made elsewhere sticks: the seeding effect does not re-run.
    theme.set('system');
    TestBed.tick();
    expect(runs).toBe(1);
    expect(theme.setting()).toBe('system');

    // Its own input still drives it.
    seed.set('dark');
    TestBed.tick();
    expect(runs).toBe(2);
    expect(theme.setting()).toBe('dark');
  });

  it('detaches its listener when the injector is destroyed', () => {
    setUp(false);
    TestBed.inject(TmTheme);
    expect(query.listeners.size).toBe(1);
    TestBed.resetTestingModule();
    expect(query.listeners.size).toBe(0);
  });
});
