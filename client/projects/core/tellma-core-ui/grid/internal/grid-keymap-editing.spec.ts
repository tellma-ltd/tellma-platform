// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmResolveEditingKey,
  tmResolvePopupKey,
  type TmGridEditingKeyContext,
  type TmGridPopupKeyContext,
} from './grid-keymap';

function key(init: KeyboardEventInit & { key: string }): KeyboardEvent {
  return new KeyboardEvent('keydown', { cancelable: true, ...init });
}

const EDIT: TmGridEditingKeyContext = { mode: 'edit', isDropdownOpen: false };
const ENTER: TmGridEditingKeyContext = { mode: 'enter', isDropdownOpen: false };
const DROPDOWN: TmGridEditingKeyContext = { mode: 'edit', isDropdownOpen: true };

describe('tmResolveEditingKey (editing table)', () => {
  it('Enter commits toward the enter-run target; Shift+Enter runs back', () => {
    expect(tmResolveEditingKey(key({ key: 'Enter' }), EDIT)).toEqual({
      kind: 'commitMove',
      target: 'enterRun',
    });
    expect(tmResolveEditingKey(key({ key: 'Enter', shiftKey: true }), ENTER)).toEqual({
      kind: 'commitMove',
      target: 'enterRunBack',
    });
  });

  it('Tab / Shift+Tab commit toward the tab targets in both modes', () => {
    for (const ctx of [EDIT, ENTER]) {
      expect(tmResolveEditingKey(key({ key: 'Tab' }), ctx)).toEqual({
        kind: 'commitMove',
        target: 'tabNext',
      });
      expect(tmResolveEditingKey(key({ key: 'Tab', shiftKey: true }), ctx)).toEqual({
        kind: 'commitMove',
        target: 'tabPrev',
      });
    }
  });

  it('Esc cancels; F2 toggles the mode', () => {
    expect(tmResolveEditingKey(key({ key: 'Escape' }), EDIT)).toEqual({ kind: 'cancel' });
    expect(tmResolveEditingKey(key({ key: 'F2' }), ENTER)).toEqual({ kind: 'toggleMode' });
  });

  it('vertical arrows and paging commit-and-move in BOTH modes', () => {
    for (const ctx of [EDIT, ENTER]) {
      expect(tmResolveEditingKey(key({ key: 'ArrowUp' }), ctx)).toEqual({
        kind: 'commitMove',
        target: 'up',
      });
      expect(tmResolveEditingKey(key({ key: 'ArrowDown' }), ctx)).toEqual({
        kind: 'commitMove',
        target: 'down',
      });
      expect(tmResolveEditingKey(key({ key: 'PageUp' }), ctx)).toEqual({
        kind: 'commitMove',
        target: 'pageUp',
      });
      expect(tmResolveEditingKey(key({ key: 'PageDown' }), ctx)).toEqual({
        kind: 'commitMove',
        target: 'pageDown',
      });
    }
  });

  it('horizontal arrows commit-and-move in ENTER mode only (PHYSICAL motions)', () => {
    expect(tmResolveEditingKey(key({ key: 'ArrowLeft' }), ENTER)).toEqual({
      kind: 'commitMove',
      target: 'left',
    });
    expect(tmResolveEditingKey(key({ key: 'ArrowRight' }), ENTER)).toEqual({
      kind: 'commitMove',
      target: 'right',
    });
    // Edit mode leaves them to the caret.
    expect(tmResolveEditingKey(key({ key: 'ArrowLeft' }), EDIT)).toBeNull();
    expect(tmResolveEditingKey(key({ key: 'ArrowRight' }), EDIT)).toBeNull();
  });

  it('Alt+ArrowDown opens the dropdown; other modified arrows stay with the editor', () => {
    expect(tmResolveEditingKey(key({ key: 'ArrowDown', altKey: true }), EDIT)).toEqual({
      kind: 'openDropdown',
    });
    expect(tmResolveEditingKey(key({ key: 'ArrowLeft', ctrlKey: true }), ENTER)).toBeNull();
    expect(tmResolveEditingKey(key({ key: 'ArrowRight', metaKey: true }), ENTER)).toBeNull();
    expect(tmResolveEditingKey(key({ key: 'ArrowUp', altKey: true }), ENTER)).toBeNull();
    expect(
      tmResolveEditingKey(key({ key: 'ArrowDown', altKey: true, ctrlKey: true }), EDIT),
    ).toBeNull();
  });

  it('an open dropdown owns every key except F2', () => {
    for (const k of [
      'Enter',
      'Escape',
      'Tab',
      'ArrowUp',
      'ArrowDown',
      'ArrowLeft',
      'ArrowRight',
      'PageUp',
      'PageDown',
      ' ',
    ]) {
      expect(tmResolveEditingKey(key({ key: k }), DROPDOWN)).toBeNull();
    }
    expect(tmResolveEditingKey(key({ key: 'F2' }), DROPDOWN)).toEqual({ kind: 'toggleMode' });
  });

  it('plain characters and unrelated keys stay with the editor', () => {
    for (const ctx of [EDIT, ENTER]) {
      expect(tmResolveEditingKey(key({ key: 'a' }), ctx)).toBeNull();
      expect(tmResolveEditingKey(key({ key: ' ' }), ctx)).toBeNull();
      expect(tmResolveEditingKey(key({ key: 'Home' }), ctx)).toBeNull();
      expect(tmResolveEditingKey(key({ key: 'End' }), ctx)).toBeNull();
      expect(tmResolveEditingKey(key({ key: 'Backspace' }), ctx)).toBeNull();
      expect(tmResolveEditingKey(key({ key: 'Delete' }), ctx)).toBeNull();
      expect(tmResolveEditingKey(key({ key: 'F10', shiftKey: true }), ctx)).toBeNull();
    }
  });
});

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
