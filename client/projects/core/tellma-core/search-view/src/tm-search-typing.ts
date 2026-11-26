// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Type-to-search (spec 0030 §6.2): a printable key typed anywhere on the
// page that is not editing lands in the search field, as a new search, and
// so does a paste the page left unhandled. The listener runs in the bubble
// phase, so a focused component and the app's shortcuts see the key first
// and keep it by handling it. It listens on the document for a page or a
// drill-through list, and on the picker modal's pane in picker mode, which
// traps focus, so keys from layers above the modal never reach it. The key
// replaces the field's whole text through the browser's editing commands,
// so the input event runs the search as if typed, and one undo brings the
// old text back.

import { ɵtmOpenModals } from '@tellma/core-ui/overlay';

/** Where the search page runs: an entity's search page, a picker's advanced search, or a drill-through list. */
export type TmSearchFieldMode = 'page' | 'picker' | 'list';

/** The targets that edit text, where a key or a paste is the target's own: fields, and an open grid editor. */
const EDITING = 'input, textarea, select, [contenteditable]:not([contenteditable="false"]), .tm-grid__cell--editing, tm-grid-editor-pane';

/** The input types that take no typed text, which qualify as a button does. */
const TYPES_NOTHING = /^(checkbox|radio|button|submit|reset|image|file|range|color|hidden)$/;

/** A key that is not a character: a control or format character, or any kind of space. */
const NOT_TYPED = /^[\p{Cc}\p{Cf}\p{Z}\s]/u;

/** The open grid editors, which edit wherever focus is. */
const GRID_EDITORS = '.tm-grid__cell--editing, tm-grid-editor-pane';

/** The grapheme segmenter, created on the first multi-unit key. */
let graphemes: Intl.Segmenter | undefined;

/**
 * Whether a key's value is one printable character: one grapheme, not a
 * control or format character and not a space. A named key ("Enter",
 * "Dead", "Unidentified") is several graphemes, so it never qualifies.
 *
 * @param key - The keydown's `key`.
 * @returns Whether it is.
 */
export function tmSearchPrintable(key: string): boolean {
  if (key.length === 0 || NOT_TYPED.test(key)) {
    return false;
  }
  if (key.length === 1) {
    return true;
  }
  // A character outside the basic plane or a cluster (an accented letter,
  // a flag): one grapheme of several code units.
  graphemes ??= new Intl.Segmenter(undefined, { granularity: 'grapheme' });
  const segments = graphemes.segment(key)[Symbol.iterator]();
  return segments.next().done === false && segments.next().done === true;
}

/**
 * Whether a key or a paste aimed at a target edits text there: the target
 * is a field (an input that takes text, a textarea, a select, editable
 * content) or inside one, or a grid editor is open in the scope.
 *
 * @param target - The event's target.
 * @param scope - Where the listener listens: the document, or a picker's pane.
 * @returns Whether it does.
 */
export function tmSearchEditing(target: EventTarget | null, scope: ParentNode): boolean {
  if (target instanceof HTMLElement) {
    if (target.isContentEditable) {
      return true;
    }
    const field = target.closest(EDITING);
    if (field !== null && !(field instanceof HTMLInputElement && TYPES_NOTHING.test(field.type))) {
      return true;
    }
  }
  return scope.querySelector(GRID_EDITORS) !== null;
}

/**
 * Whether a popup is attached in a scope: a menu, a popover, a select's
 * list, a launcher or a dialog, by its overlay pane. A tooltip's pane does
 * not count: it shows over the page without taking its keys.
 *
 * @param scope - The document, or a picker's pane.
 * @returns Whether one is.
 */
export function tmSearchPopupAttached(scope: ParentNode): boolean {
  for (const pane of scope.querySelectorAll('.cdk-overlay-pane')) {
    if (pane.childElementCount > 0 && pane.querySelector('[role="tooltip"]') === null) {
      return true;
    }
  }
  return false;
}

/**
 * Whether the page is on top of its scope: for a page or a list, no
 * context-bearing dialog, modal or popup is open over it; for a picker, no
 * popup is attached inside its pane.
 *
 * @param mode - Where the page runs.
 * @param scope - The document for a page or a list; the picker's pane for a picker.
 * @param overlays - How many context-bearing dialogs are open.
 * @returns Whether it is.
 */
export function tmSearchOnTop(mode: TmSearchFieldMode, scope: ParentNode, overlays: number): boolean {
  if (mode !== 'picker' && (overlays > 0 || ɵtmOpenModals().length > 0)) {
    return false;
  }
  return !tmSearchPopupAttached(scope);
}

/**
 * Whether type-to-search captures a keydown: untouched by anything before
 * it, one printable character, typed without a command modifier, aimed at
 * a target that is not editing, while the page is on top.
 *
 * @param event - The keydown.
 * @param scope - Where the listener listens.
 * @param onTop - Whether the page is on top; asked last, as the costliest check.
 * @returns Whether it does.
 */
export function tmSearchCapturesKey(event: KeyboardEvent, scope: ParentNode, onTop: () => boolean): boolean {
  // 1. Untouched: nothing handled it, and it is neither part of a composition nor a repeat.
  if (event.defaultPrevented || event.isComposing || event.key === 'Process' || event.repeat) {
    return false;
  }
  // 2. One printable character.
  if (!tmSearchPrintable(event.key)) {
    return false;
  }
  // 3. No command modifier: Shift types, and AltGr (reported with Ctrl and Alt on Windows) types too.
  if (event.metaKey || ((event.ctrlKey || event.altKey) && !event.getModifierState('AltGraph'))) {
    return false;
  }
  // 4 and 5. Not editing, and the page on top.
  return !tmSearchEditing(event.target, scope) && onTop();
}

/**
 * The text type-to-search takes from a paste: the clipboard's plain text
 * with its line breaks as spaces, which a single-line field holds; `null`
 * when the paste is not the page's to take (handled already, aimed at a
 * field, the page not on top) or carries no text.
 *
 * @param event - The paste.
 * @param scope - Where the listener listens.
 * @param onTop - Whether the page is on top.
 * @returns The text, or `null`.
 */
export function tmSearchPastedText(event: ClipboardEvent, scope: ParentNode, onTop: () => boolean): string | null {
  if (event.defaultPrevented || tmSearchEditing(event.target, scope) || !onTop()) {
    return null;
  }
  const text = (event.clipboardData?.getData('text/plain') ?? '').replace(/\r\n?|\n/g, ' ');
  return text.trim() === '' ? null : text;
}

/**
 * Replaces a field's whole text as an edit: focuses it, selects the text and
 * inserts the new text through `document.execCommand('insertText')`, which
 * fires `input` and keeps one undo step. An engine that refuses the command
 * gets the text through `setRangeText` and a synthetic `input` event, which
 * still searches but is not undoable. The text is cut to the field's
 * `maxlength` first, never inside a surrogate pair.
 *
 * @param field - The search field.
 * @param text - The text.
 */
export function tmSearchInsert(field: HTMLInputElement, text: string): void {
  const max = field.maxLength;
  let inserted = text;
  if (max >= 0 && inserted.length > max) {
    inserted = inserted.slice(0, max);
    const last = inserted.charCodeAt(inserted.length - 1);
    if (last >= 0xd800 && last <= 0xdbff) {
      inserted = inserted.slice(0, -1);
    }
  }
  field.focus();
  field.select();
  let edited: boolean;
  try {
    // The deprecated-but-universal editing command is the only way to
    // change an input's text through the browser's undo history.
    edited = field.ownerDocument.execCommand('insertText', false, inserted);
  } catch {
    edited = false;
  }
  if (!edited) {
    field.setRangeText(inserted, 0, field.value.length, 'end');
    field.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: inserted }));
  }
}
