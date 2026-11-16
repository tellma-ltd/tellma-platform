// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The keys of a popup editor's pane (spec 0010 §C.15) as a pure keydown →
// pane action resolver, plus the two DOM tests the editing controller runs
// on the action: whether Enter stays with the pane's content, and whether a
// Tab leaves the pane. A pane only exists once this entry point has
// arrived, so its keys live here rather than in the grid's own keymap.

/** What the popup-pane key resolver needs to know. */
export interface TmGridPopupKeyContext {
  /** Whether the platform is Apple's, where Mod is ⌘ rather than Ctrl. */
  readonly apple: boolean;
  /** Whether a built-in editor's dropdown panel is open, which owns every key. */
  readonly isDropdownOpen: boolean;
}

/** A resolved key inside a popup editor's pane. */
export type TmGridPopupKey =
  | { readonly kind: 'cancel' }
  | { readonly kind: 'commit' }
  | { readonly kind: 'enter'; readonly backward: boolean }
  | { readonly kind: 'tab'; readonly backward: boolean }
  | { readonly kind: 'openDropdown' };

/**
 * Content that keeps Enter inside a popup editor's pane: an editable
 * multi-line field inserts a line, and a button, link or choice control
 * activates itself (editable text is matched by `isContentEditable`).
 */
const ENTER_OWNERS =
  'textarea:not([readonly]), button, a[href], select, [role="button"], [role="checkbox"], ' +
  '[role="radio"], [role="switch"], [role="tab"], [role="option"]';

/** What can be one of a pane's tab stops: interactive content and editable text. */
const TAB_STOP_CANDIDATES =
  'a[href], button, input, select, textarea, [tabindex], ' +
  '[contenteditable]:not([contenteditable="false"])';

/**
 * Resolves a keydown inside a popup editor's pane (that the editor did not
 * `preventDefault`) to a pane action, or `null` for keys that stay with the
 * editor. The pane's editor is always in edit mode: the arrows, Home, End
 * and the page keys move inside it and never commit, and F2 toggles
 * nothing. Escape cancels; Mod+Enter commits and keeps the cell; Enter and
 * Shift+Enter commit and move unless the editor keeps them (the caller
 * decides, as it decides whether Tab leaves the pane's first or last
 * control); Alt+ArrowDown opens a built-in editor's dropdown.
 */
export function tmResolvePopupKey(
  event: KeyboardEvent,
  ctx: TmGridPopupKeyContext,
): TmGridPopupKey | null {
  if (ctx.isDropdownOpen) {
    return null; // the open panel owns Enter, Escape, the arrows and typeahead
  }
  const key = event.key;
  const mod = ctx.apple ? event.metaKey : event.ctrlKey;
  const other = ctx.apple ? event.ctrlKey : event.metaKey;
  const alt = event.altKey;
  const shift = event.shiftKey;
  if (other) {
    return null;
  }
  if (key === 'ArrowDown' && alt && !mod && !shift) {
    return { kind: 'openDropdown' };
  }
  if (alt) {
    return null;
  }
  if (key === 'Enter') {
    if (mod) {
      return shift ? null : { kind: 'commit' };
    }
    return { kind: 'enter', backward: shift };
  }
  if (mod) {
    return null;
  }
  if (key === 'Escape') {
    return shift ? null : { kind: 'cancel' };
  }
  if (key === 'Tab') {
    return { kind: 'tab', backward: shift };
  }
  return null;
}

/**
 * Whether an Enter on `target` stays with the pane's content: a line break
 * in editable text, or the activation of a control that owns Enter.
 */
export function tmPopupKeepsEnter(target: Element): boolean {
  return (target instanceof HTMLElement && target.isContentEditable) || target.closest(ENTER_OWNERS) !== null;
}

/**
 * Whether a Tab from `target` leaves its popup pane: from the pane's last
 * tab stop forward, from its first one backward, or from anything that is
 * not one of its tab stops. `inPane` tells the pane's own stops from those
 * of an overlay nested inside it (an editor's open option list), which are
 * not the pane's.
 */
export function tmPopupTabLeaves(
  target: Element,
  backward: boolean,
  inPane: (element: Element) => boolean,
): boolean {
  const pane = target.closest('[data-tm-popup]');
  if (pane === null) {
    return true;
  }
  const stops = [...pane.querySelectorAll<HTMLElement>(TAB_STOP_CANDIDATES)].filter(
    (element) =>
      element.tabIndex >= 0 &&
      !element.matches(':disabled') &&
      element.closest('[inert]') === null &&
      element.getClientRects().length > 0 &&
      inPane(element),
  );
  const index = stops.findIndex((stop) => stop.contains(target));
  return index === -1 || index === (backward ? 0 : stops.length - 1);
}
