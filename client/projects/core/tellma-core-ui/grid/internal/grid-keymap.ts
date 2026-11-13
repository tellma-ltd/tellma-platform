// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The keyboard matrix as a pure keydown → intent resolver, unit-testable
// without any DOM or engine. The core executes intents; keys that must
// keep their native behavior (clipboard events, Tab out of a readonly
// grid) resolve to null.
//
// One binding table lists every key combination the grid handles in its
// navigation state (no editor open), with the intent it resolves to and its
// help label. The published key list is that table without the intents, so
// the two cannot drift apart (spec 0010 §C.12). Bindings on a letter, digit
// or symbol match through `tmKeysMatch`, which reads the physical key when a
// non-Latin layout changes the typed character; named keys keep the direct
// checks in `tmResolveGridKey`, held to exactly the modifiers the table
// lists.

import type { TmGridMotion } from '@tellma/core-ui/grid-engine';
import { tmKeysMatch, type TmKeys } from '@tellma/core-ui/kbd';

/** What the resolver needs to know about the grid's current state. */
export interface TmGridKeyContext {
  /** Whether the platform is Apple's, where Mod is ⌘ rather than Ctrl. */
  readonly apple: boolean;
  /** Whether the grid is editable right now. */
  readonly editable: boolean;
  /** Whether the find bar is enabled. */
  readonly searchable: boolean;
  /** Whether row checkbox selection is enabled. */
  readonly selectable: boolean;
  /** Whether the grid is a tree. */
  readonly isTree: boolean;
  /** Whether the active cell is a boolean column's cell. */
  readonly activeIsBoolean: boolean;
  /** Whether the readonly grid opens rows (row activation is on). */
  readonly rowActivation: boolean;
  /**
   * Whether focus is on a column header of a grid whose header row
   * navigates; absent, focus is on a cell. On a header, Alt+ArrowDown opens
   * the header's action rather than a cell's dropdown (the header's other
   * keys resolve through `tmResolveHeaderKey`).
   */
  readonly header?: boolean;
}

/** Where a header-row key moves the header focus. */
export type TmGridHeaderMotion = 'left' | 'right' | 'first' | 'last' | 'up' | 'down';

/** A resolved keyboard intent. */
export type TmGridIntent =
  | { readonly kind: 'move'; readonly motion: TmGridMotion; readonly extend: boolean; readonly jump: boolean }
  | { readonly kind: 'tab'; readonly backward: boolean }
  | { readonly kind: 'enter'; readonly backward: boolean }
  | {
      readonly kind: 'edit';
      readonly mode: 'edit' | 'enter';
      readonly seed?: string;
      /** The key asked for the cell's dropdown, not just its editor. */
      readonly dropdown?: boolean;
    }
  | { readonly kind: 'toggleBoolean' }
  | { readonly kind: 'activateRow'; readonly newTab: boolean }
  | { readonly kind: 'toggleCheck' }
  | { readonly kind: 'toggleSelectAllCheckbox' }
  | { readonly kind: 'clear' }
  | { readonly kind: 'selectRows' }
  | { readonly kind: 'selectCols' }
  | { readonly kind: 'selectAll' }
  | { readonly kind: 'undo' }
  | { readonly kind: 'redo' }
  | { readonly kind: 'fillDown' }
  | { readonly kind: 'deleteRows' }
  | { readonly kind: 'insertRowsAbove' }
  | { readonly kind: 'menu' }
  | { readonly kind: 'find' }
  | { readonly kind: 'escape' }
  | { readonly kind: 'expand' }
  | { readonly kind: 'collapse' }
  // The header row's own intents (focus on a column header).
  | { readonly kind: 'headerMove'; readonly motion: TmGridHeaderMotion }
  | { readonly kind: 'headerActivate' }
  | { readonly kind: 'headerAction' };

/**
 * The grid state a binding needs before it acts: an editable grid, the find
 * bar enabled, row checkboxes enabled, a tree, row activation, or focus on
 * a navigable column header.
 */
export type TmGridKeyCondition =
  | 'editable'
  | 'searchable'
  | 'selectable'
  | 'tree'
  | 'rowActivation'
  | 'header';

/** One row of the grid's binding table. */
export interface TmGridKeyBinding {
  /** The key combination. */
  readonly keys: TmKeys;
  /**
   * What the combination does with no editor open, or `null` for the
   * clipboard keys, which the grid leaves to the browser's clipboard events.
   */
  readonly intent: TmGridIntent | null;
  /** The full translation key of the help label, under `tmUi.grid.keys`. */
  readonly labelKey: string;
  /**
   * The grid state the binding needs; a list acts when any one of its
   * states holds. Absent, the binding always acts.
   */
  readonly when?: TmGridKeyCondition | readonly TmGridKeyCondition[];
}

/** The find shortcut: the grid, its open editor and the find bar all match it. */
export const TM_GRID_FIND_KEYS: TmKeys = 'Mod+F';

/** The physical arrows, each with the motion it starts. */
const ARROWS: readonly (readonly [key: string, motion: TmGridMotion])[] = [
  ['ArrowUp', 'up'],
  ['ArrowDown', 'down'],
  ['ArrowLeft', 'left'],
  ['ArrowRight', 'right'],
];

const ARROW_MOTIONS: Readonly<Record<string, TmGridMotion>> = Object.fromEntries(ARROWS);

/** A help label's full translation key. */
function label(name: string): string {
  return `tmUi.grid.keys.${name}`;
}

/** A move intent. */
function move(motion: TmGridMotion, extend: boolean, jump: boolean): TmGridIntent {
  return { kind: 'move', motion, extend, jump };
}

/** The four arrow bindings behind one modifier prefix. */
function arrowBindings(prefix: string, extend: boolean, jump: boolean, name: string): TmGridKeyBinding[] {
  return ARROWS.map(([key, motion]) => ({
    keys: `${prefix}${key}`,
    intent: move(motion, extend, jump),
    labelKey: label(name),
  }));
}

/**
 * The grid's binding table: every key combination it handles with no
 * editor open, in the order of the grid's keyboard reference, with the
 * intent each resolves to and its help label. Entries that share a label
 * form one help row.
 */
export const TM_GRID_KEY_BINDINGS: readonly TmGridKeyBinding[] = [
  // Section: moving the active cell.
  ...arrowBindings('', false, false, 'move'),
  ...arrowBindings('Mod+', false, true, 'jump'),
  { keys: 'PageUp', intent: move('pageUp', false, false), labelKey: label('page') },
  { keys: 'PageDown', intent: move('pageDown', false, false), labelKey: label('page') },
  { keys: 'Home', intent: move('rowStart', false, false), labelKey: label('rowEdge') },
  { keys: 'End', intent: move('rowEnd', false, false), labelKey: label('rowEdge') },
  { keys: 'Mod+Home', intent: move('gridStart', false, false), labelKey: label('gridEdge') },
  { keys: 'Mod+End', intent: move('gridEnd', false, false), labelKey: label('gridEdge') },

  // Section: extending the selection — Shift with every motion above.
  ...arrowBindings('Shift+', true, false, 'extend'),
  ...arrowBindings('Mod+Shift+', true, true, 'extend'),
  { keys: 'Shift+PageUp', intent: move('pageUp', true, false), labelKey: label('extend') },
  { keys: 'Shift+PageDown', intent: move('pageDown', true, false), labelKey: label('extend') },
  { keys: 'Shift+Home', intent: move('rowStart', true, false), labelKey: label('extend') },
  { keys: 'Shift+End', intent: move('rowEnd', true, false), labelKey: label('extend') },
  { keys: 'Mod+Shift+Home', intent: move('gridStart', true, false), labelKey: label('extend') },
  { keys: 'Mod+Shift+End', intent: move('gridEnd', true, false), labelKey: label('extend') },

  // Section: whole rows, columns and the grid. Column select and the
  // select-all toggle are literally Ctrl on every platform: ⌘+Space is
  // Spotlight, and Excel for Mac uses Ctrl+Space too.
  { keys: 'Shift+Space', intent: { kind: 'selectRows' }, labelKey: label('selectRows') },
  { keys: 'Ctrl+Space', intent: { kind: 'selectCols' }, labelKey: label('selectColumns') },
  {
    keys: 'Ctrl+Shift+Space',
    intent: { kind: 'toggleSelectAllCheckbox' },
    labelKey: label('checkAll'),
    when: 'selectable',
  },
  { keys: 'Space', intent: { kind: 'toggleCheck' }, labelKey: label('checkRow'), when: 'selectable' },
  { keys: 'Mod+A', intent: { kind: 'selectAll' }, labelKey: label('selectAll') },

  // Section: editing and the cell's own actions.
  { keys: 'Tab', intent: { kind: 'tab', backward: false }, labelKey: label('tab'), when: 'editable' },
  {
    keys: 'Shift+Tab',
    intent: { kind: 'tab', backward: true },
    labelKey: label('tab'),
    when: 'editable',
  },
  // Enter opens the row under row activation, which the core decides; Mod
  // asks the consumer for a new tab, so only row activation binds it.
  { keys: 'Enter', intent: { kind: 'enter', backward: false }, labelKey: label('enter') },
  {
    keys: 'Mod+Enter',
    intent: { kind: 'activateRow', newTab: true },
    labelKey: label('openRowNewTab'),
    when: 'rowActivation',
  },
  { keys: 'Shift+Enter', intent: { kind: 'enter', backward: true }, labelKey: label('moveUp') },
  // F2 edits an editable cell and, under row activation (always readonly),
  // reaches the cell's link or button instead.
  {
    keys: 'F2',
    intent: { kind: 'edit', mode: 'edit' },
    labelKey: label('edit'),
    when: ['editable', 'rowActivation'],
  },
  {
    keys: 'Alt+ArrowDown',
    intent: { kind: 'edit', mode: 'edit', dropdown: true },
    labelKey: label('openDropdown'),
    when: 'editable',
  },
  { keys: 'Delete', intent: { kind: 'clear' }, labelKey: label('clear'), when: 'editable' },
  { keys: 'Backspace', intent: { kind: 'clear' }, labelKey: label('clear'), when: 'editable' },
  { keys: 'Escape', intent: { kind: 'escape' }, labelKey: label('escape') },
  { keys: 'ContextMenu', intent: { kind: 'menu' }, labelKey: label('menu') },
  { keys: 'Shift+F10', intent: { kind: 'menu' }, labelKey: label('menu') },
  { keys: TM_GRID_FIND_KEYS, intent: { kind: 'find' }, labelKey: label('find'), when: 'searchable' },

  // Section: clipboard, history and rows. Copy, cut and paste are the
  // browser's clipboard events, which the grid handles without a keydown.
  { keys: 'Mod+C', intent: null, labelKey: label('copy') },
  { keys: 'Mod+X', intent: null, labelKey: label('cut') },
  { keys: 'Mod+V', intent: null, labelKey: label('paste') },
  { keys: 'Mod+D', intent: { kind: 'fillDown' }, labelKey: label('fillDown'), when: 'editable' },
  { keys: 'Mod+Z', intent: { kind: 'undo' }, labelKey: label('undo'), when: 'editable' },
  { keys: 'Mod+Y', intent: { kind: 'redo' }, labelKey: label('redo'), when: 'editable' },
  { keys: 'Mod+Shift+Z', intent: { kind: 'redo' }, labelKey: label('redo'), when: 'editable' },
  // Row operations take Alt: the bare Mod+Minus and Mod+Plus are browser zoom.
  {
    keys: 'Mod+Alt+Minus',
    intent: { kind: 'deleteRows' },
    labelKey: label('deleteRows'),
    when: 'editable',
  },
  {
    keys: 'Mod+Alt+Plus',
    intent: { kind: 'insertRowsAbove' },
    labelKey: label('insertRows'),
    when: 'editable',
  },
  {
    keys: 'Mod+Alt+=',
    intent: { kind: 'insertRowsAbove' },
    labelKey: label('insertRows'),
    when: 'editable',
  },

  // Section: the tree's additions. Alt+Arrow carries no caret meaning; the
  // core prevents the browser's history navigation on it.
  { keys: 'Alt+ArrowRight', intent: { kind: 'expand' }, labelKey: label('expand'), when: 'tree' },
  { keys: 'Alt+ArrowLeft', intent: { kind: 'collapse' }, labelKey: label('collapse'), when: 'tree' },

  // Section: the header row's addition (spec 0010 §C.4). A header's other
  // keys are plain moves and Enter, which the rows above already list.
  {
    keys: 'Alt+ArrowDown',
    intent: { kind: 'headerAction' },
    labelKey: label('headerAction'),
    when: 'header',
  },
];

/**
 * Whether a binding's key is a letter, digit or symbol rather than a named
 * key: one character, or the `Plus` and `Minus` words (`Space` is a named
 * key here, the same on every layout).
 */
function isCharacterKey(keys: TmKeys): boolean {
  const key = keys.slice(keys.lastIndexOf('+') + 1);
  return [...key].length === 1 || /^(plus|minus)$/i.test(key);
}

// The bindings matched through kbd, filtered once: a keydown walks a dozen
// entries whose parsed combinations kbd has cached.
const CHARACTER_BINDINGS: readonly TmGridKeyBinding[] = TM_GRID_KEY_BINDINGS.filter((binding) =>
  isCharacterKey(binding.keys),
);

/** Whether a binding's condition (any one of a list) holds in the grid's current state. */
function conditionHolds(
  when: TmGridKeyCondition | readonly TmGridKeyCondition[] | undefined,
  ctx: TmGridKeyContext,
): boolean {
  if (Array.isArray(when)) {
    return when.some((one: TmGridKeyCondition) => conditionHolds(one, ctx));
  }
  switch (when) {
    case 'editable':
      return ctx.editable;
    case 'searchable':
      return ctx.searchable;
    case 'selectable':
      return ctx.selectable;
    case 'tree':
      return ctx.isTree;
    case 'rowActivation':
      return ctx.rowActivation;
    case 'header':
      return ctx.header === true;
    default:
      return true;
  }
}

/**
 * Resolves a grid-focused keydown (no editor open) to an intent, or `null`
 * for keys the grid leaves alone: native clipboard shortcuts (the
 * ClipboardEvent path handles them), Tab in a readonly grid (the single
 * tab stop exits natively), browser zoom, a binding whose condition does
 * not hold, a named key with modifiers the table does not list, and
 * everything unrecognized.
 */
export function tmResolveGridKey(event: KeyboardEvent, ctx: TmGridKeyContext): TmGridIntent | null {
  // Section: letters, digits and symbols, matched through kbd so they work
  // on every keyboard layout (Ctrl+D sends 'ي' on an Arabic layout).
  for (const binding of CHARACTER_BINDINGS) {
    if (tmKeysMatch(event, binding.keys, ctx.apple)) {
      return binding.intent !== null && conditionHolds(binding.when, ctx) ? binding.intent : null;
    }
  }

  // Section: the modifiers of the named keys, with Mod resolved for the
  // platform. `other` is the control key that is not Mod (Ctrl on Apple,
  // the Windows or Super key elsewhere), which no named binding uses.
  const key = event.key;
  const mod = ctx.apple ? event.metaKey : event.ctrlKey;
  const other = ctx.apple ? event.ctrlKey : event.metaKey;
  const alt = event.altKey;
  const shift = event.shiftKey;

  // Section: Alt+Arrow — the tree's expand and collapse, and the cell's
  // dropdown.
  if (alt && !mod && !other && !shift) {
    if (key === 'ArrowRight' || key === 'ArrowLeft') {
      return ctx.isTree ? { kind: key === 'ArrowRight' ? 'expand' : 'collapse' } : null;
    }
    if (key === 'ArrowDown') {
      if (ctx.header === true) {
        return { kind: 'headerAction' };
      }
      // Routed as an edit intent that carries the ask, so ONE press reaches
      // the panel or calendar (F2 on the same cell opens the editor without it).
      return ctx.editable ? { kind: 'edit', mode: 'edit', dropdown: true } : null;
    }
  }

  // Section: motions — Shift extends, Mod jumps (arrows) or reaches the
  // grid's corners (Home and End).
  const arrowMotion = ARROW_MOTIONS[key];
  if (arrowMotion !== undefined) {
    return alt || other ? null : move(arrowMotion, shift, mod);
  }
  if (key === 'PageUp' || key === 'PageDown') {
    return alt || mod || other ? null : move(key === 'PageUp' ? 'pageUp' : 'pageDown', shift, false);
  }
  if (key === 'Home' || key === 'End') {
    if (alt || other) {
      return null;
    }
    const motion: TmGridMotion = mod
      ? key === 'Home'
        ? 'gridStart'
        : 'gridEnd'
      : key === 'Home'
        ? 'rowStart'
        : 'rowEnd';
    return move(motion, shift, false);
  }

  // Section: Space — whole rows and columns (Ctrl literally, see the
  // table), the row checkbox, a boolean cell, or type-to-edit.
  if (key === ' ') {
    if (alt || event.metaKey) {
      return null;
    }
    if (event.ctrlKey) {
      if (shift) {
        return ctx.selectable ? { kind: 'toggleSelectAllCheckbox' } : null;
      }
      return { kind: 'selectCols' };
    }
    if (shift) {
      return { kind: 'selectRows' };
    }
    if (ctx.selectable) {
      return { kind: 'toggleCheck' };
    }
    if (ctx.activeIsBoolean && ctx.editable) {
      return { kind: 'toggleBoolean' };
    }
    return ctx.editable ? { kind: 'edit', mode: 'enter', seed: ' ' } : null;
  }

  // Section: Mod+Enter — open the row in a new tab, under row activation.
  if (key === 'Enter' && mod && !alt && !other && !shift) {
    return ctx.rowActivation ? { kind: 'activateRow', newTab: true } : null;
  }

  // Section: the remaining named keys, none of which takes Alt or Mod;
  // only Tab, Enter and F10 take Shift.
  if (!alt && !mod && !other) {
    switch (key) {
      case 'Tab':
        // Readonly grids are a single tab stop — the browser moves focus out.
        return ctx.editable ? { kind: 'tab', backward: shift } : null;
      case 'Enter':
        if (ctx.activeIsBoolean && ctx.editable && !shift) {
          return { kind: 'toggleBoolean' };
        }
        return { kind: 'enter', backward: shift };
      case 'F10':
        return shift ? { kind: 'menu' } : null;
      default:
        break;
    }
    if (!shift) {
      switch (key) {
        case 'F2':
          return ctx.editable || ctx.rowActivation ? { kind: 'edit', mode: 'edit' } : null;
        case 'Delete':
        case 'Backspace':
          return ctx.editable ? { kind: 'clear' } : null;
        case 'Escape':
          return { kind: 'escape' };
        case 'ContextMenu':
          return { kind: 'menu' };
        default:
          break;
      }
    }
  }

  // Section: type-to-edit. Any typed character replaces the cell content,
  // and only a typed one (see tmIsTypedText).
  if (tmIsTypedText(event, ctx.apple) && ctx.editable) {
    if (ctx.activeIsBoolean) {
      return null; // boolean cells toggle via Enter/Space only
    }
    return { kind: 'edit', mode: 'enter', seed: key };
  }
  return null;
}

/** What the header-key resolver needs to know about the grid. */
export interface TmGridHeaderKeyContext {
  /** Whether the platform is Apple's, where Mod is ⌘ rather than Ctrl. */
  readonly apple: boolean;
  /** Whether the find bar is enabled. */
  readonly searchable: boolean;
}

/**
 * Resolves a keydown on a navigable column header to an intent, or `null`
 * for keys the header leaves alone — Tab among them, so the grid stays a
 * single tab stop, and every typed character. The horizontal arrows are
 * PHYSICAL (`left`/`right`); the core maps them through the reading
 * direction.
 */
export function tmResolveHeaderKey(
  event: KeyboardEvent,
  ctx: TmGridHeaderKeyContext,
): TmGridIntent | null {
  if (ctx.searchable && tmKeysMatch(event, TM_GRID_FIND_KEYS, ctx.apple)) {
    return { kind: 'find' };
  }
  const key = event.key;
  const shift = event.shiftKey;
  // Alt+ArrowDown opens the header's action; no other key takes Alt.
  if (event.altKey) {
    return key === 'ArrowDown' && !event.ctrlKey && !event.metaKey && !shift
      ? { kind: 'headerAction' }
      : null;
  }
  if (event.ctrlKey || event.metaKey || shift) {
    return null;
  }
  switch (key) {
    case 'ArrowLeft':
      return { kind: 'headerMove', motion: 'left' };
    case 'ArrowRight':
      return { kind: 'headerMove', motion: 'right' };
    case 'Home':
      return { kind: 'headerMove', motion: 'first' };
    case 'End':
      return { kind: 'headerMove', motion: 'last' };
    case 'ArrowUp':
      return { kind: 'headerMove', motion: 'up' };
    case 'ArrowDown':
      return { kind: 'headerMove', motion: 'down' };
    case 'Enter':
    case ' ':
      return { kind: 'headerActivate' };
    case 'Escape':
      return { kind: 'escape' };
    default:
      return null;
  }
}

/**
 * Whether a keydown types a character: a single-character key with no
 * modifier that makes it a chord. Plain typing (Shift picks the capital or
 * symbol) types; off Apple so does AltGraph, which Windows reports as
 * Ctrl+Alt with AltGraph and Linux as AltGraph alone, so `@ € [ { \` on
 * German, French and Nordic layouts type; on Apple, Option without Cmd or
 * Ctrl types (é, ≠). A bare Alt or Ctrl+Alt chord, and Ctrl+letter on
 * Apple (the emacs caret family), never type, so they reach the page's own
 * shortcuts.
 */
export function tmIsTypedText(event: KeyboardEvent, apple: boolean): boolean {
  // A chord the table does not list must reach the page's own shortcuts
  // (spec 0027 §4.3's Alt+Shift+<letter> family among them), so it is never
  // mistaken for text.
  const plain = !event.ctrlKey && !event.altKey && !event.metaKey;
  const typed = apple
    ? !event.ctrlKey && !event.metaKey
    : plain || event.getModifierState('AltGraph');
  return event.key.length === 1 && typed;
}

/** What the editing-key resolver needs to know about the open session. */
export interface TmGridEditingKeyContext {
  /**
   * The session's mode: `edit` leaves the horizontal arrows to the caret;
   * `enter` commits and moves on them (Excel).
   */
  readonly mode: 'edit' | 'enter';
  /**
   * Whether the editor's dropdown panel is open. An open panel owns every
   * editing key except F2 (it consumes them with `preventDefault` anyway;
   * this is the resolver-level belt to that brace).
   */
  readonly isDropdownOpen: boolean;
}

/** Where a commit-and-move editing key sends the selection after the commit. */
export type TmGridEditingCommitTarget =
  | TmGridMotion
  | 'tabNext'
  | 'tabPrev'
  | 'enterRun'
  | 'enterRunBack';

/** A resolved editing-branch keyboard action. */
export type TmGridEditingKey =
  | { readonly kind: 'commitMove'; readonly target: TmGridEditingCommitTarget }
  | { readonly kind: 'cancel' }
  | { readonly kind: 'toggleMode' }
  | { readonly kind: 'openDropdown' };

/**
 * Resolves a keydown that reached the grid while an editor session is open
 * (and that the editor did not `preventDefault`) to an editing action, or
 * `null` for keys that stay with the editor: plain characters, the caret
 * arrows in *edit* mode, and everything a dropdown panel owns while open.
 * Arrow motions are PHYSICAL (`left`/`right`) — the engine's navigation
 * maps them through the reading direction.
 */
export function tmResolveEditingKey(
  event: KeyboardEvent,
  ctx: TmGridEditingKeyContext,
): TmGridEditingKey | null {
  const key = event.key;
  if (key === 'F2') {
    return { kind: 'toggleMode' };
  }
  if (ctx.isDropdownOpen) {
    // The open panel owns Enter/Esc/arrows/typeahead — the grid stays out.
    return null;
  }
  if (key === 'Escape') {
    return { kind: 'cancel' };
  }
  if (key === 'Tab') {
    return { kind: 'commitMove', target: event.shiftKey ? 'tabPrev' : 'tabNext' };
  }
  if (key === 'Enter') {
    return { kind: 'commitMove', target: event.shiftKey ? 'enterRunBack' : 'enterRun' };
  }
  if (key === 'ArrowDown' && event.altKey && !event.ctrlKey && !event.metaKey) {
    return { kind: 'openDropdown' };
  }
  if (event.altKey || event.ctrlKey || event.metaKey) {
    return null; // modified arrows stay with the editor (caret/word motion)
  }
  switch (key) {
    case 'ArrowUp':
      return { kind: 'commitMove', target: 'up' };
    case 'ArrowDown':
      return { kind: 'commitMove', target: 'down' };
    case 'PageUp':
      return { kind: 'commitMove', target: 'pageUp' };
    case 'PageDown':
      return { kind: 'commitMove', target: 'pageDown' };
    case 'ArrowLeft':
      // Physical arrow; the nav maps direction like the navigation branch.
      return ctx.mode === 'enter' ? { kind: 'commitMove', target: 'left' } : null;
    case 'ArrowRight':
      return ctx.mode === 'enter' ? { kind: 'commitMove', target: 'right' } : null;
    default:
      return null;
  }
}
