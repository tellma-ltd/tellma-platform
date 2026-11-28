// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmModal } from '@tellma/core-ui/modal';
import { tmPushEscapeDismissal, ɵtmIsBusy, ɵtmModalOpened } from '@tellma/core-ui/overlay';
import { TmToasts } from '@tellma/core-ui/toast';
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

/**
 * A modal as TmModal shows one: a `popover="manual"` host with a button,
 * in a container on the body, shown and recorded as an open modal.
 */
function modal() {
  const container = document.createElement('div');
  const host = document.createElement('div');
  host.setAttribute('popover', 'manual');
  const button = document.createElement('button');
  button.textContent = 'Continue';
  host.appendChild(button);
  container.appendChild(host);
  document.body.appendChild(container);
  host.showPopover();
  const closed = ɵtmModalOpened(host);
  return {
    container,
    button,
    close: () => {
      closed();
      host.hidePopover();
      container.remove();
    },
  };
}

/** Presses a key on the body and answers whether something held it. */
function keyHeld(): boolean {
  const event = new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true });
  document.body.dispatchEvent(event);
  return event.defaultPrevented;
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

  it('leaves top-layer popovers and live regions readable, so announcements are heard', async () => {
    const { busy } = setup();
    const toasts = document.createElement('div');
    toasts.setAttribute('popover', 'manual');
    const announcer = document.createElement('div');
    announcer.setAttribute('aria-live', 'polite');
    const content = page();
    document.body.append(toasts, announcer);
    const work = deferred();
    const result = busy.run(work.promise);
    const late = document.createElement('div');
    late.setAttribute('aria-live', 'assertive');
    document.body.appendChild(late);
    await flush();
    expect(content.main.inert).toBe(true);
    expect(toasts.inert).toBe(false);
    expect(announcer.inert).toBe(false);
    expect(late.inert).toBe(false);
    work.resolve();
    await result;
    toasts.remove();
    announcer.remove();
    late.remove();
    content.remove();
  });

  it('stands aside for a modal opened during a run, and blocks again with the scrim at once when it closes', async () => {
    const { busy, host } = setup();
    const content = page();
    const work = deferred();
    const result = busy.run(work.promise);

    // --- The write asks for a confirmation: the page is unblocked so it can be answered ---
    const confirm = modal();
    TestBed.tick();
    expect(busy.active()).toBe(true);
    expect(confirm.container.inert).toBe(false);
    expect(content.main.inert).toBe(false);
    expect(keyHeld()).toBe(false);
    expect(host.classList.contains('tm-busy-overlay--active')).toBe(false);
    expect(getComputedStyle(host).pointerEvents).toBe('none');

    // The delay passes while the modal is open: no scrim over it.
    vi.advanceTimersByTime(500);
    TestBed.tick();
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(false);

    // --- Answered: the run goes on, blocked, with the scrim at once ---
    confirm.close();
    TestBed.tick();
    expect(content.main.inert).toBe(true);
    expect(keyHeld()).toBe(true);
    expect(host.classList.contains('tm-busy-overlay--active')).toBe(true);
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(true);

    work.resolve();
    await result;
    expect(content.main.inert).toBe(false);
    expect(document.activeElement).toBe(content.button);
    content.remove();
  });

  it('blocks a modal that was open when the run began, with the page', async () => {
    const { busy } = setup();
    const open = modal();
    const work = deferred();
    const result = busy.run(work.promise);
    expect(open.container.inert).toBe(true);
    expect(keyHeld()).toBe(true);
    work.resolve();
    await result;
    expect(open.container.inert).toBe(false);
    open.close();
  });

  it('blocks a modal that a run started from, and gives focus back there when that run ends', async () => {
    const { busy } = setup();
    const content = page();
    const outer = deferred();
    const outerResult = busy.run(outer.promise);
    const confirm = modal();
    confirm.button.focus();

    // The modal's own action runs under the overlay: the modal is blocked too.
    const inner = deferred();
    const innerResult = busy.run(inner.promise);
    expect(confirm.container.inert).toBe(true);
    expect(document.activeElement).not.toBe(confirm.button);
    expect(keyHeld()).toBe(true);

    // That run ends with the outer one still waiting on the modal: the overlay stands aside again.
    inner.resolve();
    await innerResult;
    expect(confirm.container.inert).toBe(false);
    expect(document.activeElement).toBe(confirm.button);
    expect(keyHeld()).toBe(false);

    confirm.close();
    outer.resolve();
    await outerResult;
    expect(document.activeElement).toBe(content.button);
    content.remove();
  });

  it('leaves focus in a modal the work opened when the last run settles', async () => {
    const { busy } = setup();
    const content = page();
    const work = deferred();
    const result = busy.run(work.promise);
    const opened = modal();
    opened.button.focus();
    work.resolve();
    await result;
    expect(busy.active()).toBe(false);
    expect(document.activeElement).toBe(opened.button);
    opened.close();
    content.remove();
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

  it('tells the page-wide layers a run is in progress, while it stands aside for a modal too', async () => {
    expect(ɵtmIsBusy()).toBe(false);
    const { busy } = setup();
    expect(ɵtmIsBusy()).toBe(false);
    const work = deferred();
    const result = busy.run(work.promise);
    expect(ɵtmIsBusy()).toBe(true);
    const confirmation = modal();
    expect(ɵtmIsBusy()).toBe(true);
    confirmation.close();
    work.resolve();
    await result;
    expect(ɵtmIsBusy()).toBe(false);
  });

  it('stops answering for the layers once the app that created it is destroyed', () => {
    const { busy } = setup();
    void busy.run(new Promise<void>(() => {}));
    expect(ɵtmIsBusy()).toBe(true);
    TestBed.resetTestingModule();
    expect(ɵtmIsBusy()).toBe(false);
  });
});

@Component({ template: '' })
class Empty {}

/** A page with the button a write starts from. */
@Component({ template: `<button type="button" class="save">Save</button>` })
class SavePage {}

/** A confirmation's content: one button that answers it. */
@Component({ template: `<button type="button" class="continue">Continue</button>` })
class ConfirmProbe {}

describe('TmBusyOverlay with the page-wide layers', () => {
  /** Lets the CDK's focus trap and the observers settle. */
  const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

  it('lets a TmModal opened during a run take focus and keys, then blocks again when it closes', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(SavePage);
    await fixture.whenStable();
    const save = fixture.nativeElement.querySelector('.save') as HTMLButtonElement;
    save.focus();
    const busy = TestBed.inject(TmBusyOverlay);
    const host = document.querySelector<HTMLElement>('.tm-busy-overlay')!;
    const work = deferred();
    const result = busy.run(work.promise);

    // --- The confirmation opens mid-run: it is reachable and takes focus ---
    const ref = TestBed.inject(TmModal).open(ConfirmProbe, { title: 'Confirm to continue' });
    await fixture.whenStable();
    await settle();
    const dialog = document.querySelector('cdk-dialog-container') as HTMLElement;
    expect(dialog.closest('[inert]')).toBeNull();
    expect(dialog.contains(document.activeElement)).toBe(true);
    const key = new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true });
    document.activeElement!.dispatchEvent(key);
    expect(key.defaultPrevented).toBe(false);

    // --- Answered: blocked again with the scrim, then focus returns to Save ---
    ref.close();
    await fixture.whenStable();
    await settle();
    TestBed.tick();
    expect(save.closest('[inert]')).not.toBeNull();
    expect(host.classList.contains('tm-busy-overlay--scrim')).toBe(true);
    work.resolve();
    await result;
    expect(document.activeElement).toBe(save);
  });

  it('leaves focus in a TmModal the work opened when the last run settles', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(SavePage);
    await fixture.whenStable();
    const save = fixture.nativeElement.querySelector('.save') as HTMLButtonElement;
    save.focus();
    const busy = TestBed.inject(TmBusyOverlay);
    const work = deferred();
    const result = busy.run(work.promise);
    const ref = TestBed.inject(TmModal).open(ConfirmProbe, { title: 'Confirm to continue' });
    await fixture.whenStable();
    await settle();

    work.resolve();
    await result;
    const dialog = document.querySelector('cdk-dialog-container') as HTMLElement;
    expect(dialog.contains(document.activeElement)).toBe(true);
    ref.close();
    await fixture.whenStable();
  });

  it('stays above a toast while blocking when an overlay opening re-raises the toasts', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const toasts = TestBed.inject(TmToasts);
    const busy = TestBed.inject(TmBusyOverlay);
    const host = document.querySelector<HTMLElement>('.tm-busy-overlay')!;
    toasts.show({ tone: 'error', title: 'Upload failed' });
    TestBed.tick();
    const toast = document.querySelector<HTMLElement>('.tm-toast')!.getBoundingClientRect();
    const x = toast.left + toast.width / 2;
    const y = toast.top + toast.height / 2;

    const work = deferred();
    const result = busy.run(work.promise);
    TestBed.tick();
    expect(host.contains(document.elementFromPoint(x, y))).toBe(true);

    // Something reaches the overlay container, so the toasts re-raise: the overlay stays on top.
    const pane = document.createElement('div');
    TestBed.inject(OverlayContainer).getContainerElement().appendChild(pane);
    await settle();
    expect(host.contains(document.elementFromPoint(x, y))).toBe(true);

    work.resolve();
    await result;
    TestBed.tick();
    expect(host.contains(document.elementFromPoint(x, y))).toBe(false);
    pane.remove();
  });
});

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
