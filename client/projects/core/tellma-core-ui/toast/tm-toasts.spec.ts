// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { OverlayContainer } from '@angular/cdk/overlay';
import { Component, ErrorHandler } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TestbedHarnessEnvironment } from '@angular/cdk/testing/testbed';

import { provideTellmaUi, TM_NOW } from '@tellma/core-ui';
import { tmPushEscapeDismissal } from '@tellma/core-ui/overlay';
import { TmToastHarness, TmToastStackHarness } from '@tellma/core-ui-testing';

import { provideTellmaToasts, TmToasts, type TmToastsOptions } from './tm-toasts';

/** Sets the service up under fake timers, with the clock read through TM_NOW. */
function setup(options?: TmToastsOptions) {
  vi.useFakeTimers();
  TestBed.configureTestingModule({
    providers: [
      provideTellmaUi(),
      { provide: TM_NOW, useValue: () => Date.now() },
      ...(options === undefined ? [] : [provideTellmaToasts(options)]),
    ],
  });
  const toasts = TestBed.inject(TmToasts);
  return { toasts, render: () => TestBed.tick() };
}

/** The toast host on `<body>`. */
function hostElement(): HTMLElement {
  return document.querySelector<HTMLElement>('.tm-toast-host')!;
}

/** The titles on show, newest first. */
function titles(): string[] {
  TestBed.tick();
  return Array.from(document.querySelectorAll('.tm-toast__title')).map(
    (title) => title.textContent?.trim() ?? '',
  );
}

/** Presses a key on the focused element and returns the event. */
function pressOnFocused(key: string, init: KeyboardEventInit = {}): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init });
  (document.activeElement ?? document.body).dispatchEvent(event);
  return event;
}

/** Records how a ref's `closed` resolves, once it does. */
function track(closed: Promise<string>): { reason: string | null } {
  const result = { reason: null as string | null };
  void closed.then((reason) => (result.reason = reason));
  return result;
}

describe('TmToasts — queue and timers', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('creates one popover host on first show, with the stack named "Recent messages"', () => {
    const { toasts } = setup();
    expect(document.querySelector('.tm-toast-host')).toBeNull();
    toasts.show({ tone: 'neutral', title: 'Saved' });
    toasts.show({ tone: 'neutral', title: 'Posted' });
    TestBed.tick();
    expect(document.querySelectorAll('.tm-toast-host')).toHaveLength(1);
    expect(hostElement().getAttribute('popover')).toBe('manual');
    expect(hostElement().matches(':popover-open')).toBe(true);
    const region = hostElement().querySelector('[role="region"]')!;
    expect(region.getAttribute('aria-label')).toBe('Recent messages');
  });

  it('shows at most maxVisible toasts, newest first, and counts the rest in the pill', () => {
    const { toasts } = setup();
    for (const title of ['One', 'Two', 'Three', 'Four', 'Five']) {
      toasts.show({ tone: 'neutral', title });
    }
    expect(titles()).toEqual(['Five', 'Four', 'Three']);
    expect(hostElement().querySelector('.tm-toasts__pill')?.textContent?.trim()).toBe('2 more');
    // Plain text without a run.
    expect(hostElement().querySelector('button.tm-toasts__pill')).toBeNull();
  });

  it('honours a configured maxVisible and overflow pill', () => {
    const run = vi.fn();
    const { toasts } = setup({ maxVisible: 1, overflow: (hidden) => ({ label: `+${hidden}`, run }) });
    toasts.show({ tone: 'neutral', title: 'One' });
    toasts.show({ tone: 'neutral', title: 'Two' });
    expect(titles()).toEqual(['Two']);
    const pill = hostElement().querySelector<HTMLButtonElement>('button.tm-toasts__pill')!;
    expect(pill.textContent?.trim()).toBe('+1');
    pill.click();
    expect(run).toHaveBeenCalledTimes(1);
  });

  it('replaces an open toast with the same key in place', () => {
    const { toasts } = setup();
    const first = toasts.show({ tone: 'neutral', title: 'Uploading', key: 'job:1' });
    const firstClosed = track(first.closed);
    toasts.show({ tone: 'neutral', title: 'Other' });
    const second = toasts.show({ tone: 'success', title: 'Uploaded', key: 'job:1' });
    expect(titles()).toEqual(['Other', 'Uploaded']);
    return Promise.resolve().then(() => {
      expect(firstClosed.reason).toBe('replaced');
      // The old handle is spent; the new one controls the toast.
      first.close();
      expect(titles()).toEqual(['Other', 'Uploaded']);
      second.close();
      expect(titles()).toEqual(['Other']);
    });
  });

  it('closes a toast after six seconds by default', async () => {
    const { toasts } = setup();
    const ref = toasts.show({ tone: 'success', title: 'Saved' });
    const closed = track(ref.closed);
    vi.advanceTimersByTime(5999);
    expect(titles()).toEqual(['Saved']);
    vi.advanceTimersByTime(1);
    expect(titles()).toEqual([]);
    await Promise.resolve();
    expect(closed.reason).toBe('timeout');
  });

  it('keeps an error toast until it is closed, unless it gives a duration', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'error', title: 'Could not save' });
    toasts.show({ tone: 'error', title: 'Timed error', duration: 1000 });
    vi.advanceTimersByTime(60_000);
    expect(titles()).toEqual(['Could not save']);
  });

  it('keeps a sticky toast of any tone', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'neutral', title: 'Pinned', duration: 'sticky' });
    vi.advanceTimersByTime(60_000);
    expect(titles()).toEqual(['Pinned']);
  });

  it('runs the timers of the toasts on show only; a hidden toast keeps its time', () => {
    const { toasts } = setup({ maxVisible: 1 });
    toasts.show({ tone: 'neutral', title: 'First' });
    vi.advanceTimersByTime(1000);
    toasts.show({ tone: 'neutral', title: 'Second' });
    // First is hidden with 5000 ms left; Second runs its 6000.
    vi.advanceTimersByTime(5999);
    expect(titles()).toEqual(['Second']);
    vi.advanceTimersByTime(1);
    expect(titles()).toEqual(['First']);
    vi.advanceTimersByTime(4999);
    expect(titles()).toEqual(['First']);
    vi.advanceTimersByTime(1);
    expect(titles()).toEqual([]);
  });

  it('pauses every timer while the pointer is over the stack and resumes with the time left', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'neutral', title: 'Saved' });
    TestBed.tick();
    vi.advanceTimersByTime(2000);
    const region = hostElement().querySelector('.tm-toasts')!;
    region.dispatchEvent(new PointerEvent('pointerenter'));
    vi.advanceTimersByTime(60_000);
    expect(titles()).toEqual(['Saved']);
    region.dispatchEvent(new PointerEvent('pointerleave'));
    vi.advanceTimersByTime(3999);
    expect(titles()).toEqual(['Saved']);
    vi.advanceTimersByTime(1);
    expect(titles()).toEqual([]);
  });

  it('pauses while focus is inside the stack', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'neutral', title: 'Saved' });
    TestBed.tick();
    const outside = document.createElement('button');
    document.body.appendChild(outside);
    outside.focus();
    expect(toasts.focus()).toBe(true);
    vi.advanceTimersByTime(60_000);
    expect(titles()).toEqual(['Saved']);
    outside.focus();
    vi.advanceTimersByTime(6000);
    expect(titles()).toEqual([]);
    outside.remove();
  });

  it('pauses while the page is hidden', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'neutral', title: 'Saved' });
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    document.dispatchEvent(new Event('visibilitychange'));
    vi.advanceTimersByTime(60_000);
    expect(titles()).toEqual(['Saved']);
    visibility.mockReturnValue('visible');
    document.dispatchEvent(new Event('visibilitychange'));
    vi.advanceTimersByTime(6000);
    expect(titles()).toEqual([]);
  });

  it('restarts the timer on update', () => {
    const { toasts } = setup();
    const ref = toasts.show({ tone: 'neutral', title: 'Uploading' });
    vi.advanceTimersByTime(5000);
    ref.update({ title: 'Uploaded', duration: 3000 });
    expect(titles()).toEqual(['Uploaded']);
    vi.advanceTimersByTime(2999);
    expect(titles()).toEqual(['Uploaded']);
    vi.advanceTimersByTime(1);
    expect(titles()).toEqual([]);
  });

  it('closes another toast holding a key that an update takes', async () => {
    const { toasts } = setup();
    const other = toasts.show({ tone: 'neutral', title: 'Old', key: 'k' });
    const otherClosed = track(other.closed);
    const ref = toasts.show({ tone: 'neutral', title: 'New' });
    ref.update({ key: 'k' });
    expect(titles()).toEqual(['New']);
    await Promise.resolve();
    expect(otherClosed.reason).toBe('replaced');
  });

  it('runs the action, then closes the toast', async () => {
    const { toasts } = setup();
    const run = vi.fn();
    const ref = toasts.show({ tone: 'accent', title: 'Started', action: { label: 'Track', run } });
    const closed = track(ref.closed);
    TestBed.tick();
    hostElement().querySelector<HTMLButtonElement>('.tm-toast__action')!.click();
    expect(run).toHaveBeenCalledTimes(1);
    expect(titles()).toEqual([]);
    await Promise.resolve();
    expect(closed.reason).toBe('action');
  });

  it('reports a failing action and still closes the toast', () => {
    const handleError = vi.fn();
    vi.useFakeTimers();
    TestBed.configureTestingModule({
      providers: [provideTellmaUi(), { provide: ErrorHandler, useValue: { handleError } }],
    });
    const toasts = TestBed.inject(TmToasts);
    toasts.show({
      tone: 'accent',
      title: 'Started',
      action: {
        label: 'Track',
        run: () => {
          throw new Error('navigation failed');
        },
      },
    });
    TestBed.tick();
    hostElement().querySelector<HTMLButtonElement>('.tm-toast__action')!.click();
    expect(handleError).toHaveBeenCalledTimes(1);
    expect(titles()).toEqual([]);
  });

  it('closes with the close button as a dismissal, and clear() closes the rest through the API', async () => {
    const { toasts } = setup();
    const a = toasts.show({ tone: 'neutral', title: 'A' });
    const b = toasts.show({ tone: 'neutral', title: 'B' });
    const c = toasts.show({ tone: 'neutral', title: 'C' });
    const [aClosed, bClosed, cClosed] = [a, b, c].map((ref) => track(ref.closed));
    TestBed.tick();
    hostElement().querySelector<HTMLButtonElement>('.tm-toast__close')!.click();
    toasts.clear();
    expect(titles()).toEqual([]);
    await Promise.resolve();
    expect(cClosed.reason).toBe('dismiss');
    expect(aClosed.reason).toBe('api');
    expect(bClosed.reason).toBe('api');
    // The region hides when the stack is empty.
    expect(hostElement().querySelector<HTMLElement>('.tm-toasts')!.hidden).toBe(true);
  });

  it('announces "title. detail" politely, and error toasts assertively, after emptying the region', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'success', title: 'Saved', detail: 'Invoice 42' });
    TestBed.tick();
    const polite = hostElement().querySelector('[role="status"]')!;
    const assertive = hostElement().querySelector('[role="alert"]')!;
    expect(polite.textContent?.trim()).toBe('');
    vi.advanceTimersByTime(100);
    TestBed.tick();
    expect(polite.textContent?.trim()).toBe('Saved. Invoice 42');

    toasts.show({ tone: 'error', title: 'Could not save!', detail: 'The server is down' });
    TestBed.tick();
    vi.advanceTimersByTime(100);
    TestBed.tick();
    expect(assertive.textContent?.trim()).toBe('Could not save! The server is down');
  });

  it('reads messages that arrive together in one announcement', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'neutral', title: 'Saved' });
    toasts.show({ tone: 'neutral', title: 'Posted' });
    vi.advanceTimersByTime(100);
    TestBed.tick();
    expect(hostElement().querySelector('[role="status"]')!.textContent?.trim()).toBe('Saved Posted');
  });

  it('announces a replacement, and an update only when its text changes', () => {
    const { toasts } = setup();
    const ref = toasts.show({ tone: 'neutral', title: 'Uploading', key: 'k' });
    vi.advanceTimersByTime(100);
    const polite = () => {
      TestBed.tick();
      return hostElement().querySelector('[role="status"]')!.textContent?.trim();
    };
    expect(polite()).toBe('Uploading');
    ref.update({ duration: 9000 });
    expect(polite()).toBe('Uploading');
    toasts.show({ tone: 'neutral', title: 'Uploaded', key: 'k' });
    expect(polite()).toBe('');
    vi.advanceTimersByTime(100);
    expect(polite()).toBe('Uploaded');
  });

  it('re-raises the host when a modal opens over it', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'neutral', title: 'Saved' });
    const show = vi.spyOn(HTMLElement.prototype, 'showPopover');
    TestBed.inject(OverlayContainer).getContainerElement().appendChild(document.createElement('div'));
    return Promise.resolve().then(() => {
      expect(show).toHaveBeenCalled();
      expect(show.mock.contexts.at(-1)).toBe(hostElement());
      expect(hostElement().matches(':popover-open')).toBe(true);
    });
  });
});

describe('TmToasts — focus', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('focus() is false with no toast open', () => {
    const { toasts } = setup();
    expect(toasts.focus()).toBe(false);
  });

  it('focus() moves focus to the newest toast, and Escape closes it and returns focus', async () => {
    const { toasts } = setup();
    const older = toasts.show({ tone: 'neutral', title: 'Older' });
    const ref = toasts.show({
      tone: 'accent',
      title: 'Newest',
      action: { label: 'Track', run: () => undefined },
    });
    const closed = track(ref.closed);
    const origin = document.createElement('button');
    document.body.appendChild(origin);
    origin.focus();

    expect(toasts.focus()).toBe(true);
    expect(document.activeElement?.classList.contains('tm-toast__action')).toBe(true);
    expect(document.activeElement?.closest('.tm-toast')?.querySelector('.tm-toast__title')?.textContent).toContain('Newest');

    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    document.activeElement!.dispatchEvent(escape);
    expect(escape.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(origin);
    expect(titles()).toEqual(['Older']);
    await Promise.resolve();
    expect(closed.reason).toBe('dismiss');

    // Escape no longer belongs to the stack once focus has left it.
    const later = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    origin.dispatchEvent(later);
    expect(later.defaultPrevented).toBe(false);
    older.close();
    origin.remove();
  });

  it('keeps focus in the stack when a new toast pushes the focused one out, and lets go after', () => {
    const { toasts } = setup({ maxVisible: 1 });
    const under = vi.fn();
    const releaseUnder = tmPushEscapeDismissal(under);
    const origin = document.createElement('button');
    document.body.appendChild(origin);
    origin.focus();
    toasts.show({ tone: 'neutral', title: 'First' });
    expect(toasts.focus()).toBe(true);
    toasts.show({ tone: 'neutral', title: 'Second' });
    expect(titles()).toEqual(['Second']);
    expect(document.activeElement?.closest('.tm-toast')?.textContent).toContain('Second');

    // Escape closes the toast that now holds focus and returns focus.
    pressOnFocused('Escape');
    expect(document.activeElement).toBe(origin);
    expect(titles()).toEqual(['First']);
    // The hold is released: the timers run and Escape reaches what lies under the stack.
    vi.advanceTimersByTime(6000);
    expect(titles()).toEqual([]);
    pressOnFocused('Escape');
    expect(under).toHaveBeenCalledTimes(1);
    releaseUnder();
    origin.remove();
  });

  it('moves focus to the toast’s close button when a replacement drops the focused action', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'accent', title: 'Uploading', key: 'job', action: { label: 'Track', run: () => undefined } });
    expect(toasts.focus()).toBe(true);
    expect(document.activeElement?.classList.contains('tm-toast__action')).toBe(true);
    toasts.show({ tone: 'success', title: 'Uploaded', key: 'job' });
    TestBed.tick();
    expect(document.activeElement?.classList.contains('tm-toast__close')).toBe(true);
    expect(document.activeElement?.closest('.tm-toast')?.textContent).toContain('Uploaded');
    toasts.clear();
  });

  it('returns focus where it came from on Tab past the last button or Shift+Tab before the first', () => {
    const { toasts } = setup();
    const origin = document.createElement('button');
    document.body.appendChild(origin);
    toasts.show({ tone: 'neutral', title: 'Older' });
    toasts.show({ tone: 'accent', title: 'Newer', action: { label: 'Track', run: () => undefined } });
    TestBed.tick();
    const buttons = () => Array.from(hostElement().querySelectorAll<HTMLElement>('.tm-toasts button'));

    origin.focus();
    toasts.focus();
    buttons().at(-1)!.focus();
    expect(pressOnFocused('Tab').defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(origin);

    toasts.focus();
    expect(pressOnFocused('Tab', { shiftKey: true }).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(origin);

    // Between the buttons, Tab is the browser's.
    toasts.focus();
    expect(pressOnFocused('Tab').defaultPrevented).toBe(false);
    toasts.clear();
    origin.remove();
  });

  it('wraps Tab around the stack when there is nowhere to return to', () => {
    const { toasts } = setup();
    toasts.show({ tone: 'neutral', title: 'Older' });
    toasts.show({ tone: 'neutral', title: 'Newer' });
    TestBed.tick();
    const origin = document.createElement('button');
    document.body.appendChild(origin);
    origin.focus();
    toasts.focus();
    origin.remove();
    const buttons = Array.from(hostElement().querySelectorAll<HTMLElement>('.tm-toasts button'));
    buttons.at(-1)!.focus();
    expect(pressOnFocused('Tab').defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(buttons[0]);
    expect(pressOnFocused('Tab', { shiftKey: true }).defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(buttons.at(-1));
    toasts.clear();
  });

  it('returns focus on Escape from the pill, and then lets Escape through', () => {
    const { toasts } = setup({ maxVisible: 1, overflow: (hidden) => ({ label: `${hidden} more`, run: () => undefined }) });
    toasts.show({ tone: 'neutral', title: 'Older' });
    toasts.show({ tone: 'neutral', title: 'Newer' });
    TestBed.tick();
    const origin = document.createElement('button');
    document.body.appendChild(origin);
    origin.focus();
    hostElement().querySelector<HTMLElement>('button.tm-toasts__pill')!.focus();
    expect(pressOnFocused('Escape').defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(origin);
    expect(titles()).toEqual(['Newer']);
    expect(pressOnFocused('Escape').defaultPrevented).toBe(false);
    toasts.clear();
    origin.remove();
  });

  it('never takes focus by itself', () => {
    const { toasts } = setup();
    const origin = document.createElement('button');
    document.body.appendChild(origin);
    origin.focus();
    toasts.show({ tone: 'error', title: 'Could not save' });
    TestBed.tick();
    expect(document.activeElement).toBe(origin);
    origin.remove();
  });
});

@Component({ template: '' })
class Empty {}

describe('TmToastHarness', () => {
  it('reads and drives a toast through the document-root loader', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(Empty);
    const toasts = TestBed.inject(TmToasts);
    const run = vi.fn();
    toasts.show({
      tone: 'warning',
      title: 'Low stock',
      detail: '3 items left',
      action: { label: 'Reorder', run },
      duration: 'sticky',
    });
    toasts.show({ tone: 'success', title: 'Saved', duration: 'sticky' });
    const loader = TestbedHarnessEnvironment.documentRootLoader(fixture);
    const stack = await loader.getHarness(TmToastStackHarness);
    const [newest, older] = await stack.getToasts();
    expect(await newest.getTitle()).toBe('Saved');
    expect(await newest.getDetail()).toBeNull();
    expect(await older.getTone()).toBe('warning');
    expect(await older.getDetail()).toBe('3 items left');
    expect(await older.getActionLabel()).toBe('Reorder');
    expect(await older.getCloseLabel()).toBe('Close');
    await older.runAction();
    expect(run).toHaveBeenCalledTimes(1);
    expect(await stack.getToasts()).toHaveLength(1);
    await newest.close();
    expect(await loader.getAllHarnesses(TmToastHarness)).toHaveLength(0);
    expect(await stack.getPillText()).toBeNull();
  });
});
