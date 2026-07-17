// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The keyboard matrix as a pure keydown → intent resolver, unit-testable
// without any DOM or engine. The core executes intents; keys that must
// keep their native behavior (clipboard events, Tab out of a readonly
// grid) resolve to null.

import type { TmGridMotion } from '@tellma/core-ui/grid-engine';

/** What the resolver needs to know about the grid's current state. */
export interface TmGridKeyContext {
  /** Whether the platform modifier is ⌘ (macOS) instead of Ctrl. */
  readonly isMac: boolean;
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
}

/** A resolved keyboard intent. */
export type TmGridIntent =
  | { readonly kind: 'move'; readonly motion: TmGridMotion; readonly extend: boolean; readonly jump: boolean }
  | { readonly kind: 'tab'; readonly backward: boolean }
  | { readonly kind: 'enter'; readonly backward: boolean }
  | { readonly kind: 'edit'; readonly mode: 'edit' | 'enter'; readonly seed?: string }
  | { readonly kind: 'toggleBoolean' }
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
  | { readonly kind: 'collapse' };

const ARROW_MOTIONS: Record<string, TmGridMotion> = {
  ArrowUp: 'up',
  ArrowDown: 'down',
  ArrowLeft: 'left',
  ArrowRight: 'right',
};

/**
 * Resolves a grid-focused keydown (no editor open) to an intent, or `null`
 * for keys the grid leaves alone: native clipboard shortcuts (the
 * ClipboardEvent path handles them), Tab in a readonly grid (the single
 * tab stop exits natively), browser zoom, and everything unrecognized.
 */
export function tmResolveGridKey(event: KeyboardEvent, ctx: TmGridKeyContext): TmGridIntent | null {
  const mod = ctx.isMac ? event.metaKey : event.ctrlKey;
  const key = event.key;

  // Tree expand/collapse (Alt+Arrow is free of text-caret meaning; browsers
  // use it for history navigation, which the core preventDefaults away).
  if (ctx.isTree && event.altKey && !mod && (key === 'ArrowRight' || key === 'ArrowLeft')) {
    return { kind: key === 'ArrowRight' ? 'expand' : 'collapse' };
  }
  if (key === 'ArrowDown' && event.altKey && !mod) {
    // Alt+ArrowDown opens dropdown editors — routed as an edit intent.
    return ctx.editable ? { kind: 'edit', mode: 'edit' } : null;
  }

  const arrowMotion = ARROW_MOTIONS[key];
  if (arrowMotion !== undefined && !event.altKey) {
    return { kind: 'move', motion: arrowMotion, extend: event.shiftKey, jump: mod };
  }
  if (key === 'PageUp' || key === 'PageDown') {
    return {
      kind: 'move',
      motion: key === 'PageUp' ? 'pageUp' : 'pageDown',
      extend: event.shiftKey,
      jump: false,
    };
  }
  if (key === 'Home' || key === 'End') {
    const motion: TmGridMotion = mod
      ? key === 'Home'
        ? 'gridStart'
        : 'gridEnd'
      : key === 'Home'
        ? 'rowStart'
        : 'rowEnd';
    return { kind: 'move', motion, extend: event.shiftKey, jump: false };
  }

  if (key === ' ') {
    // Column select is LITERALLY Ctrl on every platform (⌘+Space is
    // Spotlight; Excel for Mac uses Ctrl+Space too).
    if (event.ctrlKey && event.shiftKey) {
      return ctx.selectable ? { kind: 'toggleSelectAllCheckbox' } : null;
    }
    if (event.ctrlKey) {
      return { kind: 'selectCols' };
    }
    if (event.shiftKey) {
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

  if (mod && !event.altKey) {
    switch (key.toLowerCase()) {
      case 'a':
        return { kind: 'selectAll' };
      case 'z':
        return event.shiftKey ? { kind: 'redo' } : { kind: 'undo' };
      case 'y':
        return { kind: 'redo' };
      case 'd':
        return ctx.editable ? { kind: 'fillDown' } : null;
      case 'f':
        return ctx.searchable ? { kind: 'find' } : null;
      default:
        return null; // incl. c/x/v — the native clipboard events handle those
    }
  }

  // Row operations use the Alt-modified bindings (bare Mod+Minus/Plus are
  // browser zoom).
  if (mod && event.altKey && ctx.editable) {
    if (key === '-') {
      return { kind: 'deleteRows' };
    }
    if (key === '+' || key === '=') {
      return { kind: 'insertRowsAbove' };
    }
  }

  switch (key) {
    case 'Tab':
      // Readonly grids are a single tab stop — the browser moves focus out.
      return ctx.editable ? { kind: 'tab', backward: event.shiftKey } : null;
    case 'Enter':
      if (ctx.activeIsBoolean && ctx.editable && !event.shiftKey) {
        return { kind: 'toggleBoolean' };
      }
      return { kind: 'enter', backward: event.shiftKey };
    case 'F2':
      return ctx.editable ? { kind: 'edit', mode: 'edit' } : null;
    case 'Delete':
    case 'Backspace':
      return ctx.editable ? { kind: 'clear' } : null;
    case 'Escape':
      return { kind: 'escape' };
    case 'ContextMenu':
      return { kind: 'menu' };
    case 'F10':
      return event.shiftKey ? { kind: 'menu' } : null;
    default:
      break;
  }

  // Type-to-edit: any printable character replaces the cell content.
  // AltGr arrives as ctrl+alt on Windows — those characters still count.
  const printable = key.length === 1 && !mod && (!event.altKey || event.ctrlKey);
  if (printable && ctx.editable) {
    if (ctx.activeIsBoolean) {
      return null; // boolean cells toggle via Enter/Space only
    }
    return { kind: 'edit', mode: 'enter', seed: key };
  }
  return null;
}
