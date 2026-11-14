// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TM_UI_STRINGS_EN } from '@tellma/core-ui';
import { tmKeysMatch } from '@tellma/core-ui/kbd';

import { TM_GRID_KEYS } from '../tm-grid-keys';
import {
  TM_GRID_KEY_BINDINGS,
  tmResolveGridKey,
  tmResolveHeaderKey,
  type TmGridIntent,
  type TmGridKeyBinding,
  type TmGridKeyCondition,
  type TmGridKeyContext,
} from './grid-keymap';

// Section: synthetic keydowns.

/** The modifiers of one synthetic keydown. */
interface Modifiers {
  readonly ctrl: boolean;
  readonly alt: boolean;
  readonly shift: boolean;
  readonly meta: boolean;
  /** Whether AltGraph is down (AltGr on Windows and Linux layouts). */
  readonly altGraph?: boolean;
}

/** A physical key: its code and what it types, unshifted and shifted. */
interface PhysicalKey {
  readonly code: string;
  readonly key: string;
  readonly shifted: string;
}

/** A keydown as a layout and the OS would produce it. */
function keydown(physical: PhysicalKey, modifiers: Modifiers, typed?: string): KeyboardEvent {
  return new KeyboardEvent('keydown', {
    key: typed ?? (modifiers.shift ? physical.shifted : physical.key),
    code: physical.code,
    ctrlKey: modifiers.ctrl,
    altKey: modifiers.alt,
    shiftKey: modifiers.shift,
    metaKey: modifiers.meta,
    modifierAltGraph: modifiers.altGraph ?? false,
    cancelable: true,
  });
}

/** The US-layout key a binding's key part names, as the keyboard sends it. */
function physicalKey(written: string): PhysicalKey {
  if (/^[A-Z]$/.test(written)) {
    return { code: `Key${written}`, key: written.toLowerCase(), shifted: written };
  }
  switch (written) {
    case 'Plus':
      // The main-row + is Shift+=; the matcher ignores Shift beside a symbol.
      return { code: 'Equal', key: '+', shifted: '+' };
    case 'Minus':
      return { code: 'Minus', key: '-', shifted: '_' };
    case '=':
      return { code: 'Equal', key: '=', shifted: '+' };
    case 'Space':
      return { code: 'Space', key: ' ', shifted: ' ' };
    default:
      // A named key: its code and value coincide for every key the grid binds.
      return { code: written, key: written, shifted: written };
  }
}

/** The keydown that presses a binding's combination on one platform. */
function pressBinding(keys: string, apple: boolean, typed?: string): KeyboardEvent {
  const parts = keys.split('+');
  const written = parts.at(-1)!;
  const held = new Set(parts.slice(0, -1));
  const mod = held.has('Mod');
  return keydown(
    physicalKey(written),
    {
      ctrl: held.has('Ctrl') || (mod && !apple),
      alt: held.has('Alt'),
      shift: held.has('Shift') || written === 'Plus',
      meta: held.has('Meta') || (mod && apple),
    },
    typed,
  );
}

// What each letter key types on the Arabic (101) layout.
const ARABIC: Readonly<Record<string, string>> = {
  A: 'ش',
  C: 'ؤ',
  D: 'ي',
  F: 'ب',
  V: 'ر',
  X: 'ء',
  Y: 'غ',
  Z: 'ئ',
};

/** A binding's key part when it is a single letter, else `undefined`. */
function letterOf(binding: TmGridKeyBinding): string | undefined {
  const written = binding.keys.slice(binding.keys.lastIndexOf('+') + 1);
  return /^[A-Z]$/.test(written) ? written : undefined;
}

// Section: grid states.

/** Every condition met; the active cell is not a boolean one. */
function everything(apple: boolean): TmGridKeyContext {
  return {
    apple,
    editable: true,
    searchable: true,
    selectable: true,
    isTree: true,
    activeIsBoolean: false,
    rowActivation: true,
  };
}

/**
 * Resolves a binding's keydown where it applies: on a navigable header for
 * a header binding, in the rows otherwise.
 */
function resolveWhereBound(
  binding: TmGridKeyBinding,
  event: KeyboardEvent,
  apple: boolean,
): TmGridIntent | null {
  return binding.when === 'header'
    ? tmResolveHeaderKey(event, { apple, searchable: true })
    : tmResolveGridKey(event, everything(apple));
}

/** Every condition met except the binding's own (each of them, for a list). */
function without(
  when: TmGridKeyCondition | readonly TmGridKeyCondition[],
  apple: boolean,
): TmGridKeyContext {
  const unmet: readonly TmGridKeyCondition[] = typeof when === 'string' ? [when] : when;
  return {
    ...everything(apple),
    editable: !unmet.includes('editable'),
    searchable: !unmet.includes('searchable'),
    selectable: !unmet.includes('selectable'),
    isTree: !unmet.includes('tree'),
    rowActivation: !unmet.includes('rowActivation'),
  };
}

/** Whether an intent is type-to-edit, which seeds the editor with any printable key. */
function isTypeToEdit(intent: TmGridIntent | null): boolean {
  return intent !== null && intent.kind === 'edit' && intent.seed !== undefined;
}

const PLATFORMS = [
  { name: 'Windows and Linux', apple: false },
  { name: 'Apple', apple: true },
] as const;

// Section: the tests.

describe('tmResolveGridKey (binding table)', () => {
  for (const { name, apple } of PLATFORMS) {
    describe(name, () => {
      it.each(TM_GRID_KEY_BINDINGS.map((binding) => [binding.keys, binding] as const))(
        'resolves %s to its binding',
        (_keys, binding) => {
          expect(resolveWhereBound(binding, pressBinding(binding.keys, apple), apple)).toEqual(
            binding.intent,
          );
        },
      );

      it.each(
        TM_GRID_KEY_BINDINGS.filter((binding) => letterOf(binding) !== undefined).map(
          (binding) => [binding.keys, binding] as const,
        ),
      )('resolves %s on an Arabic layout by its physical key', (_keys, binding) => {
        const typed = ARABIC[letterOf(binding)!];
        expect(typed).toBeDefined();
        expect(
          tmResolveGridKey(pressBinding(binding.keys, apple, typed), everything(apple)),
        ).toEqual(binding.intent);
      });

      it.each(
        TM_GRID_KEY_BINDINGS.filter((binding) => binding.when !== undefined).map(
          (binding) => [binding.keys, String(binding.when), binding] as const,
        ),
      )('does not act on %s unless the grid is %s', (_keys, _when, binding) => {
        // A bare Space falls through to type-to-edit without row checkboxes,
        // and a combination two bindings share resolves to the other one.
        const intent = tmResolveGridKey(
          pressBinding(binding.keys, apple),
          without(binding.when!, apple),
        );
        const others = TM_GRID_KEY_BINDINGS.filter(
          (other) => other !== binding && other.keys === binding.keys,
        ).map((other) => JSON.stringify(other.intent));
        expect(
          intent === null || isTypeToEdit(intent) || others.includes(JSON.stringify(intent)),
          JSON.stringify(intent),
        ).toBe(true);
      });
    });
  }

  it('matches the row operations under Option on Apple, where the key types another symbol', () => {
    const insert = keydown(
      { code: 'Equal', key: '≠', shifted: '±' },
      {
        ctrl: false,
        alt: true,
        shift: false,
        meta: true,
      },
    );
    const remove = keydown(
      { code: 'Minus', key: '–', shifted: '—' },
      {
        ctrl: false,
        alt: true,
        shift: false,
        meta: true,
      },
    );
    expect(tmResolveGridKey(insert, everything(true))).toEqual({ kind: 'insertRowsAbove' });
    expect(tmResolveGridKey(remove, everything(true))).toEqual({ kind: 'deleteRows' });
  });

  it('leaves the browser zoom keys alone', () => {
    for (const physical of [physicalKey('='), physicalKey('Minus')]) {
      const event = keydown(physical, { ctrl: true, alt: false, shift: false, meta: false });
      expect(tmResolveGridKey(event, everything(false))).toBeNull();
    }
  });
});

describe('tmResolveGridKey (nothing outside the table)', () => {
  // Every key the table binds, pressed under every set of modifiers.
  const named = [
    'ArrowUp',
    'ArrowDown',
    'ArrowLeft',
    'ArrowRight',
    'PageUp',
    'PageDown',
    'Home',
    'End',
    'Space',
    'Tab',
    'Enter',
    'F2',
    'F10',
    'Delete',
    'Backspace',
    'Escape',
    'ContextMenu',
  ];
  const characters = ['A', 'C', 'D', 'F', 'V', 'X', 'Y', 'Z', 'Minus', '='];
  const modifierSets: Modifiers[] = [];
  for (let bits = 0; bits < 16; bits++) {
    modifierSets.push({
      ctrl: (bits & 1) !== 0,
      alt: (bits & 2) !== 0,
      shift: (bits & 4) !== 0,
      meta: (bits & 8) !== 0,
    });
  }

  for (const { name, apple } of PLATFORMS) {
    it(`resolves only the table's combinations on ${name}`, () => {
      const ctx = everything(apple);
      for (const written of [...named, ...characters]) {
        const physical = physicalKey(written);
        for (const modifiers of modifierSets) {
          const event = keydown(physical, modifiers);
          const intent = tmResolveGridKey(event, ctx);
          if (intent === null) {
            continue;
          }
          if (isTypeToEdit(intent)) {
            // Only typing seeds the editor: no Ctrl or Meta, and Alt only
            // as Apple's Option (these events carry no AltGraph).
            expect(
              modifiers.ctrl || modifiers.meta || (modifiers.alt && !apple),
              `${JSON.stringify(modifiers)} ${written}`,
            ).toBe(false);
            continue;
          }
          const bound = TM_GRID_KEY_BINDINGS.filter((binding) =>
            tmKeysMatch(event, binding.keys, apple),
          );
          expect(
            bound.map((binding) => binding.intent),
            `${JSON.stringify(modifiers)} ${written}`,
          ).toContainEqual(intent);
        }
      }
    });
  }
});

describe('tmResolveGridKey (type-to-edit)', () => {
  const letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('');
  // The US digit row: each digit and what Shift types on it.
  const digits = [...'0123456789'].map((digit, index) => ({
    code: `Digit${digit}`,
    key: digit,
    shifted: ')!@#$%^&*('[index]!,
  }));
  const keys: PhysicalKey[] = [...letters.map((letter) => physicalKey(letter)), ...digits];
  const none = { ctrl: false, alt: false, shift: false, meta: false };

  it('seeds the editor with plain and shifted typing on every platform', () => {
    for (const { apple } of PLATFORMS) {
      for (const physical of keys) {
        for (const shift of [false, true]) {
          const event = keydown(physical, { ...none, shift });
          expect(tmResolveGridKey(event, everything(apple))).toEqual({
            kind: 'edit',
            mode: 'enter',
            seed: event.key,
          });
        }
      }
    }
  });

  it('leaves Alt and Ctrl+Alt chords to the page on Windows and Linux', () => {
    // Spec 0027's platform family (Alt+Shift+<letter or digit>,
    // Mod+Alt+Shift+<key>) among them: Alt types nothing there.
    const chords = [
      { ...none, alt: true },
      { ...none, alt: true, shift: true },
      { ...none, ctrl: true, alt: true },
      { ...none, ctrl: true, alt: true, shift: true },
    ];
    for (const physical of keys) {
      for (const modifiers of chords) {
        const event = keydown(physical, modifiers);
        expect(
          tmResolveGridKey(event, everything(false)),
          `${JSON.stringify(modifiers)} ${physical.code}`,
        ).toBeNull();
      }
    }
  });

  it('seeds the editor with an AltGr character on Windows and Linux', () => {
    // Windows reports AltGr as Ctrl+Alt with AltGraph; Linux as AltGraph alone.
    const windows = keydown(
      { code: 'KeyQ', key: '@', shifted: '@' },
      { ...none, ctrl: true, alt: true, altGraph: true },
    );
    const linux = keydown({ code: 'KeyE', key: '€', shifted: '€' }, { ...none, altGraph: true });
    expect(tmResolveGridKey(windows, everything(false))).toEqual({
      kind: 'edit',
      mode: 'enter',
      seed: '@',
    });
    expect(tmResolveGridKey(linux, everything(false))).toEqual({
      kind: 'edit',
      mode: 'enter',
      seed: '€',
    });
  });

  it('seeds the editor with an Option character on Apple, never with a Ctrl or Cmd chord', () => {
    const option = keydown({ code: 'KeyE', key: 'é', shifted: 'é' }, { ...none, alt: true });
    expect(tmResolveGridKey(option, everything(true))).toEqual({
      kind: 'edit',
      mode: 'enter',
      seed: 'é',
    });
    for (const modifiers of [
      { ...none, ctrl: true },
      { ...none, meta: true, shift: true },
      { ...none, ctrl: true, alt: true },
    ]) {
      const event = keydown(physicalKey('P'), modifiers);
      expect(tmResolveGridKey(event, everything(true)), JSON.stringify(modifiers)).toBeNull();
    }
  });
});

describe('tmResolveGridKey (readonly grids)', () => {
  const none: Modifiers = { ctrl: false, alt: false, shift: false, meta: false };
  const readonlyGrid = (rowActivation: boolean): TmGridKeyContext => ({
    ...everything(false),
    editable: false,
    rowActivation,
  });

  for (const rowActivation of [false, true]) {
    it(`leaves typed characters to the page ${rowActivation ? 'with' : 'without'} row activation`, () => {
      for (const written of ['A', 'Z', '7', '=', 'Minus']) {
        for (const shift of [false, true]) {
          const event = keydown(physicalKey(written), { ...none, shift });
          expect(tmResolveGridKey(event, readonlyGrid(rowActivation)), written).toBeNull();
        }
      }
    });
  }

  it('opens the row in a new tab on Mod+Enter only under row activation', () => {
    for (const apple of [false, true]) {
      const modEnter = keydown(physicalKey('Enter'), { ...none, ctrl: !apple, meta: apple });
      const ctx = { ...readonlyGrid(true), apple };
      expect(tmResolveGridKey(modEnter, ctx)).toEqual({ kind: 'activateRow', newTab: true });
      expect(tmResolveGridKey(modEnter, { ...ctx, rowActivation: false })).toBeNull();
    }
  });

  it('reaches the cell content on F2 only under row activation', () => {
    const f2 = keydown(physicalKey('F2'), none);
    expect(tmResolveGridKey(f2, readonlyGrid(true))).toEqual({ kind: 'edit', mode: 'edit' });
    expect(tmResolveGridKey(f2, readonlyGrid(false))).toBeNull();
  });
});

describe('TM_GRID_KEYS', () => {
  it('lists the binding table without its intents, frozen', () => {
    expect(TM_GRID_KEYS).toEqual(
      TM_GRID_KEY_BINDINGS.map(({ keys, labelKey }) => ({ keys, labelKey })),
    );
    expect(Object.isFrozen(TM_GRID_KEYS)).toBe(true);
    expect(TM_GRID_KEYS.every((entry) => Object.isFrozen(entry))).toBe(true);
  });

  it('names each combination once per state it applies in', () => {
    const keys = TM_GRID_KEY_BINDINGS.map((entry) => `${entry.keys} ${String(entry.when)}`);
    expect(new Set(keys).size).toBe(keys.length);
    // Alt+ArrowDown alone means two things: a cell's dropdown and a header's action.
    const shared = TM_GRID_KEYS.filter((entry) => entry.keys === 'Alt+ArrowDown');
    expect(shared.map((entry) => entry.labelKey)).toEqual([
      'tmUi.grid.keys.openDropdown',
      'tmUi.grid.keys.headerAction',
    ]);
  });

  it('labels every entry with an English string under the grid keys', () => {
    const labels: Readonly<Record<string, string>> = TM_UI_STRINGS_EN.grid.keys;
    for (const { labelKey } of TM_GRID_KEYS) {
      expect(labelKey).toMatch(/^tmUi\.grid\.keys\./);
      expect(labels[labelKey.slice('tmUi.grid.keys.'.length)], labelKey).toEqual(
        expect.any(String),
      );
    }
    // And no label is left without a binding.
    const used = new Set(
      TM_GRID_KEYS.map((entry) => entry.labelKey.slice('tmUi.grid.keys.'.length)),
    );
    expect(Object.keys(labels).sort()).toEqual([...used].sort());
  });
});

describe('tmResolveHeaderKey', () => {
  /** Resolves a named key on a header, with the given modifiers. */
  function resolve(
    key: string,
    init: KeyboardEventInit = {},
    ctx: { apple: boolean; searchable: boolean } = { apple: false, searchable: true },
  ): TmGridIntent | null {
    return tmResolveHeaderKey(new KeyboardEvent('keydown', { key, ...init }), ctx);
  }

  it('moves between headers and down to the rows on plain arrows, Home and End', () => {
    expect(resolve('ArrowLeft')).toEqual({ kind: 'headerMove', motion: 'left' });
    expect(resolve('ArrowRight')).toEqual({ kind: 'headerMove', motion: 'right' });
    expect(resolve('Home')).toEqual({ kind: 'headerMove', motion: 'first' });
    expect(resolve('End')).toEqual({ kind: 'headerMove', motion: 'last' });
    expect(resolve('ArrowUp')).toEqual({ kind: 'headerMove', motion: 'up' });
    expect(resolve('ArrowDown')).toEqual({ kind: 'headerMove', motion: 'down' });
  });

  it('activates on Enter and Space and opens the action on Alt+ArrowDown alone', () => {
    expect(resolve('Enter')).toEqual({ kind: 'headerActivate' });
    expect(resolve(' ')).toEqual({ kind: 'headerActivate' });
    expect(resolve('ArrowDown', { altKey: true })).toEqual({ kind: 'headerAction' });
    expect(resolve('ArrowDown', { altKey: true, shiftKey: true })).toBeNull();
    expect(resolve('ArrowDown', { altKey: true, ctrlKey: true })).toBeNull();
    expect(resolve('ArrowUp', { altKey: true })).toBeNull();
  });

  it('leaves chords, Tab and typing to the page, keeping Escape and the find key', () => {
    expect(resolve('Tab')).toBeNull();
    expect(resolve('a')).toBeNull();
    expect(resolve('ArrowRight', { shiftKey: true })).toBeNull();
    expect(resolve('Home', { ctrlKey: true })).toBeNull();
    expect(resolve('Enter', { metaKey: true })).toBeNull();
    expect(resolve('Escape')).toEqual({ kind: 'escape' });
    expect(resolve('f', { ctrlKey: true, code: 'KeyF' })).toEqual({ kind: 'find' });
    expect(resolve('f', { metaKey: true, code: 'KeyF' }, { apple: true, searchable: true })).toEqual(
      { kind: 'find' },
    );
    expect(resolve('f', { ctrlKey: true, code: 'KeyF' }, { apple: false, searchable: false })).toBeNull();
  });
});

describe('tmResolveGridKey (header-only bindings)', () => {
  it('never resolves the header action in the rows', () => {
    const event = new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true });
    expect(
      tmResolveGridKey(event, {
        apple: false,
        editable: false,
        searchable: false,
        selectable: false,
        isTree: false,
        activeIsBoolean: false,
        rowActivation: false,
      }),
    ).toBeNull();
  });
});
