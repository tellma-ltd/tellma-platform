// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { LiveAnnouncer } from '@angular/cdk/a11y';
import { DestroyRef, inject } from '@angular/core';
import { TmLocalization } from '@tellma/core';

import { tmSearchCapturesKey, tmSearchInsert, tmSearchOnTop, tmSearchPastedText, type TmSearchFieldMode } from './tm-search-typing';

// The search page's keys (spec 0030 §5.2, §6.2): type-to-search, the pause
// the live region waits for, and the pointer presses that disarm a pending
// Enter. The listeners run in the bubble phase on the document, or on the
// picker modal's pane in picker mode, so focused components and the app's
// shortcuts see a key first.

/** How long the live region waits after the last key before it speaks, so it never speaks once per keystroke. */
const ANNOUNCE_QUIET_MS = 500;

/** Where the keys are listened for, and what they reach. */
export interface ɵTmSearchKeysOptions {
  /** Where the page runs. */
  readonly mode: TmSearchFieldMode;
  /** The document for a page or a list; the picker modal's pane for a picker. */
  readonly scope: Document | Element;
  /** The field's input, while it is in the document. */
  readonly field: () => HTMLInputElement | null;
  /** How many context-bearing dialogs are open over the page. */
  readonly overlays: () => number;
  /** A key or a paste was typed into the field. */
  readonly typed: () => void;
  /** A pointer pressed anywhere in the scope. */
  readonly pressed: () => void;
}

/**
 * The search page's keys: it types a printable key or a paste the page left
 * unhandled into the field, notes when the last key was pressed, and speaks
 * messages in the polite live region only after a pause in the keys; it
 * also copies the row menu's link and says so. Create
 * it in an injection context; it stops listening when that context is
 * destroyed.
 */
export class ɵTmSearchKeys {
  /** The live region. */
  readonly #announcer = inject(LiveAnnouncer);

  /** The strings it speaks. */
  readonly #l10n = inject(TmLocalization);

  /** When the last key was pressed, on the page's clock. */
  #keyAt = Number.NEGATIVE_INFINITY;

  /** The message waiting for a pause in the keys. */
  #waiting: ReturnType<typeof setTimeout> | undefined;

  /** What stops the listeners. */
  #stop: (() => void) | null = null;

  /** Stops listening and drops a waiting message when the context is destroyed. */
  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.#stop?.();
      this.hush();
    });
  }

  /**
   * Starts listening.
   *
   * @param options - Where to listen, and what the keys reach.
   */
  listen(options: ɵTmSearchKeysOptions): void {
    const { mode, scope } = options;
    const onTop = (): boolean => tmSearchOnTop(mode, scope, options.overlays());
    const typeIn = (input: HTMLInputElement, text: string): void => {
      options.typed();
      tmSearchInsert(input, text);
    };
    const keydown = (event: Event): void => {
      this.#keyAt = performance.now();
      const input = options.field();
      if (input !== null && tmSearchCapturesKey(event as KeyboardEvent, scope, onTop)) {
        event.preventDefault();
        typeIn(input, (event as KeyboardEvent).key);
      }
    };
    const paste = (event: Event): void => {
      const input = options.field();
      const text = input === null ? null : tmSearchPastedText(event as ClipboardEvent, scope, onTop);
      if (input !== null && text !== null) {
        event.preventDefault();
        typeIn(input, text);
      }
    };
    const press = (): void => options.pressed();
    scope.addEventListener('keydown', keydown);
    scope.addEventListener('paste', paste);
    scope.addEventListener('pointerdown', press, { capture: true, passive: true });
    this.#stop = () => {
      scope.removeEventListener('keydown', keydown);
      scope.removeEventListener('paste', paste);
      scope.removeEventListener('pointerdown', press, { capture: true });
    };
  }

  /**
   * Speaks a message in the polite live region once no key was pressed for
   * half a second, so typing never makes it speak once per key. A key
   * pressed while it waits puts it off again; a newer message replaces it.
   *
   * @param message - The message.
   */
  speak(message: string): void {
    this.hush();
    const wait = this.#keyAt + ANNOUNCE_QUIET_MS - performance.now();
    if (wait > 0) {
      this.#waiting = setTimeout(() => this.speak(message), wait);
    } else {
      void this.#announcer.announce(message, 'polite');
    }
  }

  /** Drops the message waiting for a pause, if any. */
  hush(): void {
    clearTimeout(this.#waiting);
  }

  /**
   * Copies a link to the clipboard and says whether it was copied. The
   * write starts synchronously, inside the gesture that asked for it, as
   * the clipboard requires.
   *
   * @param view - The page's window.
   * @param href - The link, absolute.
   */
  copyLink(view: Window | null, href: string): void {
    let written: Promise<void>;
    try {
      written = view!.navigator.clipboard.writeText(href);
    } catch (error) {
      // No clipboard here, such as on an insecure origin.
      written = Promise.reject(error);
    }
    const say = (key: 'linkCopied' | 'linkCopyFailed'): void => void this.#announcer.announce(this.#l10n.ɵtext(`tm.search.${key}`)(), 'polite');
    written.then(
      () => say('linkCopied'),
      () => say('linkCopyFailed'),
    );
  }
}


