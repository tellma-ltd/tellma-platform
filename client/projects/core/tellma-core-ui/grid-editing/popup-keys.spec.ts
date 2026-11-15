// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmPopupKeepsEnter,
  tmPopupTabLeaves,
  tmResolvePopupKey,
  type TmGridPopupKeyContext,
} from './popup-keys';

function key(init: KeyboardEventInit & { key: string }): KeyboardEvent {
  return new KeyboardEvent('keydown', { cancelable: true, ...init });
}

describe('tmResolvePopupKey (popup pane table)', () => {
  const OFF_APPLE: TmGridPopupKeyContext = { apple: false, isDropdownOpen: false };
  const ON_APPLE: TmGridPopupKeyContext = { apple: true, isDropdownOpen: false };

  it('leaves the arrows, paging, Home, End and F2 to the editor', () => {
    for (const name of ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'PageUp', 'PageDown', 'Home', 'End', 'F2']) {
      expect(tmResolvePopupKey(key({ key: name }), OFF_APPLE), name).toBeNull();
    }
  });

  it('cancels on Escape and commits in place on Mod+Enter, per platform', () => {
    expect(tmResolvePopupKey(key({ key: 'Escape' }), OFF_APPLE)).toEqual({ kind: 'cancel' });
    expect(tmResolvePopupKey(key({ key: 'Enter', ctrlKey: true }), OFF_APPLE)).toEqual({
      kind: 'commit',
    });
    expect(tmResolvePopupKey(key({ key: 'Enter', metaKey: true }), ON_APPLE)).toEqual({
      kind: 'commit',
    });
    // The other control key is no modifier the pane gives a meaning.
    expect(tmResolvePopupKey(key({ key: 'Enter', ctrlKey: true }), ON_APPLE)).toBeNull();
  });

  it('resolves Enter and Tab with their direction, for the core to judge', () => {
    expect(tmResolvePopupKey(key({ key: 'Enter' }), OFF_APPLE)).toEqual({
      kind: 'enter',
      backward: false,
    });
    expect(tmResolvePopupKey(key({ key: 'Enter', shiftKey: true }), OFF_APPLE)).toEqual({
      kind: 'enter',
      backward: true,
    });
    expect(tmResolvePopupKey(key({ key: 'Tab', shiftKey: true }), OFF_APPLE)).toEqual({
      kind: 'tab',
      backward: true,
    });
  });

  it('opens the dropdown on Alt+ArrowDown and yields every key to an open one', () => {
    expect(tmResolvePopupKey(key({ key: 'ArrowDown', altKey: true }), OFF_APPLE)).toEqual({
      kind: 'openDropdown',
    });
    const open: TmGridPopupKeyContext = { apple: false, isDropdownOpen: true };
    for (const name of ['Escape', 'Enter', 'Tab']) {
      expect(tmResolvePopupKey(key({ key: name }), open), name).toBeNull();
    }
  });
});

// Section: the DOM tests on a pane — whether Enter stays with its content
// and whether a Tab leaves it.

describe('tmPopupKeepsEnter', () => {
  afterEach(() => document.body.replaceChildren());

  it('keeps Enter in editable text and controls that own it, not in a plain input', () => {
    document.body.innerHTML = `
      <div data-tm-popup>
        <input id="plain" />
        <textarea id="lines"></textarea>
        <textarea id="fixed" readonly></textarea>
        <button id="press">Go</button>
        <div id="rich" contenteditable="true"></div>
        <span role="option" id="option"><b id="inner">A</b></span>
      </div>`;
    const by = (id: string): Element => document.getElementById(id)!;
    expect(tmPopupKeepsEnter(by('plain'))).toBe(false);
    expect(tmPopupKeepsEnter(by('lines'))).toBe(true);
    expect(tmPopupKeepsEnter(by('fixed'))).toBe(false);
    expect(tmPopupKeepsEnter(by('press'))).toBe(true);
    expect(tmPopupKeepsEnter(by('rich'))).toBe(true);
    expect(tmPopupKeepsEnter(by('inner'))).toBe(true);
  });
});

describe('tmPopupTabLeaves', () => {
  afterEach(() => document.body.replaceChildren());

  it('leaves from the last stop forward and the first backward, and stays between', () => {
    document.body.innerHTML = `
      <div data-tm-popup>
        <input id="first" />
        <input id="skipped" disabled />
        <input id="last" />
      </div>`;
    const inPane = (): boolean => true;
    const first = document.getElementById('first')!;
    const last = document.getElementById('last')!;
    expect(tmPopupTabLeaves(first, false, inPane)).toBe(false);
    expect(tmPopupTabLeaves(first, true, inPane)).toBe(true);
    expect(tmPopupTabLeaves(last, false, inPane)).toBe(true);
    expect(tmPopupTabLeaves(last, true, inPane)).toBe(false);
  });

  it('leaves from outside any pane and from what is not one of its stops', () => {
    document.body.innerHTML = `
      <input id="outside" />
      <div data-tm-popup>
        <input id="first" />
        <input id="nested" />
        <input id="last" />
      </div>`;
    const nested = document.getElementById('nested')!;
    // The middle input stands for a control in an overlay nested in the pane.
    const inPane = (element: Element): boolean => element !== nested;
    expect(tmPopupTabLeaves(document.getElementById('outside')!, false, inPane)).toBe(true);
    expect(tmPopupTabLeaves(nested, false, inPane)).toBe(true);
    expect(tmPopupTabLeaves(document.getElementById('first')!, false, inPane)).toBe(false);
  });
});
