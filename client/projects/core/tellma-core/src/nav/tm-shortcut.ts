// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { tmFormatKeys, type TmKeys } from '@tellma/core-ui/kbd';

// The shortcut registry's contract and its rules (spec 0027 §4.1, §4.3,
// Appendix A). The platform and pages draw their keys from disjoint families,
// so a page can never take a shell key: a `tm.` id registers only the keys
// Appendix A reserves for it, and every other id only a page key. The rules
// here are pure, so the registry asks them once per registration and the
// dispatch never re-checks them.

/**
 * An app-level keyboard shortcut, registered with `TmShortcuts`.
 *
 * Its keys come from one of two disjoint families. The platform's are the
 * `tm.` ids of {@link TM_RESERVED_SHORTCUTS}, each with its reserved keys.
 * Every other id takes a page key: `Mod+<key>` or `Mod+Shift+<key>` (except
 * `Mod+K` and `Mod+/`), `F2`, `Alt+PageUp` or `Alt+PageDown`. Unmodified
 * printable keys are never shortcuts: a page that wants one (type-to-search)
 * listens itself.
 *
 * @example
 * ```ts
 * inject(TmShortcuts).register({
 *   id: 'acme.credit-review.approve-next', keys: 'Mod+Shift+M',
 *   labelKey: 'Acme_CreditReview_ApproveNext', scope: 'page', run: () => this.approveNext(),
 * });
 * ```
 */
export interface TmShortcut {
  /** The shortcut's id, such as `tm.launcher.open`, `details.save` or `acme.credit-review.approve-next`. */
  readonly id: string;
  /** The key combination, in `@tellma/core-ui/kbd`'s grammar: `'Mod+K'`, `'Alt+Shift+P'`, `'F2'`. */
  readonly keys: TmKeys;
  /** The string key of the shortcut's row in the keyboard shortcuts dialog. */
  readonly labelKey: string;
  /** The dialog section the shortcut lists under: General (`global`) or This page (`page`). */
  readonly scope: 'global' | 'page';
  /**
   * Whether the shortcut runs while focus is in an editable field: a text
   * input, a text area, a select, an editable region or an open grid cell
   * editor. Defaults to `true` for keys with `Mod` or `Ctrl`, `false` for the rest.
   */
  readonly inFields?: boolean;
  /** Whether the shortcut runs while a modal dialog is open; defaults to `false`. */
  readonly overModals?: boolean;
  /**
   * Runs the shortcut. Return `false` when it did not handle the key: the
   * event then continues as if no shortcut had matched.
   *
   * @param event - The keydown that matched.
   */
  run(event: KeyboardEvent): void | false;
}

/** One of the platform's reserved shortcuts: its id, its keys and the part of the platform that registers it. */
export interface TmReservedShortcut {
  /** The shortcut's `tm.` id. */
  readonly id: string;
  /** Its key combination. */
  readonly keys: TmKeys;
  /** The part of the platform that registers it: `shell`, `attention`, `support`, `help` or `assistant`. */
  readonly owner: string;
}

/**
 * The platform's shortcuts: the only `tm.` ids the registry accepts, each
 * with the one combination reserved for it. `tm.help.open` and
 * `tm.assistant.open` are held for the help drawer and the assistant.
 *
 * The `Alt+Shift` letters avoid the keys ChromeOS and Chrome's accessibility
 * features handle before the page (S, M, N, L, T, A, B, R) and Chrome's tab
 * group keys (C, W, X, Z). P and I are taken from Chrome on purpose: neither
 * is a system or accessibility key, and Chrome lets the page handle both first.
 */
export const TM_RESERVED_SHORTCUTS: readonly TmReservedShortcut[] = Object.freeze([
  { id: 'tm.launcher.open', keys: 'Mod+K', owner: 'shell' },
  { id: 'tm.shortcuts.open', keys: 'Mod+/', owner: 'shell' },
  { id: 'tm.pins.toggle', keys: 'Alt+Shift+P', owner: 'shell' },
  { id: 'tm.toasts.focus', keys: 'Alt+Shift+J', owner: 'shell' },
  { id: 'tm.attention.notifications', keys: 'Alt+Shift+U', owner: 'attention' },
  { id: 'tm.attention.assignments', keys: 'Alt+Shift+Q', owner: 'attention' },
  { id: 'tm.help.open', keys: 'Alt+Shift+H', owner: 'help' },
  { id: 'tm.assistant.open', keys: 'Alt+Shift+I', owner: 'assistant' },
  { id: 'tm.support.copy-diagnostics', keys: 'Mod+Alt+Shift+D', owner: 'support' },
]);

/**
 * The combinations no page can take from the browser, which handles them
 * before the page sees them: new, close and restore tabs and windows, the
 * address bar, reload, quit, hide, minimize, print and zoom.
 */
export const TM_BROWSER_KEYS: readonly TmKeys[] = Object.freeze([
  'Mod+T',
  'Mod+W',
  'Mod+N',
  'Mod+Shift+N',
  'Mod+Shift+T',
  'Mod+Shift+W',
  'Mod+Tab',
  'Mod+L',
  'Mod+R',
  'Mod+Q',
  'Mod+H',
  'Mod+M',
  'Mod+P',
  'Mod+Plus',
  'Mod+Minus',
  'Mod+0',
]);

/**
 * Why the registry refused a shortcut: its keys are not a key combination
 * (`invalid`), lie outside its id's family (`family`), are a key a focused
 * grid handles (`grid`), are one the browser keeps (`browser`), or are
 * already taken by a live shortcut (`live`).
 */
export type TmShortcutRefusal = 'invalid' | 'family' | 'grid' | 'browser' | 'live';

/**
 * The error a refused registration throws in development. In production the
 * registry drops the registration instead and reports it as the
 * `tm.shortcut.conflict` telemetry event.
 */
export class TmShortcutConflictError extends Error {
  /** The id of the refused shortcut. */
  readonly id: string;
  /** Why it was refused. */
  readonly reason: TmShortcutRefusal;

  /**
   * Creates the error.
   *
   * @param id - The id of the refused shortcut.
   * @param reason - Why it was refused.
   * @param message - What the developer should change.
   * @param options - The underlying error, when the keys did not parse.
   */
  constructor(id: string, reason: TmShortcutRefusal, message: string, options?: ErrorOptions) {
    super(message, options);
    this.name = 'TmShortcutConflictError';
    this.id = id;
    this.reason = reason;
  }
}

// Section: the internal model of a combination. The kbd grammar validates and
// matches a combination; the families need its modifiers by name and its key.

/** A combination's parts: the modifiers as written, and the key as kbd compares it. */
export interface TmKeyParts {
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
  /** The key, lowercased, with `Plus`, `Minus` and `Space` as the characters they stand for. */
  readonly key: string;
}

/** The characters the grammar's key words stand for. */
const KEY_WORDS: Readonly<Record<string, string>> = { plus: '+', minus: '-', space: ' ' };

/**
 * Splits a combination into its parts.
 *
 * @param keys - The combination.
 * @returns Its parts.
 * @throws Error when `keys` is not a valid combination.
 */
export function tmKeyParts(keys: TmKeys): TmKeyParts {
  // The grammar's own parser validates first, so the split below only ever
  // sees well-formed text: modifiers, each once, then one key.
  tmFormatKeys(keys, false);
  const parts = keys.toLowerCase().split('+');
  const key = parts.pop() as string;
  return {
    mod: parts.includes('mod'),
    ctrl: parts.includes('ctrl'),
    alt: parts.includes('alt'),
    shift: parts.includes('shift'),
    meta: parts.includes('meta'),
    key: KEY_WORDS[key] ?? key,
  };
}

/**
 * A combination's identity on one platform: `Mod` folded into Meta on Apple
 * platforms and into Ctrl elsewhere, so `'Mod+Space'` and `'Ctrl+Space'` are
 * one combination off Apple platforms.
 */
function identity(parts: TmKeyParts, apple: boolean): string {
  const ctrl = parts.ctrl || (parts.mod && !apple);
  const meta = parts.meta || (parts.mod && apple);
  return `${ctrl ? 'c' : ''}${parts.alt ? 'a' : ''}${parts.shift ? 's' : ''}${meta ? 'm' : ''}:${parts.key}`;
}

/**
 * Whether two combinations are the same keys on either platform, so a
 * conflict is refused everywhere, not only on the developer's machine.
 */
export function tmSameKeys(a: TmKeyParts, b: TmKeyParts): boolean {
  return identity(a, false) === identity(b, false) || identity(a, true) === identity(b, true);
}

/** The modifiers as written, in a fixed order, for comparing against the families. */
function modifiers(parts: TmKeyParts): string {
  return `${parts.mod ? 'mod+' : ''}${parts.ctrl ? 'ctrl+' : ''}${parts.alt ? 'alt+' : ''}${parts.shift ? 'shift+' : ''}${parts.meta ? 'meta+' : ''}`;
}

/**
 * The only id that may take Escape: the dashboard's exit from presentation
 * mode, which runs only when no focused component, Escape dismissal or modal
 * takes the key first.
 */
export const TM_PRESENTATION_EXIT_ID = 'dashboard.exit-presentation';

/**
 * Whether a combination is a page key: `Mod+<key>` and `Mod+Shift+<key>`
 * (except `Mod+K` and `Mod+/`), which includes `Mod+Enter`, the admitted
 * `F2`, `Alt+PageUp` and `Alt+PageDown`, and Escape for presentation mode's exit.
 */
function isPageKey(id: string, parts: TmKeyParts): boolean {
  const written = modifiers(parts);
  return (
    (written === 'mod+' && parts.key !== 'k' && parts.key !== '/') ||
    written === 'mod+shift+' ||
    (written === '' && parts.key === 'f2') ||
    (written === 'alt+' && (parts.key === 'pageup' || parts.key === 'pagedown')) ||
    (written === '' && parts.key === 'escape' && id === TM_PRESENTATION_EXIT_ID)
  );
}

/**
 * The modified combinations a focused grid handles, as `TM_GRID_KEYS` in
 * `@tellma/core-ui/grid` lists them, less `Mod+Enter`.
 *
 * The copy keeps the grid's whole binding table off the app's first frame:
 * importing `TM_GRID_KEYS` would bring the table with its intents. A unit
 * test holds the copy equal to the grid's list, so the two cannot drift.
 *
 * `Mod+Enter` stays a page key: the grid takes it only while it opens rows,
 * and then it prevents the event, which the registry never handles.
 */
export const TM_GRID_MODIFIED_KEYS: readonly TmKeys[] = Object.freeze(
  (
    'Mod+ArrowUp Mod+ArrowDown Mod+ArrowLeft Mod+ArrowRight Mod+Home Mod+End ' +
    'Shift+ArrowUp Shift+ArrowDown Shift+ArrowLeft Shift+ArrowRight ' +
    'Mod+Shift+ArrowUp Mod+Shift+ArrowDown Mod+Shift+ArrowLeft Mod+Shift+ArrowRight ' +
    'Shift+PageUp Shift+PageDown Shift+Home Shift+End Mod+Shift+Home Mod+Shift+End ' +
    'Shift+Space Ctrl+Space Ctrl+Shift+Space Mod+A Shift+Tab Shift+Enter Alt+ArrowDown ' +
    'Shift+F10 Mod+F Mod+C Mod+X Mod+V Mod+D Mod+Z Mod+Y Mod+Shift+Z ' +
    'Mod+Alt+Minus Mod+Alt+Plus Mod+Alt+= Alt+ArrowRight Alt+ArrowLeft'
  ).split(' '),
);

/** Lazily parsed parts of a fixed list of combinations. */
function partsOf(list: readonly TmKeys[]): () => readonly TmKeyParts[] {
  let parsed: readonly TmKeyParts[] | null = null;
  return () => (parsed ??= list.map(tmKeyParts));
}

/** The grid's modified combinations, parsed on the first registration. */
const gridParts = partsOf(TM_GRID_MODIFIED_KEYS);

/** The browser's combinations, parsed on the first registration. */
const browserParts = partsOf(TM_BROWSER_KEYS);

/**
 * Why a shortcut cannot be registered beside the live ones, or `null` when it
 * can: the keys of a `tm.` id must be the ones TM_RESERVED_SHORTCUTS gives it,
 * every other id must take a page key, and neither may take a grid key with a
 * modifier, a browser key, or the keys of a live shortcut.
 *
 * @param shortcut - The shortcut to register.
 * @param parts - Its parsed keys.
 * @param live - The parsed keys of the live shortcuts.
 * @returns The refusal, or `null`.
 */
export function tmShortcutRefusal(
  shortcut: TmShortcut,
  parts: TmKeyParts,
  live: readonly TmKeyParts[],
): Exclude<TmShortcutRefusal, 'invalid'> | null {
  // The families: a reserved id takes its own keys, any other id a page key.
  if (shortcut.id.startsWith('tm.')) {
    const reserved = TM_RESERVED_SHORTCUTS.find((entry) => entry.id === shortcut.id);
    if (reserved === undefined || !tmSameKeys(parts, tmKeyParts(reserved.keys))) {
      return 'family';
    }
  } else if (!isPageKey(shortcut.id, parts)) {
    return 'family';
  }

  // The keys a focused grid or the browser keeps, then the live shortcuts.
  if (gridParts().some((grid) => tmSameKeys(parts, grid))) {
    return 'grid';
  }
  if (browserParts().some((browser) => tmSameKeys(parts, browser))) {
    return 'browser';
  }
  return live.some((other) => tmSameKeys(parts, other)) ? 'live' : null;
}

/**
 * Whether a shortcut runs in an editable field when it does not say: with
 * `Mod` or `Ctrl` it does, since those keys never type a character.
 */
export function tmRunsInFields(shortcut: TmShortcut, parts: TmKeyParts): boolean {
  return shortcut.inFields ?? (parts.mod || parts.ctrl);
}
