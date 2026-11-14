// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';

import { TmSearch } from './tm-search';

/** A bare search input that records every emitted query with its time. */
@Component({
  imports: [TmSearch],
  template: `
    <input
      tmSearch
      #search="tmSearch"
      [(value)]="value"
      [debounce]="debounce()"
      [escapeClears]="escapeClears()"
      (search)="emitted.push({ query: $event, at: now() })"
    />
  `,
})
class Host {
  readonly value = signal('');
  readonly debounce = signal(50);
  readonly escapeClears = signal(true);
  readonly emitted: { readonly query: string; readonly at: number }[] = [];
  readonly start = Date.now();
  /** Milliseconds since the host was created, on the (fake) clock. */
  now(): number {
    return Date.now() - this.start;
  }
}

/** Creates the host under fake timers and returns its input. */
function setup(configure?: (host: Host) => void) {
  vi.useFakeTimers();
  TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
  const fixture = TestBed.createComponent(Host);
  configure?.(fixture.componentInstance);
  fixture.detectChanges();
  const input = fixture.nativeElement.querySelector('input') as HTMLInputElement;
  const host = fixture.componentInstance;
  /** Types one character the way the engine reports a keystroke. */
  const type = (char: string, init: KeyboardEventInit = {}): void => {
    input.dispatchEvent(new KeyboardEvent('keydown', { key: char, bubbles: true, ...init }));
    input.value += char;
    input.dispatchEvent(new InputEvent('input', { bubbles: true, data: char }));
  };
  const keyup = (key: string): void => {
    input.dispatchEvent(new KeyboardEvent('keyup', { key, bubbles: true }));
  };
  const queries = () => host.emitted.map((entry) => entry.query);
  return { fixture, host, input, type, keyup, queries };
}

describe('TmSearch timing', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('emits the first input at once and the burst once more after the window', () => {
    // "jed" typed 30 ms per key: "j" leads at 0 ms, "jed" trails at 110 ms.
    const { host, type } = setup();
    type('j');
    vi.advanceTimersByTime(30);
    type('e');
    vi.advanceTimersByTime(30);
    type('d');
    expect(host.emitted).toEqual([{ query: 'j', at: 0 }]);
    vi.advanceTimersByTime(49);
    expect(host.emitted).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(host.emitted).toEqual([
      { query: 'j', at: 0 },
      { query: 'jed', at: 110 },
    ]);
  });

  it('updates the value on every keystroke', () => {
    const { host, type } = setup();
    type('a');
    expect(host.value()).toBe('a');
    type('b');
    expect(host.value()).toBe('ab');
  });

  it('leads again after a pause of at least the window', () => {
    const { queries, type } = setup();
    type('a');
    vi.advanceTimersByTime(50);
    type('b');
    expect(queries()).toEqual(['a', 'ab']);
    vi.advanceTimersByTime(50);
    expect(queries()).toEqual(['a', 'ab']);
  });

  it('never emits the value it emitted last', () => {
    const { input, queries, type } = setup();
    type('a');
    vi.advanceTimersByTime(10);
    // Type and delete inside the window: the burst ends where it started.
    type('b');
    input.value = 'a';
    input.dispatchEvent(new InputEvent('input', { bubbles: true }));
    vi.advanceTimersByTime(50);
    expect(queries()).toEqual(['a']);
  });

  it('treats the initial value as already searched', () => {
    const { input, queries } = setup((host) => host.value.set('jed'));
    expect(input.value).toBe('jed');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(queries()).toEqual([]);
  });

  it('emits at once on Enter and closes the burst', () => {
    const { input, queries, type } = setup();
    type('a');
    type('b');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(queries()).toEqual(['a', 'ab']);
    // The burst is closed: the next key leads at once, with no timer run.
    type('c');
    expect(queries()).toEqual(['a', 'ab', 'abc']);
    vi.advanceTimersByTime(100);
    expect(queries()).toEqual(['a', 'ab', 'abc']);
  });

  it('emits the empty query at once on a clear inside a burst, and drops the burst’s trailing edge', () => {
    // A clear supersedes the text it removes: the window still open on "ab"
    // must not search for it afterwards.
    const { fixture, queries, type } = setup();
    const search = fixture.debugElement.children[0].references['search'] as TmSearch;
    type('a');
    type('b');
    search.clear();
    expect(queries()).toEqual(['a', '']);
    // The burst is closed: the next key leads at once, with no timer run.
    type('c');
    expect(queries()).toEqual(['a', '', 'c']);
    vi.advanceTimersByTime(100);
    expect(queries()).toEqual(['a', '', 'c']);
  });

  it('holds a repeating key until it is released', () => {
    const { keyup, queries, type } = setup();
    type('a');
    vi.advanceTimersByTime(500);
    type('a', { repeat: true });
    vi.advanceTimersByTime(30);
    type('a', { repeat: true });
    vi.advanceTimersByTime(30);
    type('a', { repeat: true });
    vi.advanceTimersByTime(200);
    expect(queries()).toEqual(['a']);
    keyup('a');
    expect(queries()).toEqual(['a', 'aaaa']);
    vi.advanceTimersByTime(100);
    expect(queries()).toEqual(['a', 'aaaa']);
  });

  it('holds the first keystroke of a repeat burst behind the release too', () => {
    const { keyup, queries, type } = setup();
    type('x');
    type('x', { repeat: true });
    vi.advanceTimersByTime(100);
    // The leading edge went out; the hold suppressed the trailing one.
    expect(queries()).toEqual(['x']);
    keyup('x');
    expect(queries()).toEqual(['x', 'xx']);
  });

  it('emits nothing during an IME composition and the composed text after it', () => {
    const { host, input, queries } = setup();
    input.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
    input.value = 'ka';
    input.dispatchEvent(new InputEvent('input', { bubbles: true, isComposing: true }));
    input.value = 'か';
    input.dispatchEvent(new InputEvent('input', { bubbles: true, isComposing: true }));
    // Enter confirms the composition: it belongs to the IME, not the search.
    input.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', isComposing: true, bubbles: true }),
    );
    vi.advanceTimersByTime(200);
    expect(queries()).toEqual([]);
    expect(host.value()).toBe('か');
    input.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true, data: 'か' }));
    expect(queries()).toEqual(['か']);
  });

  it('emits on every input with a zero window', () => {
    const { host, queries, type } = setup((created) => created.debounce.set(0));
    type('a');
    type('b');
    type('c');
    expect(queries()).toEqual(['a', 'ab', 'abc']);
    expect(host.emitted.every((entry) => entry.at === 0)).toBe(true);
  });

  it('flushes on demand, only when the text changed', () => {
    const { fixture, queries, type } = setup();
    const search = fixture.debugElement.children[0].references['search'] as TmSearch;
    type('a');
    type('b');
    search.flush();
    search.flush();
    expect(queries()).toEqual(['a', 'ab']);
  });

  it('clears undoably on Escape, emits the empty query at once, and stops the key there', () => {
    const { fixture, host, input, queries, type } = setup();
    let reachedParent: boolean;
    fixture.nativeElement.addEventListener('keydown', () => (reachedParent = true));
    input.focus();
    type('a');
    vi.advanceTimersByTime(100);
    // Typing reached the parent; only the Escape that follows is under test.
    reachedParent = false;
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    input.dispatchEvent(escape);
    expect(input.value).toBe('');
    expect(host.value()).toBe('');
    expect(queries()).toEqual(['a', '']);
    expect(escape.defaultPrevented).toBe(true);
    expect(reachedParent).toBe(false);
    // The clear went through the editing history: undo brings the text back.
    expect(document.execCommand('undo')).toBe(true);
    expect(input.value).toBe('a');
    expect(host.value()).toBe('a');
  });

  it('lets Escape through on empty text, and always when Escape does not clear', () => {
    const { fixture, host, input, type } = setup((created) => created.escapeClears.set(false));
    let reachedParent = 0;
    fixture.nativeElement.addEventListener('keydown', () => reachedParent++);
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    type('a');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(host.value()).toBe('a');
    // Two Escapes and the keystroke itself.
    expect(reachedParent).toBe(3);
  });

  it('carries the search input attributes', () => {
    const { input } = setup();
    expect(input.type).toBe('search');
    expect(input.getAttribute('dir')).toBe('auto');
    expect(input.getAttribute('enterkeyhint')).toBe('search');
  });

  it('keeps the engine’s own search event from reaching the output listeners', () => {
    const { input, queries } = setup();
    input.dispatchEvent(new Event('search'));
    expect(queries()).toEqual([]);
  });
});
