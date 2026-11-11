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
  };
}

/** Every condition met except one. */
function without(condition: TmGridKeyCondition, apple: boolean): TmGridKeyContext {
  return {
    ...everything(apple),
    editable: condition !== 'editable',
    searchable: condition !== 'searchable',
    selectable: condition !== 'selectable',
    isTree: condition !== 'tree',
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
          expect(tmResolveGridKey(pressBinding(binding.keys, apple), everything(apple))).toEqual(
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
          (binding) => [binding.keys, binding.when, binding] as const,
        ),
      )('does not act on %s unless the grid is %s', (_keys, when, binding) => {
        // A bare Space falls through to type-to-edit without row checkboxes.
        const intent = tmResolveGridKey(pressBinding(binding.keys, apple), without(when!, apple));
        expect(intent === null || isTypeToEdit(intent), JSON.stringify(intent)).toBe(true);
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
          if (intent === null || isTypeToEdit(intent)) {
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

describe('TM_GRID_KEYS', () => {
  it('lists the binding table without its intents, frozen', () => {
    expect(TM_GRID_KEYS).toEqual(
      TM_GRID_KEY_BINDINGS.map(({ keys, labelKey }) => ({ keys, labelKey })),
    );
    expect(Object.isFrozen(TM_GRID_KEYS)).toBe(true);
    expect(TM_GRID_KEYS.every((entry) => Object.isFrozen(entry))).toBe(true);
  });

  it('names each combination once', () => {
    const keys = TM_GRID_KEYS.map((entry) => entry.keys);
    expect(new Set(keys).size).toBe(keys.length);
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
