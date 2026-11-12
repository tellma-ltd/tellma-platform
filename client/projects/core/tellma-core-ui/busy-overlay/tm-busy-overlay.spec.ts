// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';
import { TmBusyOverlayHarness } from '@tellma/core-ui-testing';

import { TmBusyOverlay } from './tm-busy-overlay';

/** A promise with its settlers, for work that ends when the test says. */
function deferred<T = void>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}

/** Lets settled work's reactions run. */
async function flush(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
}

/** The page content the overlay blocks: a button with focus, and an already-inert aside. */
function page() {
  const main = document.createElement('main');
  const button = document.createElement('button');
  button.textContent = 'Save';
  main.appendChild(button);
  const aside = document.createElement('aside');
  aside.inert = true;
  document.body.append(main, aside);
  button.focus();
  return { main, button, aside, remove: () => (main.remove(), aside.remove()) };
}

/** Sets the service up under fake timers. */
function setup() {
  vi.useFakeTimers();
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const busy = TestBed.inject(TmBusyOverlay);
  const host = document.querySelector<HTMLElement>('.tm-busy-overlay')!;
  const label = () => {
    TestBed.tick();
    return host.querySelector('[role="status"]')!.textContent?.trim();
  };
  return { busy, host, label };
}

describe('TmBusyOverlay', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('keeps one idle popover host on the body that lets the pointer through', () => {
    const { busy, host } = setup();
    TestBed.tick();
    expect(document.querySelectorAll('.tm-busy-overlay')).toHaveLength(1);
    expect(host.getAttribute('popover')).toBe('manual');
    expect(host.matches(':popover-open')).toBe(true);
    expect(busy.active()).toBe(false);
    expect(getComputedStyle(host).pointerEvents).toBe('none');
    expect(host.querySelector('[role="status"]')).not.toBeNull();
  });

  it('blocks in the calling task: inert page, focus set aside, progress cursor, no scrim yet', () => {
    const { busy, host } = setup();
    const content = page();
    void busy.run(deferred().promise);
    expect(busy.active()).toBe(true);
    expect(content.main.inert).toBe(true);
    expect(host.inert).toBe(false);
    expect(document.activeElement).not.toBe(content.button);
    TestBed.tick();
    expect(getComputedStyle(host).cursor).toBe('progress');
    expect(getComputedStyle(host).pointerEvents).toBe('auto');
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(false);
    content.remove();
  });

  it('holds every key, Escape included, ahead of the page and the Escape stack', () => {
    const { busy } = setup();
    const content = page();
    const onEscape = vi.fn();
    const release = tmPushEscapeDismissal(onEscape);
    const seen = vi.fn();
    document.addEventListener('keydown', seen);
    void busy.run(deferred().promise);
    for (const key of ['Escape', 'Enter', 'Tab', 'a']) {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      document.body.dispatchEvent(event);
      expect(event.defaultPrevented).toBe(true);
    }
    expect(onEscape).not.toHaveBeenCalled();
    expect(seen).not.toHaveBeenCalled();
    document.removeEventListener('keydown', seen);
    release();
    content.remove();
  });

  it('shows the scrim, spinner and label after 200 ms', () => {
    const { busy, host, label } = setup();
    void busy.run(deferred().promise);
    vi.advanceTimersByTime(199);
    TestBed.tick();
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(false);
    expect(label()).toBe('');
    vi.advanceTimersByTime(1);
    TestBed.tick();
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(true);
    expect(host.querySelector('tm-spinner')).not.toBeNull();
    expect(label()).toBe('Please wait');
  });

  it('reads the delay from --busy-overlay-delay', () => {
    const { busy, host } = setup();
    document.documentElement.style.setProperty('--busy-overlay-delay', '0.5s');
    void busy.run(deferred().promise);
    vi.advanceTimersByTime(499);
    TestBed.tick();
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(false);
    vi.advanceTimersByTime(1);
    TestBed.tick();
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(true);
    document.documentElement.style.removeProperty('--busy-overlay-delay');
  });

  it('shows the scrim at once with immediate', () => {
    const { busy, host, label } = setup();
    vi.advanceTimersByTime(1);
    void busy.run(deferred().promise, { immediate: true, label: 'Signing out…' });
    TestBed.tick();
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(true);
    expect(label()).toBe('Signing out…');
  });

  it('fills the status region only after it has been in the page for a task', () => {
    const { busy, label } = setup();
    void busy.run(deferred().promise, { immediate: true });
    expect(label()).toBe('');
    vi.advanceTimersByTime(0);
    expect(label()).toBe('Please wait');
  });

  it('reads a signal label live, and nested runs show the latest run’s label', async () => {
    const { busy, label } = setup();
    vi.advanceTimersByTime(1);
    const text = signal('Finishing uploads…');
    const outer = deferred();
    const inner = deferred();
    void busy.run(outer.promise, { label: text, immediate: true });
    expect(label()).toBe('Finishing uploads…');
    text.set('Saving…');
    expect(label()).toBe('Saving…');
    void busy.run(inner.promise, { label: 'Posting…' });
    expect(label()).toBe('Posting…');
    inner.resolve();
    await flush();
    expect(busy.active()).toBe(true);
    expect(label()).toBe('Saving…');
    outer.resolve();
    await flush();
    expect(busy.active()).toBe(false);
  });

  it('unblocks when the last run settles, before the caller continues, and returns focus', async () => {
    const { busy, host } = setup();
    const content = page();
    const outer = deferred<string>();
    const inner = deferred();
    const result = busy.run(outer.promise);
    const nested = busy.run(inner.promise);
    inner.resolve();
    await nested;
    expect(content.main.inert).toBe(true);

    outer.resolve('saved');
    const seen = await result.then((value) => ({
      value,
      inert: content.main.inert,
      focused: document.activeElement,
      active: busy.active(),
    }));
    expect(seen).toEqual({ value: 'saved', inert: false, focused: content.button, active: false });
    // What was inert before stays inert.
    expect(content.aside.inert).toBe(true);
    TestBed.tick();
    expect(host.classList.contains('tm-busy-overlay--active')).toBe(false);
    expect(getComputedStyle(host).pointerEvents).toBe('none');
    content.remove();
  });

  it('unblocks on a failure too, and rejects with it', async () => {
    const { busy } = setup();
    const content = page();
    const work = deferred();
    const result = busy.run(work.promise);
    work.reject(new Error('conflict'));
    await expect(result).rejects.toThrow('conflict');
    expect(content.main.inert).toBe(false);
    expect(busy.active()).toBe(false);
    content.remove();
  });

  it('makes content added to the body while blocked inert, and frees it after', async () => {
    const { busy } = setup();
    const work = deferred();
    const result = busy.run(work.promise);
    const late = document.createElement('div');
    document.body.appendChild(late);
    await flush();
    expect(late.inert).toBe(true);
    work.resolve();
    await result;
    expect(late.inert).toBe(false);
    late.remove();
  });

  it('lets keys through again once unblocked', async () => {
    const { busy } = setup();
    const work = deferred();
    const result = busy.run(work.promise);
    work.resolve();
    await result;
    const event = new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true });
    document.body.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(false);
  });
});

@Component({ template: '' })
class Empty {}

describe('TmBusyOverlayHarness', () => {
  it('reads the overlay through the document-root loader', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(Empty);
    const busy = TestBed.inject(TmBusyOverlay);
    // The status region has been in the page for a task before any run.
    await new Promise((resolve) => setTimeout(resolve));
    const overlay = await TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(
      TmBusyOverlayHarness,
    );
    expect(await overlay.isActive()).toBe(false);
    const work = deferred();
    const result = busy.run(work.promise, { immediate: true, label: 'Saving…' });
    expect(await overlay.isActive()).toBe(true);
    expect(await overlay.isScrimShown()).toBe(true);
    expect(await overlay.hasSpinner()).toBe(true);
    expect(await overlay.getLabel()).toBe('Saving…');
    work.resolve();
    await result;
    expect(await overlay.isActive()).toBe(false);
    expect(await overlay.getLabel()).toBe('');
  });
});
