// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * A key combination: zero or more modifiers, then one `KeyboardEvent.key`
 * value, joined by `+`.
 *
 * The modifiers are `Mod` (⌘ on Apple platforms, Ctrl elsewhere), `Ctrl`,
 * `Alt`, `Shift` and `Meta`. The words `Plus`, `Minus` and `Space` stand for
 * the `+`, `-` and space keys. A named key is spelt the way
 * `KeyboardEvent.key` spells it (`'PageUp'`, `'F2'`), which is also how its
 * cap reads. Examples: `'Mod+K'`, `'?'`, `'F2'`, `'Mod+Plus'`,
 * `'Alt+Shift+P'`. Every name matches case-insensitively.
 *
 * A symbol key never takes `Shift`: which symbol a key types with Shift
 * depends on the layout, so a symbol binding names the symbol itself
 * (`'Mod+?'`, not `'Mod+Shift+/'`) and matches with or without Shift.
 * Writing Shift beside a symbol is an error that names the symbol to bind.
 */
export type TmKeys = string;

/** A key combination, parsed once and cached. */
interface TmParsedKeys {
  /** `Mod` was written. */
  readonly mod: boolean;
  /** `Ctrl` was written. */
  readonly ctrl: boolean;
  /** `Alt` was written. */
  readonly alt: boolean;
  /** `Shift` was written. */
  readonly shift: boolean;
  /** `Meta` was written. */
  readonly meta: boolean;
  /** The key, lowercased for comparison: one character, or a named key. */
  readonly key: string;
  /** The key as displayed: an uppercase letter, a symbol, or a key name. */
  readonly label: string;
  /** The key as `aria-keyshortcuts` writes it. */
  readonly aria: string;
  /**
   * How the key matches when `event.key` cannot: `letter` and `digit` by
   * `KeyX`/`DigitN` code, `punctuation` through the US layout's codes, and
   * never for `symbol` (any other printable character that is not a letter,
   * digit or space), `named` (a key name such as `F2`) or `other` (a space,
   * or a letter or digit outside ASCII, which has no US key to fall back
   * to). Shift is compared for every kind but `punctuation` and `symbol`.
   */
  readonly kind: 'letter' | 'digit' | 'punctuation' | 'symbol' | 'named' | 'other';
}

// The US layout's punctuation keys: each code with its unshifted and shifted
// symbol. A punctuation binding falls back to these codes when the layout
// types something else on the key (spec 0023 §3.2).
const US_PUNCTUATION: Readonly<Record<string, readonly [string, string]>> = {
  Slash: ['/', '?'],
  Period: ['.', '>'],
  Comma: [',', '<'],
  Semicolon: [';', ':'],
  Quote: ["'", '"'],
  BracketLeft: ['[', '{'],
  BracketRight: [']', '}'],
  Backslash: ['\\', '|'],
  Backquote: ['`', '~'],
  Minus: ['-', '_'],
  Equal: ['=', '+'],
};

// Every symbol the codes above can read as, for classifying a binding.
const US_PUNCTUATION_SYMBOLS: ReadonlySet<string> = new Set(Object.values(US_PUNCTUATION).flat());

// The words that stand for keys `+` cannot otherwise spell, plus space.
const KEY_WORDS: Readonly<Record<string, string>> = { plus: '+', minus: '-', space: ' ' };

// The display labels that differ from the key name: the arrows draw as
// arrows and Escape abbreviates, on every platform and in every language.
const KEY_LABELS: Readonly<Record<string, string>> = {
  escape: 'Esc',
  arrowup: '↑',
  arrowdown: '↓',
  arrowleft: '←',
  arrowright: '→',
  ' ': 'Space',
};

const MODIFIERS = ['mod', 'ctrl', 'alt', 'shift', 'meta'] as const;

// Parsed combinations, by their exact text: a keydown handler matching a
// dozen bindings parses nothing after the first press.
const parsed = new Map<string, TmParsedKeys>();

/** The error a malformed combination throws. */
function invalid(keys: TmKeys): Error {
  return new Error(
    `tmKeys: "${keys}" is not a key combination: write modifiers (Mod, Ctrl, Alt, Shift, ` +
      "Meta, each once) then one key, joined by '+', with the + key as 'Plus'.",
  );
}

/**
 * The error a combination of Shift and a symbol throws. It names the binding
 * to write instead: the US layout's shifted symbol for an unshifted
 * punctuation key (`'Mod+Shift+/'` → `'Mod+?'`), else the same symbol
 * without Shift (`'Shift+?'` → `'?'`).
 */
function shiftedSymbol(keys: TmKeys, parts: readonly string[], key: string): Error {
  const shifted = Object.values(US_PUNCTUATION).find(([plain]) => plain === key)?.[1] ?? key;
  const written = shifted === '+' ? 'Plus' : shifted === '-' ? 'Minus' : shifted;
  const instead = [...parts.filter((part) => part.toLowerCase() !== 'shift'), written].join('+');
  return new Error(
    `tmKeys: "${keys}" combines Shift with a symbol key, and the layout decides which ` +
      `symbol Shift types; bind the symbol itself instead: '${instead}'.`,
  );
}

/** Parses (or recalls) a key combination, throwing on a malformed one. */
function parse(keys: TmKeys): TmParsedKeys {
  const cached = parsed.get(keys);
  if (cached !== undefined) {
    return cached;
  }

  // Section: split into modifiers and the one key. Every malformation (an
  // unknown or repeated modifier, a missing key, a bare '+') gets the same
  // message, which states the whole grammar.
  const parts = keys.split('+');
  const keyPart = parts.pop() ?? '';
  const modifiers = { mod: false, ctrl: false, alt: false, shift: false, meta: false };
  const isModifier = (part: string) => MODIFIERS.some((name) => name === part.toLowerCase());
  for (const part of parts) {
    const modifier = part.toLowerCase() as (typeof MODIFIERS)[number];
    if (!isModifier(part) || modifiers[modifier]) {
      throw invalid(keys);
    }
    modifiers[modifier] = true;
  }
  if (keyPart === '' || isModifier(keyPart)) {
    throw invalid(keys);
  }

  // Section: classify the key and derive its display and ARIA spellings.
  const lower = keyPart.toLowerCase();
  const key = KEY_WORDS[lower] ?? lower;
  let kind: TmParsedKeys['kind'];
  let label: string;
  let aria: string;
  if ([...key].length === 1) {
    kind = /^[a-z]$/.test(key)
      ? 'letter'
      : /^[0-9]$/.test(key)
        ? 'digit'
        : US_PUNCTUATION_SYMBOLS.has(key)
          ? 'punctuation'
          : /^[\p{L}\p{N}\s]$/u.test(key)
            ? 'other'
            : 'symbol';
    // Uppercase only where that stays one character ('ß' would become 'SS').
    const upper = [...key.toUpperCase()].length === 1 ? key.toUpperCase() : key;
    label = KEY_LABELS[key] ?? upper;
    aria = key === '+' ? 'Plus' : key === ' ' ? 'Space' : upper;
  } else {
    kind = 'named';
    label = KEY_LABELS[key] ?? keyPart;
    aria = keyPart;
  }
  // A symbol never compares Shift (see tmKeysMatch), so a written Shift
  // beside one could never be honoured: refuse it rather than bind a
  // shortcut that silently never fires.
  if (modifiers.shift && (kind === 'punctuation' || kind === 'symbol')) {
    throw shiftedSymbol(keys, parts, key);
  }
  const result: TmParsedKeys = { ...modifiers, key, label, aria, kind };
  parsed.set(keys, result);
  return result;
}

/**
 * Whether the code is running on an Apple platform (macOS, iOS, iPadOS),
 * where `Mod` is ⌘ and shortcut hints use the ⌃ ⌥ ⇧ ⌘ symbols.
 *
 * Reads `navigator.userAgentData.platform` where the browser offers it, and
 * `navigator.platform` otherwise. This is the library's one platform check;
 * every other component asks it.
 *
 * @returns `true` on macOS, iOS and iPadOS.
 */
export function tmIsApplePlatform(): boolean {
  if (typeof navigator === 'undefined') {
    return false;
  }
  const uaData = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData;
  if (uaData?.platform) {
    return /^(macos|ios)$/i.test(uaData.platform);
  }
  return /^(mac|iphone|ipad|ipod)/i.test(navigator.platform ?? '');
}

/** The resolved modifier set: `Mod` folded into Meta or Ctrl. */
function resolvedModifiers(
  keys: TmParsedKeys,
  apple: boolean,
): { ctrl: boolean; alt: boolean; shift: boolean; meta: boolean } {
  return {
    ctrl: keys.ctrl || (keys.mod && !apple),
    alt: keys.alt,
    shift: keys.shift,
    meta: keys.meta || (keys.mod && apple),
  };
}

/**
 * The labels of a key combination, one per key cap, modifiers first.
 *
 * Elsewhere the modifiers read Ctrl, Alt, Shift and Meta; Apple platforms
 * use ⌃ ⌥ ⇧ ⌘, in that order. Key names stay Latin in every language:
 * letters uppercase, Escape as Esc, the arrows as ↑ ↓ ← →.
 *
 * @example
 * ```ts
 * tmFormatKeys('Mod+Shift+K', false); // ['Ctrl', 'Shift', 'K']
 * tmFormatKeys('Mod+Shift+K', true);  // ['⇧', '⌘', 'K']
 * ```
 *
 * @param keys - The combination.
 * @param apple - Whether to label for an Apple platform; defaults to
 *   {@link tmIsApplePlatform}.
 * @returns One label per key cap.
 * @throws Error when `keys` is not a valid combination.
 */
export function tmFormatKeys(
  keys: TmKeys,
  apple: boolean = tmIsApplePlatform(),
): readonly string[] {
  const combination = parse(keys);
  const { ctrl, alt, shift, meta } = resolvedModifiers(combination, apple);
  const labels: string[] = [];
  if (ctrl) {
    labels.push(apple ? '⌃' : 'Ctrl');
  }
  if (alt) {
    labels.push(apple ? '⌥' : 'Alt');
  }
  if (shift) {
    labels.push(apple ? '⇧' : 'Shift');
  }
  if (meta) {
    labels.push(apple ? '⌘' : 'Meta');
  }
  labels.push(combination.label);
  return labels;
}

/**
 * A key combination as one string, for sentences and tooltips: the
 * {@link tmFormatKeys} labels joined with `+`, or with nothing on Apple
 * platforms.
 *
 * @example
 * ```ts
 * tmKeysText('Mod+V', false); // 'Ctrl+V'
 * tmKeysText('Mod+V', true);  // '⌘V'
 * ```
 *
 * @param keys - The combination.
 * @param apple - Whether to write it for an Apple platform; defaults to
 *   {@link tmIsApplePlatform}.
 * @returns The combination as display text.
 * @throws Error when `keys` is not a valid combination.
 */
export function tmKeysText(keys: TmKeys, apple: boolean = tmIsApplePlatform()): string {
  return tmFormatKeys(keys, apple).join(apple ? '' : '+');
}

/**
 * A key combination in the `aria-keyshortcuts` syntax: `Control`, `Alt`,
 * `Shift` and `Meta`, then the key, joined with `+` (`'Control+K'`, or
 * `'Meta+K'` on Apple platforms for `'Mod+K'`).
 *
 * @param keys - The combination.
 * @param apple - Whether `Mod` means Meta; defaults to
 *   {@link tmIsApplePlatform}.
 * @returns The attribute value.
 * @throws Error when `keys` is not a valid combination.
 */
export function tmAriaKeyShortcuts(keys: TmKeys, apple: boolean = tmIsApplePlatform()): string {
  const combination = parse(keys);
  const { ctrl, alt, shift, meta } = resolvedModifiers(combination, apple);
  return [
    ...(ctrl ? ['Control'] : []),
    ...(alt ? ['Alt'] : []),
    ...(shift ? ['Shift'] : []),
    ...(meta ? ['Meta'] : []),
    combination.aria,
  ].join('+');
}

/** Whether a key value is one ASCII letter or digit. */
function isAsciiAlphanumeric(key: string): boolean {
  return /^[a-z0-9]$/i.test(key);
}

/** Whether a key value is one character outside ASCII. */
function isOneNonAsciiCharacter(key: string): boolean {
  const characters = [...key];
  return characters.length === 1 && characters[0].codePointAt(0)! > 0x7f;
}

/** What the physical key reads as on a US layout, for the code fallback. */
function usReading(code: string, shift: boolean, kind: TmParsedKeys['kind']): string | undefined {
  if (kind === 'letter') {
    return /^Key[A-Z]$/.test(code) ? code.slice(3).toLowerCase() : undefined;
  }
  if (kind === 'digit') {
    // The main row only: a numpad key with Num Lock off is End, Home, …
    return /^Digit[0-9]$/.test(code) ? code.slice(5) : undefined;
  }
  const pair = US_PUNCTUATION[code];
  return pair === undefined ? undefined : pair[shift ? 1 : 0];
}

/**
 * Whether a keydown matches a key combination, on any keyboard layout.
 *
 * - **Modifiers match exactly**, except Shift beside a printable symbol
 *   (which is why a symbol binding cannot name Shift)
 *   (`?`, `+`, `/`): layouts differ on whether a symbol needs Shift, so
 *   `'?'` matches Shift+/ on a US layout and `'Mod+Alt+Plus'` matches both
 *   the numpad `+` and Shift+= on the main keys.
 * - **Keys compare `event.key`**, case-insensitively. When that fails, the
 *   physical key (`event.code`) decides instead in three cases: a letter or
 *   digit binding whose `event.key` is not an ASCII letter or digit ("ي" or
 *   "]" from the D key on an Arabic layout); a US punctuation binding whose
 *   `event.key` is one non-ASCII character ("ظ" from the Slash key); and
 *   either kind while Alt (Option) is held on an Apple platform. A
 *   punctuation code reads as the US layout's key, or its shifted symbol
 *   with Shift held.
 * - **AltGr types characters**, so the physical fallback never applies
 *   while AltGraph is down: on a Polish layout AltGr+A types "ą" and does
 *   not match `'Mod+Alt+A'`. Apple platforms have no AltGr key; Option is
 *   Alt there, whatever the engine reports.
 *
 * It does not filter composition (`isComposing`) or repeated events; a
 * caller that must ignore those checks them first.
 *
 * @param event - The keydown.
 * @param keys - The combination.
 * @param apple - Whether `Mod` means ⌘ and Option rules apply; defaults to
 *   {@link tmIsApplePlatform}.
 * @returns `true` when the event is that combination.
 * @throws Error when `keys` is not a valid combination.
 */
export function tmKeysMatch(
  event: KeyboardEvent,
  keys: TmKeys,
  apple: boolean = tmIsApplePlatform(),
): boolean {
  const combination = parse(keys);
  const want = resolvedModifiers(combination, apple);

  // Section: modifiers, exact except Shift beside a symbol.
  const symbol = combination.kind === 'punctuation' || combination.kind === 'symbol';
  if (
    event.ctrlKey !== want.ctrl ||
    event.altKey !== want.alt ||
    event.metaKey !== want.meta ||
    (!symbol && event.shiftKey !== want.shift)
  ) {
    return false;
  }

  // Section: the typed key.
  if (event.key.toLowerCase() === combination.key) {
    return true;
  }

  // Section: the physical key, in the three fallback cases only.
  const physical =
    combination.kind === 'letter' ||
    combination.kind === 'digit' ||
    combination.kind === 'punctuation';
  if (!physical || (!apple && event.getModifierState('AltGraph'))) {
    return false;
  }
  const fallback =
    (apple && event.altKey) ||
    (combination.kind === 'punctuation'
      ? isOneNonAsciiCharacter(event.key)
      : !isAsciiAlphanumeric(event.key));
  return fallback && usReading(event.code, event.shiftKey, combination.kind) === combination.key;
}
