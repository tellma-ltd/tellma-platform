// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The editing branch of the grid's keyboard matrix: a pure keydown → action
// resolver for keys that reach the grid while an editor session is open.
// It lives with the editing code it serves, so a readonly grid never loads
// it; the navigation table stays in the grid's own keymap.

import type { TmGridMotion } from '@tellma/core-ui/grid-engine';

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
