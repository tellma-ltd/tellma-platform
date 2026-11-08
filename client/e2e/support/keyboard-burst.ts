// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Page } from '@playwright/test';

/**
 * Keyboard input delivered faster than the page renders.
 *
 * A barcode scanner, a text expander, a remote-desktop client or a main
 * thread that fell behind delivers keystrokes back to back: the commit key
 * can arrive before the page has rendered the effect of the keystroke
 * before it. Playwright's own keyboard sends each key as a separate round
 * trip, so a render may or may not slip in between, and a test built on it
 * would only sometimes exercise that case. These helpers dispatch the whole
 * burst inside ONE task of the page, so no render, effect or timer can run
 * between the keys: the case is exercised every time.
 *
 * Each key is dispatched to whatever holds focus at that moment (a key may
 * move focus, as type-to-edit does), as keydown then keyup. A printable key
 * whose keydown is not cancelled and whose target is a text input is
 * inserted at the caret with an `input` event: the browser's own default
 * action. Default actions beyond text insertion (Tab's focus move, a form
 * submit) do not run, so a test asserts what the page's handlers did.
 */

/**
 * Dispatches `keys` (KeyboardEvent `key` values: `'a'`, `'Enter'`, `'Tab'`)
 * in one task, with no render between them.
 */
export async function pressInOneTask(page: Page, keys: readonly string[]): Promise<void> {
  await page.evaluate(dispatchBurst, { keys, releaseName: null });
}

/**
 * Releases the story's held `name` responses and dispatches `keys` in the
 * same task. The released answers reach the page's state through promise
 * continuations, which run before any render can: the keys then meet an
 * answer that has arrived but is not on screen yet.
 */
export async function releaseAndPressInOneTask(
  page: Page,
  name: string,
  keys: readonly string[],
): Promise<void> {
  await page.evaluate(dispatchBurst, { keys, releaseName: name });
}

/** The arguments {@link dispatchBurst} runs with inside the page. */
interface BurstArgs {
  /** The keys to dispatch, in order. */
  readonly keys: readonly string[];
  /** The held-response gate to release first, or `null` for none. */
  readonly releaseName: string | null;
}

/**
 * The in-page body of both helpers. It is serialized into the page, so it
 * may reference nothing outside itself.
 */
async function dispatchBurst({ keys, releaseName }: BurstArgs): Promise<void> {
  // Release first: the same custom event the held-response controls answer.
  if (releaseName !== null) {
    const button = document.querySelector(`[data-testid="release-${releaseName}"]`);
    if (button === null) {
      throw new Error(`No held-response gate named "${releaseName}" on this page.`);
    }
    button.dispatchEvent(new Event('release'));
    // Promise continuations only: awaiting an already-settled promise never
    // yields to a task, so no render can run before the keys below.
    for (let i = 0; i < 20; i++) {
      await Promise.resolve();
    }
  }
  // The burst itself: every key in this same task.
  for (const key of keys) {
    const target = (document.activeElement ?? document.body) as HTMLElement;
    const init: KeyboardEventInit = { key, bubbles: true, cancelable: true, composed: true };
    const proceed = target.dispatchEvent(new KeyboardEvent('keydown', init));
    if (proceed && key.length === 1 && target instanceof HTMLInputElement) {
      // The browser's default action for a printable key: insert at the caret.
      const start = target.selectionStart ?? target.value.length;
      const end = target.selectionEnd ?? start;
      target.setRangeText(key, start, end, 'end');
      target.dispatchEvent(
        new InputEvent('input', { inputType: 'insertText', data: key, bubbles: true, composed: true }),
      );
    }
    target.dispatchEvent(new KeyboardEvent('keyup', init));
  }
}
