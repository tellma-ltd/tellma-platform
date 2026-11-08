// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  tmAriaKeyShortcuts,
  tmFormatKeys,
  tmIsApplePlatform,
  tmKeysMatch,
  tmKeysText,
} from './tm-keys';

/** A real keydown, as a layout and the OS would produce it. */
function keydown(init: {
  key: string;
  code: string;
  ctrl?: boolean;
  alt?: boolean;
  shift?: boolean;
  meta?: boolean;
  altGraph?: boolean;
}): KeyboardEvent {
  return new KeyboardEvent('keydown', {
    key: init.key,
    code: init.code,
    ctrlKey: init.ctrl ?? false,
    altKey: init.alt ?? false,
    shiftKey: init.shift ?? false,
    metaKey: init.meta ?? false,
    modifierAltGraph: init.altGraph ?? false,
  });
}

const WINDOWS = false;
const APPLE = true;

describe('tmFormatKeys and tmKeysText', () => {
  it('label Mod as Ctrl elsewhere and ⌘ on Apple platforms', () => {
    expect(tmFormatKeys('Mod+Shift+K', WINDOWS)).toEqual(['Ctrl', 'Shift', 'K']);
    expect(tmFormatKeys('Mod+Shift+K', APPLE)).toEqual(['⇧', '⌘', 'K']);
    expect(tmKeysText('Mod+V', WINDOWS)).toBe('Ctrl+V');
    expect(tmKeysText('Mod+V', APPLE)).toBe('⌘V');
  });

  it('order modifiers Ctrl, Alt, Shift, Meta — ⌃ ⌥ ⇧ ⌘ on Apple — whatever the written order', () => {
    expect(tmFormatKeys('Meta+Shift+Alt+Ctrl+X', WINDOWS)).toEqual([
      'Ctrl',
      'Alt',
      'Shift',
      'Meta',
      'X',
    ]);
    expect(tmFormatKeys('Meta+Shift+Alt+Ctrl+X', APPLE)).toEqual(['⌃', '⌥', '⇧', '⌘', 'X']);
    expect(tmKeysText('Alt+Shift+P', APPLE)).toBe('⌥⇧P');
    expect(tmKeysText('Alt+Shift+P', WINDOWS)).toBe('Alt+Shift+P');
  });

  it('fold Mod into Ctrl or Meta without a duplicate cap', () => {
    expect(tmFormatKeys('Mod+Ctrl+K', WINDOWS)).toEqual(['Ctrl', 'K']);
    expect(tmFormatKeys('Mod+Ctrl+K', APPLE)).toEqual(['⌃', '⌘', 'K']);
  });

  it('spell the key words and named keys in Latin on both platforms', () => {
    for (const apple of [WINDOWS, APPLE]) {
      expect(tmFormatKeys('Plus', apple)).toEqual(['+']);
      expect(tmFormatKeys('Minus', apple)).toEqual(['-']);
      expect(tmFormatKeys('Space', apple)).toEqual(['Space']);
      expect(tmFormatKeys('Escape', apple)).toEqual(['Esc']);
      expect(tmFormatKeys('escape', apple)).toEqual(['Esc']);
      expect(tmFormatKeys('ArrowUp', apple)).toEqual(['↑']);
      expect(tmFormatKeys('F2', apple)).toEqual(['F2']);
      expect(tmFormatKeys('PageUp', apple)).toEqual(['PageUp']);
      expect(tmFormatKeys('?', apple)).toEqual(['?']);
      expect(tmFormatKeys('mod+k', apple)).toEqual(apple ? ['⌘', 'K'] : ['Ctrl', 'K']);
    }
    expect(tmKeysText('Mod+Plus', WINDOWS)).toBe('Ctrl++');
    expect(tmKeysText('Mod+Plus', APPLE)).toBe('⌘+');
    expect(tmKeysText('Mod+Enter', APPLE)).toBe('⌘Enter');
  });

  it('refuse malformed combinations', () => {
    for (const keys of ['', 'Mod+', 'Mod++', 'Mod+Shift', 'Hyper+K', 'Shift+Shift+K', '+']) {
      expect(() => tmFormatKeys(keys, WINDOWS), keys).toThrow(/tmKeys/);
    }
    expect(() => tmFormatKeys('Mod++', WINDOWS)).toThrow(/'Plus'/);
  });
});

describe('tmAriaKeyShortcuts', () => {
  it('writes the aria-keyshortcuts syntax for each platform', () => {
    expect(tmAriaKeyShortcuts('Mod+K', WINDOWS)).toBe('Control+K');
    expect(tmAriaKeyShortcuts('Mod+K', APPLE)).toBe('Meta+K');
    expect(tmAriaKeyShortcuts('Alt+Shift+P', WINDOWS)).toBe('Alt+Shift+P');
    expect(tmAriaKeyShortcuts('Mod+Alt+Plus', WINDOWS)).toBe('Control+Alt+Plus');
    expect(tmAriaKeyShortcuts('Shift+Space', APPLE)).toBe('Shift+Space');
    expect(tmAriaKeyShortcuts('Escape', WINDOWS)).toBe('Escape');
  });
});

describe('tmKeysMatch', () => {
  it('builds events whose AltGraph state is real', () => {
    expect(keydown({ key: 'ą', code: 'KeyA', altGraph: true }).getModifierState('AltGraph')).toBe(
      true,
    );
  });

  describe('modifiers match exactly', () => {
    it('Mod is Ctrl elsewhere and Meta on Apple platforms', () => {
      expect(tmKeysMatch(keydown({ key: 'k', code: 'KeyK', ctrl: true }), 'Mod+K', WINDOWS)).toBe(
        true,
      );
      expect(tmKeysMatch(keydown({ key: 'k', code: 'KeyK', meta: true }), 'Mod+K', WINDOWS)).toBe(
        false,
      );
      expect(tmKeysMatch(keydown({ key: 'k', code: 'KeyK', meta: true }), 'Mod+K', APPLE)).toBe(
        true,
      );
      expect(tmKeysMatch(keydown({ key: 'k', code: 'KeyK', ctrl: true }), 'Mod+K', APPLE)).toBe(
        false,
      );
      expect(tmKeysMatch(keydown({ key: 'k', code: 'KeyK', ctrl: true }), 'Ctrl+K', APPLE)).toBe(
        true,
      );
    });

    it('an extra or a missing modifier fails', () => {
      expect(
        tmKeysMatch(keydown({ key: 'K', code: 'KeyK', ctrl: true, shift: true }), 'Mod+K', WINDOWS),
      ).toBe(false);
      expect(
        tmKeysMatch(keydown({ key: 'k', code: 'KeyK', ctrl: true, alt: true }), 'Mod+K', WINDOWS),
      ).toBe(false);
      expect(tmKeysMatch(keydown({ key: 'k', code: 'KeyK' }), 'Mod+K', WINDOWS)).toBe(false);
      expect(
        tmKeysMatch(keydown({ key: 'k', code: 'KeyK', ctrl: true }), 'Mod+Shift+K', WINDOWS),
      ).toBe(false);
    });

    it('Shift is not compared beside a printable symbol', () => {
      // '?' is Shift+/ on a US layout.
      expect(tmKeysMatch(keydown({ key: '?', code: 'Slash', shift: true }), '?', WINDOWS)).toBe(
        true,
      );
      expect(tmKeysMatch(keydown({ key: '/', code: 'Slash' }), '/', WINDOWS)).toBe(true);
      // '!' is not US punctuation from the table, but is a symbol all the same.
      expect(tmKeysMatch(keydown({ key: '!', code: 'Digit1', shift: true }), '!', WINDOWS)).toBe(
        true,
      );
      // Shift is compared beside letters and space.
      expect(tmKeysMatch(keydown({ key: ' ', code: 'Space', shift: true }), 'Space', WINDOWS)).toBe(
        false,
      );
      expect(
        tmKeysMatch(keydown({ key: ' ', code: 'Space', shift: true }), 'Shift+Space', WINDOWS),
      ).toBe(true);
    });
  });

  describe('keys compare event.key, case-insensitively', () => {
    it('letters with Caps Lock, named keys and the key words', () => {
      expect(tmKeysMatch(keydown({ key: 'K', code: 'KeyK', ctrl: true }), 'Mod+k', WINDOWS)).toBe(
        true,
      );
      expect(tmKeysMatch(keydown({ key: 'F2', code: 'F2' }), 'F2', WINDOWS)).toBe(true);
      expect(tmKeysMatch(keydown({ key: 'Escape', code: 'Escape' }), 'escape', WINDOWS)).toBe(true);
      expect(
        tmKeysMatch(keydown({ key: 'PageUp', code: 'PageUp', alt: true }), 'Alt+PageUp', WINDOWS),
      ).toBe(true);
      expect(tmKeysMatch(keydown({ key: ' ', code: 'Space' }), 'Space', WINDOWS)).toBe(true);
      expect(
        tmKeysMatch(keydown({ key: '-', code: 'Minus', ctrl: true }), 'Mod+Minus', WINDOWS),
      ).toBe(true);
    });

    it('a Latin layout matches by the typed character, not the key position', () => {
      // AZERTY: the KeyQ position types "a".
      expect(tmKeysMatch(keydown({ key: 'a', code: 'KeyQ', ctrl: true }), 'Mod+A', WINDOWS)).toBe(
        true,
      );
      expect(tmKeysMatch(keydown({ key: 'a', code: 'KeyQ', ctrl: true }), 'Mod+Q', WINDOWS)).toBe(
        false,
      );
    });
  });

  describe('on an Arabic layout, letters, digits and punctuation match by event.code', () => {
    it('Mod+D from "ي" on the D key', () => {
      const event = keydown({ key: 'ي', code: 'KeyD', ctrl: true });
      expect(tmKeysMatch(event, 'Mod+D', WINDOWS)).toBe(true);
      expect(tmKeysMatch(keydown({ key: 'ي', code: 'KeyD', meta: true }), 'Mod+D', APPLE)).toBe(
        true,
      );
      expect(tmKeysMatch(event, 'Mod+F', WINDOWS)).toBe(false);
    });

    it('Mod+Shift+D from "]" on the D key', () => {
      const event = keydown({ key: ']', code: 'KeyD', ctrl: true, shift: true });
      expect(tmKeysMatch(event, 'Mod+Shift+D', WINDOWS)).toBe(true);
      expect(tmKeysMatch(event, 'Mod+D', WINDOWS)).toBe(false);
    });

    it('Mod+/ from "ظ" on the Slash key', () => {
      expect(tmKeysMatch(keydown({ key: 'ظ', code: 'Slash', ctrl: true }), 'Mod+/', WINDOWS)).toBe(
        true,
      );
    });

    it('a punctuation code reads as its US shifted symbol with Shift held', () => {
      const shifted = keydown({ key: '؟', code: 'Slash', shift: true });
      expect(tmKeysMatch(shifted, '?', WINDOWS)).toBe(true);
      expect(tmKeysMatch(shifted, '/', WINDOWS)).toBe(false);
    });

    it('digits match by the main-row code, never the numpad', () => {
      expect(
        tmKeysMatch(
          keydown({ key: '!', code: 'Digit1', ctrl: true, shift: true }),
          'Mod+Shift+1',
          WINDOWS,
        ),
      ).toBe(true);
      expect(tmKeysMatch(keydown({ key: '١', code: 'Digit1', ctrl: true }), 'Mod+1', WINDOWS)).toBe(
        true,
      );
      // Num Lock off: the numpad 1 is End.
      expect(
        tmKeysMatch(keydown({ key: 'End', code: 'Numpad1', ctrl: true }), 'Mod+1', WINDOWS),
      ).toBe(false);
    });

    it('a punctuation binding falls back only for one non-ASCII character', () => {
      // An ASCII character that differs from the binding is a different key.
      expect(tmKeysMatch(keydown({ key: ']', code: 'Slash', ctrl: true }), 'Mod+/', WINDOWS)).toBe(
        false,
      );
      expect(
        tmKeysMatch(keydown({ key: 'Dead', code: 'Slash', ctrl: true }), 'Mod+/', WINDOWS),
      ).toBe(false);
    });
  });

  describe('Option on Apple platforms', () => {
    it('matches by event.code while Option is held, whatever it typed', () => {
      expect(
        tmKeysMatch(keydown({ key: 'π', code: 'KeyP', alt: true, meta: true }), 'Mod+Alt+P', APPLE),
      ).toBe(true);
      // Even an ASCII letter typed by an Option layer.
      const asciiFromOption = keydown({ key: 'x', code: 'KeyK', alt: true });
      expect(tmKeysMatch(asciiFromOption, 'Alt+K', APPLE)).toBe(true);
      expect(tmKeysMatch(asciiFromOption, 'Alt+K', WINDOWS)).toBe(false);
    });

    it('is not cancelled by an AltGraph state an engine may report for Option', () => {
      const event = keydown({ key: 'å', code: 'KeyA', alt: true, meta: true, altGraph: true });
      expect(tmKeysMatch(event, 'Mod+Alt+A', APPLE)).toBe(true);
    });
  });

  describe('AltGr types characters', () => {
    it('Polish AltGr+A ("ą", reported as Ctrl+Alt) does not match Mod+Alt+A', () => {
      const altGr = keydown({ key: 'ą', code: 'KeyA', ctrl: true, alt: true, altGraph: true });
      expect(tmKeysMatch(altGr, 'Mod+Alt+A', WINDOWS)).toBe(false);
    });

    it('a real Ctrl+Alt+A matches, on a Latin and on a non-Latin layout', () => {
      expect(
        tmKeysMatch(
          keydown({ key: 'a', code: 'KeyA', ctrl: true, alt: true }),
          'Mod+Alt+A',
          WINDOWS,
        ),
      ).toBe(true);
      expect(
        tmKeysMatch(
          keydown({ key: 'ش', code: 'KeyA', ctrl: true, alt: true }),
          'Mod+Alt+A',
          WINDOWS,
        ),
      ).toBe(true);
    });
  });

  describe('Mod+Alt+Plus', () => {
    it('matches the numpad + and Shift+= on the main keys', () => {
      const numpad = keydown({ key: '+', code: 'NumpadAdd', ctrl: true, alt: true });
      const main = keydown({ key: '+', code: 'Equal', ctrl: true, alt: true, shift: true });
      expect(tmKeysMatch(numpad, 'Mod+Alt+Plus', WINDOWS)).toBe(true);
      expect(tmKeysMatch(main, 'Mod+Alt+Plus', WINDOWS)).toBe(true);
    });

    it('does not match an unshifted = (that is Mod+Alt+=)', () => {
      const equal = keydown({ key: '=', code: 'Equal', ctrl: true, alt: true });
      expect(tmKeysMatch(equal, 'Mod+Alt+Plus', WINDOWS)).toBe(false);
      expect(tmKeysMatch(equal, 'Mod+Alt+=', WINDOWS)).toBe(true);
    });

    it('matches on Apple platforms, where Option+Shift+= types "±"', () => {
      const numpad = keydown({ key: '+', code: 'NumpadAdd', meta: true, alt: true });
      const main = keydown({ key: '±', code: 'Equal', meta: true, alt: true, shift: true });
      expect(tmKeysMatch(numpad, 'Mod+Alt+Plus', APPLE)).toBe(true);
      expect(tmKeysMatch(main, 'Mod+Alt+Plus', APPLE)).toBe(true);
    });
  });

  it('refuses a malformed combination', () => {
    expect(() => tmKeysMatch(keydown({ key: 'k', code: 'KeyK' }), 'Mod+', WINDOWS)).toThrow(
      /tmKeys/,
    );
  });
});

describe('tmIsApplePlatform', () => {
  // Own properties shadow Navigator.prototype's getters; deleting them
  // restores the real ones.
  function stub(platform: string, uaPlatform: string | undefined): void {
    Object.defineProperty(navigator, 'platform', { value: platform, configurable: true });
    Object.defineProperty(navigator, 'userAgentData', {
      value: uaPlatform === undefined ? undefined : { platform: uaPlatform },
      configurable: true,
    });
  }

  afterEach(() => {
    delete (navigator as unknown as Record<string, unknown>)['platform'];
    delete (navigator as unknown as Record<string, unknown>)['userAgentData'];
  });

  it('reads userAgentData.platform where the browser offers it', () => {
    stub('Win32', 'macOS');
    expect(tmIsApplePlatform()).toBe(true);
    stub('MacIntel', 'Windows');
    expect(tmIsApplePlatform()).toBe(false);
    stub('Linux x86_64', 'iOS');
    expect(tmIsApplePlatform()).toBe(true);
  });

  it('falls back to navigator.platform', () => {
    for (const [platform, apple] of [
      ['MacIntel', true],
      ['iPhone', true],
      ['iPad', true],
      ['Win32', false],
      ['Linux x86_64', false],
    ] as const) {
      stub(platform, undefined);
      expect(tmIsApplePlatform(), platform).toBe(apple);
    }
    stub('MacIntel', '');
    expect(tmIsApplePlatform()).toBe(true);
  });

  it('is the default platform of every helper', () => {
    stub('MacIntel', undefined);
    expect(tmKeysText('Mod+V')).toBe('⌘V');
    expect(tmAriaKeyShortcuts('Mod+K')).toBe('Meta+K');
    expect(tmKeysMatch(keydown({ key: 'v', code: 'KeyV', meta: true }), 'Mod+V')).toBe(true);
    stub('Win32', 'Windows');
    expect(tmKeysText('Mod+V')).toBe('Ctrl+V');
  });
});
