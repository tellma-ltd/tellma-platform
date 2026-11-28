// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { ɵtmModalOpened } from '@tellma/core-ui/overlay';

import {
  tmSearchCapturesKey,
  tmSearchEditing,
  tmSearchInsert,
  tmSearchOnTop,
  tmSearchPastedText,
  tmSearchPrintable,
} from './tm-search-typing';

// The type-to-search predicate over key, modifier and target vectors, per
// mode and with a stacked overlay, then the paste rule and the insertion
// into the field.

/** The elements a key can be aimed at, inside a page fixture on the document. */
interface TmTargets {
  /** The page's root. */
  readonly page: HTMLElement;
  /** A readonly grid's cell. */
  readonly cell: HTMLElement;
  /** A button. */
  readonly button: HTMLButtonElement;
  /** A text input. */
  readonly text: HTMLInputElement;
  /** A checkbox, which takes no typed text. */
  readonly checkbox: HTMLInputElement;
  /** A textarea. */
  readonly textarea: HTMLTextAreaElement;
  /** A select. */
  readonly select: HTMLSelectElement;
  /** An element inside editable content. */
  readonly editable: HTMLElement;
}

/** The page fixture, rebuilt for each test. */
let targets: TmTargets;

/**
 * Builds the page fixture: a grid cell, a button and the field kinds.
 *
 * @returns The targets.
 */
function page(): TmTargets {
  const root = document.createElement('div');
  root.innerHTML = `
    <div role="grid"><div role="row"><div role="gridcell" tabindex="0" data-cell>Customer 1</div></div></div>
    <button type="button">Refresh</button>
    <input type="text" />
    <input type="checkbox" />
    <textarea></textarea>
    <select><option>One</option></select>
    <div contenteditable="true"><b data-editable>bold</b></div>
  `;
  document.body.append(root);
  return {
    page: root,
    cell: root.querySelector<HTMLElement>('[data-cell]')!,
    button: root.querySelector('button')!,
    text: root.querySelector('input[type="text"]')!,
    checkbox: root.querySelector('input[type="checkbox"]')!,
    textarea: root.querySelector('textarea')!,
    select: root.querySelector('select')!,
    editable: root.querySelector<HTMLElement>('[data-editable]')!,
  };
}

/**
 * Dispatches a keydown at a target and reports whether type-to-search captures it, the way the
 * document listener sees it: after the target's own listeners.
 *
 * @param target - Where the key is aimed.
 * @param init - The key and its modifiers.
 * @param onTop - Whether the page is on top.
 * @param handled - Whether a listener before type-to-search handles the key.
 * @returns Whether it captures the key.
 */
function captures(target: Element, init: KeyboardEventInit, onTop = true, handled = false): boolean {
  let captured: boolean | undefined;
  const listener = (event: Event): void => {
    captured = tmSearchCapturesKey(event as KeyboardEvent, document, () => onTop);
  };
  if (handled) {
    target.addEventListener('keydown', (event) => event.preventDefault(), { once: true });
  }
  document.addEventListener('keydown', listener);
  try {
    target.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init }));
  } finally {
    document.removeEventListener('keydown', listener);
  }
  return captured!;
}

/**
 * Attaches an overlay pane, as the CDK does for a popup, holding one element.
 *
 * @param parent - Where the pane goes: the document's body, or inside a picker.
 * @param role - The role of the pane's content.
 * @returns The pane.
 */
function pane(parent: Element, role: string): HTMLElement {
  const element = document.createElement('div');
  element.className = 'cdk-overlay-pane';
  const content = document.createElement('div');
  content.setAttribute('role', role);
  element.append(content);
  parent.append(element);
  return element;
}

describe('type-to-search', () => {
  beforeEach(() => {
    targets = page();
  });

  afterEach(() => {
    document.querySelectorAll('.cdk-overlay-pane').forEach((element) => element.remove());
    targets.page.remove();
  });

  describe('the printable rule', () => {
    it('takes one character, in any script, one grapheme of several code units included', () => {
      for (const key of ['a', 'Z', '7', 'é', 'ج', 'ሀ', '€', '😀', 'é', '🇸🇦']) {
        expect(tmSearchPrintable(key), key).toBe(true);
      }
    });

    it('refuses named keys, spaces, controls and two characters', () => {
      for (const key of ['', 'Enter', 'Tab', 'Escape', 'Dead', 'Unidentified', 'F2', 'ArrowDown', ' ', ' ', '　', '\t', '\u0007', '‍', 'ab']) {
        expect(tmSearchPrintable(key), JSON.stringify(key)).toBe(false);
      }
    });
  });

  describe('the key rule', () => {
    it('captures a printable key on a readonly grid cell, a button and the body', () => {
      expect(captures(targets.cell, { key: 'j' })).toBe(true);
      expect(captures(targets.button, { key: 'J', shiftKey: true })).toBe(true);
      expect(captures(document.body, { key: '٣' })).toBe(true);
      // A checkbox takes no typed text, so it qualifies as a button does.
      expect(captures(targets.checkbox, { key: 'x' })).toBe(true);
    });

    it('leaves a key that something handled, a composition, a repeat and the Process key', () => {
      expect(captures(targets.cell, { key: 'j' }, true, true)).toBe(false);
      expect(captures(targets.cell, { key: 'j', isComposing: true })).toBe(false);
      expect(captures(targets.cell, { key: 'j', repeat: true })).toBe(false);
      expect(captures(targets.cell, { key: 'Process' })).toBe(false);
    });

    it('leaves Space and named keys', () => {
      expect(captures(targets.cell, { key: ' ' })).toBe(false);
      expect(captures(targets.cell, { key: 'Enter' })).toBe(false);
      expect(captures(targets.cell, { key: 'F2' })).toBe(false);
      expect(captures(targets.cell, { key: 'Dead' })).toBe(false);
    });

    it('leaves command modifiers and takes AltGr, which types', () => {
      expect(captures(targets.cell, { key: 'c', ctrlKey: true })).toBe(false);
      expect(captures(targets.cell, { key: 'c', metaKey: true })).toBe(false);
      expect(captures(targets.cell, { key: 'c', altKey: true })).toBe(false);
      expect(captures(targets.cell, { key: 'c', ctrlKey: true, shiftKey: true })).toBe(false);
      // AltGr arrives with Ctrl and Alt on Windows, and alone elsewhere.
      expect(captures(targets.cell, { key: '@', ctrlKey: true, altKey: true, modifierAltGraph: true })).toBe(true);
      expect(captures(targets.cell, { key: '€', altKey: true, modifierAltGraph: true })).toBe(true);
      // Meta is never typing, AltGr or not.
      expect(captures(targets.cell, { key: '@', metaKey: true, modifierAltGraph: true })).toBe(false);
    });

    it('leaves a key aimed at a field or editable content', () => {
      expect(captures(targets.text, { key: 'j' })).toBe(false);
      expect(captures(targets.textarea, { key: 'j' })).toBe(false);
      expect(captures(targets.select, { key: 'j' })).toBe(false);
      expect(captures(targets.editable, { key: 'j' })).toBe(false);
    });

    it('leaves every key while a grid editor is open anywhere on the page', () => {
      const editor = document.createElement('tm-grid-editor-pane');
      targets.page.append(editor);
      expect(tmSearchEditing(targets.cell, document)).toBe(true);
      expect(captures(targets.cell, { key: 'j' })).toBe(false);
      editor.remove();
      expect(captures(targets.cell, { key: 'j' })).toBe(true);
    });

    it('leaves a key while the page is not on top', () => {
      expect(captures(targets.cell, { key: 'j' }, false)).toBe(false);
    });
  });

  describe('the page on top', () => {
    it('in page and list mode, is off under a context dialog, a modal and any attached popup but a tooltip', () => {
      for (const mode of ['page', 'list'] as const) {
        expect(tmSearchOnTop(mode, document, 0), mode).toBe(true);
        expect(tmSearchOnTop(mode, document, 1), mode).toBe(false);
      }
      const tooltip = pane(document.body, 'tooltip');
      expect(tmSearchOnTop('page', document, 0)).toBe(true);
      for (const role of ['menu', 'dialog', 'listbox']) {
        const popup = pane(document.body, role);
        expect(tmSearchOnTop('page', document, 0), role).toBe(false);
        popup.remove();
      }
      tooltip.remove();
      // An empty pane is a detached popup's leftover, not an open one.
      const empty = document.createElement('div');
      empty.className = 'cdk-overlay-pane';
      document.body.append(empty);
      expect(tmSearchOnTop('page', document, 0)).toBe(true);
      empty.remove();
      const host = document.createElement('div');
      const closed = ɵtmModalOpened(host);
      expect(tmSearchOnTop('page', document, 0)).toBe(false);
      closed();
      expect(tmSearchOnTop('page', document, 0)).toBe(true);
    });

    it('in picker mode, reads only the popups attached inside the picker, under the picker modal itself', () => {
      const picker = pane(document.body, 'dialog');
      const closed = ɵtmModalOpened(picker);
      try {
        // The picker is a modal and an overlay context of its own; neither blocks its own keys.
        expect(tmSearchOnTop('picker', picker, 1)).toBe(true);
        // A popup over the page outside the picker does not reach the picker's scope either.
        const outside = pane(document.body, 'tooltip');
        expect(tmSearchOnTop('picker', picker, 1)).toBe(true);
        outside.remove();
        // A menu opened inside the picker does.
        const menu = pane(picker, 'menu');
        expect(tmSearchOnTop('picker', picker, 1)).toBe(false);
        menu.remove();
        expect(tmSearchOnTop('picker', picker, 1)).toBe(true);
        // The page under the picker is not on top: the picker is a modal and a context over it.
        expect(tmSearchOnTop('page', document, 1)).toBe(false);
      } finally {
        closed();
        picker.remove();
      }
    });
  });

  describe('the paste rule', () => {
    /**
     * A paste of plain text aimed at a target.
     *
     * @param target - Where it is aimed.
     * @param text - The clipboard's plain text.
     * @param handled - Whether a listener before type-to-search handled it.
     * @param onTop - Whether the page is on top.
     * @returns The text type-to-search takes, or `null`.
     */
    function pasted(target: Element, text: string, handled = false, onTop = true): string | null {
      const data = new DataTransfer();
      data.setData('text/plain', text);
      let taken: string | null | undefined;
      const listener = (event: Event): void => {
        taken = tmSearchPastedText(event as ClipboardEvent, document, () => onTop);
      };
      if (handled) {
        target.addEventListener('paste', (event) => event.preventDefault(), { once: true });
      }
      document.addEventListener('paste', listener);
      try {
        target.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: data }));
      } finally {
        document.removeEventListener('paste', listener);
      }
      return taken!;
    }

    it('takes an unhandled paste on a grid cell or the body, its line breaks as spaces', () => {
      expect(pasted(targets.cell, 'Customer 12')).toBe('Customer 12');
      expect(pasted(document.body, 'Jeddah\r\nsales\nnorth\rwest')).toBe('Jeddah sales north west');
    });

    it('leaves a handled paste, one aimed at a field, one while the page is not on top, and one with no text', () => {
      expect(pasted(targets.cell, 'x', true)).toBeNull();
      expect(pasted(targets.text, 'x')).toBeNull();
      expect(pasted(targets.editable, 'x')).toBeNull();
      expect(pasted(targets.cell, 'x', false, false)).toBeNull();
      expect(pasted(targets.cell, ' \n ')).toBeNull();
    });
  });

  describe('the insertion', () => {
    it('replaces the whole text as one undoable edit that fires input', () => {
      const field = document.createElement('input');
      field.type = 'search';
      targets.page.append(field);
      field.value = 'jed';
      const inputs: string[] = [];
      field.addEventListener('input', () => inputs.push(field.value));
      tmSearchInsert(field, 'x');
      expect(field.value).toBe('x');
      expect(inputs).toEqual(['x']);
      expect(document.activeElement).toBe(field);
      // One undo brings the old text back.
      document.execCommand('undo');
      expect(field.value).toBe('jed');
    });

    it('falls back to setRangeText and a synthetic input event when the engine refuses the command', () => {
      const field = document.createElement('input');
      targets.page.append(field);
      field.value = 'jed';
      const inputs: string[] = [];
      field.addEventListener('input', () => inputs.push(field.value));
      const refuse = vi.spyOn(document, 'execCommand').mockReturnValue(false);
      try {
        tmSearchInsert(field, 'x');
      } finally {
        refuse.mockRestore();
      }
      expect(field.value).toBe('x');
      expect(inputs).toEqual(['x']);
      expect(field.selectionStart).toBe(1);
    });

    it('cuts a long paste to the field’s maxlength, never inside a surrogate pair', () => {
      const field = document.createElement('input');
      field.maxLength = 4;
      targets.page.append(field);
      tmSearchInsert(field, 'abc😀def');
      expect(field.value).toBe('abc');
    });
  });
});
