// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import {
  booleanAttribute,
  DestroyRef,
  Directive,
  DOCUMENT,
  effect,
  ElementRef,
  inject,
  input,
  model,
  numberAttribute,
  output,
  untracked,
  type OnInit,
} from '@angular/core';

/**
 * Search-as-you-type behaviour for a native `<input>`: the timing of the
 * `search` output, an undoable clear, and the search input's attributes
 * (`type="search"`, `dir="auto"`, `enterkeyhint="search"`). `tm-search-field`
 * is this directive in field chrome; put it on an input directly for
 * custom chrome, such as a launcher.
 *
 * `value` updates on every input event, for instant feedback. `search`
 * emits the query on both edges of a burst of typing:
 *
 * - at once on the first input after a pause of `debounce` ms or more
 *   (the leading edge);
 * - once more `debounce` ms after the last input of the burst that follows
 *   (the trailing edge);
 * - while a key is held down (auto-repeat), only after it is released;
 * - at once on Enter and on clear;
 * - during IME composition, only after the composition ends;
 * - never with the value it emitted last.
 *
 * Escape clears the text while there is text to clear (unless
 * `escapeClears` is `false`), and otherwise propagates, so an enclosing
 * dialog or launcher still closes on it. Clearing runs as an edit — the
 * text is selected and deleted with `document.execCommand('delete')` — so
 * Ctrl+Z (Cmd+Z on Apple) brings it back as one undo step.
 *
 * Apply the `tm-search` global stylesheet: it hides the engine's own
 * cancel button, which would clear the text outside that undo history.
 *
 * @tmGroup form-control
 * @tmA11yNotes A native search input: Enter searches at once; Escape clears
 *   non-empty text and otherwise propagates; the clear is undoable.
 */
@Directive({
  selector: 'input[tmSearch]',
  exportAs: 'tmSearch',
  host: {
    class: 'tm-search',
    type: 'search',
    dir: 'auto',
    enterkeyhint: 'search',
    autocomplete: 'off',
    '(input)': 'onInput($event)',
    '(keydown)': 'onKeydown($event)',
    '(keyup)': 'onKeyup()',
    '(compositionstart)': 'onCompositionStart()',
    '(compositionend)': 'onCompositionEnd()',
    '(blur)': 'onBlur()',
  },
})
export class TmSearch implements OnInit {
  private readonly element = inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private readonly document = inject(DOCUMENT);

  /** The text, updated on every input event. */
  readonly value = model('');
  // The name the platform's specs give this output; the engines' own
  // `search` event on a search input is stopped at its target (see the
  // constructor).
  /* eslint-disable @angular-eslint/no-output-native -- named by the spec */
  /** The query, on the leading and trailing edges of a burst of typing, on Enter and on clear. */
  readonly search = output<string>();
  /* eslint-enable @angular-eslint/no-output-native */
  /** The coalescing window in milliseconds; `0` emits on every input. Default 50. */
  readonly debounce = input(50, { transform: numberAttribute });
  /** Whether Escape clears non-empty text (it then stops there). Default `true`. */
  readonly escapeClears = input(true, { transform: booleanAttribute });

  /** The value `search` emitted last; seeded with the initial value, which counts as searched. */
  private lastEmitted = '';
  /** The pending trailing edge; `undefined` while no burst is open. */
  private trailing: ReturnType<typeof setTimeout> | undefined;
  /** A key is auto-repeating: emission waits for its release. */
  private holding = false;
  /** An IME composition is in progress: emission waits for its end. */
  private composing = false;
  /** This directive's own clear is running: its input event is not typing. */
  private clearing = false;

  constructor() {
    // External writes (a reset, a restored query) reach the element; the
    // directive's own writes already match it, so the caret never jumps.
    effect(() => {
      const value = this.value() ?? '';
      if (this.element.value !== value) {
        this.element.value = value;
      }
    });
    // The engines' own `search` event (Enter, or their cancel button) would
    // reach every `(search)` listener on this element alongside the output,
    // carrying an Event instead of the query. A capture listener at the
    // target runs before them and stops it there.
    const stopNativeSearch = (event: Event): void => event.stopImmediatePropagation();
    this.element.addEventListener('search', stopNativeSearch, { capture: true });
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.trailing);
      this.element.removeEventListener('search', stopNativeSearch, { capture: true });
    });
  }

  /** Seeds the last-emitted value with the initial text: it needs no search of its own. */
  ngOnInit(): void {
    this.lastEmitted = untracked(this.value) ?? '';
  }

  /** Emits now when the text differs from the value emitted last, and closes any open burst. */
  flush(): void {
    clearTimeout(this.trailing);
    this.trailing = undefined;
    const value = this.element.value;
    if (value !== this.lastEmitted) {
      this.lastEmitted = value;
      this.search.emit(value);
    }
  }

  /**
   * Clears the text as an edit (undoable with Ctrl+Z / Cmd+Z), emits the
   * empty query at once, and leaves focus in the input. Does nothing while
   * the text is already empty.
   */
  clear(): void {
    const element = this.element;
    if (element.value === '') {
      return;
    }
    element.focus();
    element.select();
    this.clearing = true;
    let edited: boolean;
    try {
      // The deprecated-but-universal editing command is the only way to
      // change an input's text through the browser's undo history.
      edited = this.document.execCommand('delete');
    } finally {
      this.clearing = false;
    }
    if (!edited || element.value !== '') {
      // An engine that refuses the command still clears, just not undoably.
      element.value = '';
    }
    this.value.set('');
    this.flush();
  }

  // ---- Event handlers ----

  /** Mirrors the text into `value`, then applies the edge timing. */
  protected onInput(event: Event): void {
    this.value.set(this.element.value);
    if (this.clearing || this.composing || (event as InputEvent).isComposing === true) {
      return;
    }
    this.schedule();
  }

  /**
   * Enter emits at once; Escape clears non-empty text; an auto-repeating
   * key holds emission until it is released.
   */
  protected onKeydown(event: KeyboardEvent): void {
    // Key events that belong to an IME composition (keyCode 229 in the
    // engines that report one) are the composition's, never commands.
    if (event.isComposing || event.keyCode === 229) {
      return;
    }
    if (event.key === 'Enter') {
      this.flush();
    } else if (event.key === 'Escape') {
      if (untracked(this.escapeClears) && this.element.value !== '') {
        // Handled here: the engine's own non-undoable clear must not run,
        // and nothing outside (a dialog) may act on the same press.
        event.preventDefault();
        event.stopPropagation();
        this.clear();
      }
    } else if (event.repeat && !this.holding) {
      this.holding = true;
      // The burst's trailing edge would fire mid-hold; the release emits.
      clearTimeout(this.trailing);
      this.trailing = undefined;
    }
  }

  /** The release of a held key emits what the hold typed. */
  protected onKeyup(): void {
    if (this.holding) {
      this.holding = false;
      this.flush();
    }
  }

  /** An IME composition holds emission until it ends. */
  protected onCompositionStart(): void {
    this.composing = true;
  }

  /** The end of a composition is the input the composition stood for. */
  protected onCompositionEnd(): void {
    this.composing = false;
    this.value.set(this.element.value);
    this.schedule();
  }

  /** Leaving the input mid-hold releases the hold (no keyup will reach it). */
  protected onBlur(): void {
    if (this.holding) {
      this.holding = false;
      this.flush();
    }
  }

  /**
   * The edge timing of one input: the first input after a pause emits at
   * once and opens a burst; every input restarts the burst's window, and
   * the window's end emits the burst's final text.
   */
  private schedule(): void {
    if (this.holding) {
      return;
    }
    const window = untracked(this.debounce);
    if (!(window > 0)) {
      this.flush();
      return;
    }
    const leading = this.trailing === undefined;
    clearTimeout(this.trailing);
    this.trailing = setTimeout(() => this.flush(), window);
    if (leading) {
      const value = this.element.value;
      if (value !== this.lastEmitted) {
        this.lastEmitted = value;
        this.search.emit(value);
      }
    }
  }
}
