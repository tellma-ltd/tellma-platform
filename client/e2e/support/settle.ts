// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type Locator, type Page } from '@playwright/test';

/**
 * Settling helpers: waits on the page's own completion signals instead of on
 * the clock. A fixed sleep measures how fast the runner happens to be; these
 * wait exactly as long as the page needs, however slow the machine.
 */

/** The page-side shape of the showcase's stability hook. */
interface StabilityHookWindow {
  /** Resolves once the app has no scheduled render and no pending task. */
  readonly tmShowcaseWhenStable?: () => Promise<void>;
}

/**
 * How long {@link appStable} waits before it fails. A settled render takes
 * milliseconds; a page that is still unstable after this long has a task
 * that never ends (a pending request, a render loop), and the failure says
 * so instead of surfacing as an anonymous test timeout.
 */
const APP_STABLE_TIMEOUT_MS = 10_000;

/**
 * Waits until the showcase app is stable: every render that the preceding
 * actions scheduled has run, effects included. Fails, naming the cause,
 * when the app is not stable within {@link APP_STABLE_TIMEOUT_MS}.
 *
 * Use it where the DOM alone cannot tell a settled page from one whose next
 * render is still queued. A keystroke updates component state at once but
 * the DOM only on the following render, so until then the DOM still shows
 * the previous state: an assertion on a value the previous state shares
 * (the same highlighted row for "Alic" and "Alice", the same counter text)
 * passes early, and the next key then meets state the page has not drawn.
 * On a starved runner that window grows from a frame to hundreds of
 * milliseconds.
 */
export async function appStable(page: Page): Promise<void> {
  await page.evaluate(async (timeoutMs) => {
    const hook = (window as StabilityHookWindow).tmShowcaseWhenStable;
    if (hook === undefined) {
      // A missing hook must fail loudly: skipping the wait would quietly
      // reintroduce the race it exists to close.
      throw new Error(
        'The showcase stability hook (window.tmShowcaseWhenStable) is not installed.',
      );
    }
    // Bounded: the hook waits on every pending task, so a task that never
    // ends would otherwise hang the caller until the test's own timeout.
    let timer: ReturnType<typeof setTimeout> | undefined;
    const timedOut = new Promise<never>((_, reject) => {
      timer = setTimeout(
        () =>
          reject(
            new Error(
              `The showcase app did not become stable within ${timeoutMs} ms ` +
                '(a pending task that never ends, or a render loop).',
            ),
          ),
        timeoutMs,
      );
    });
    try {
      await Promise.race([hook(), timedOut]);
    } finally {
      clearTimeout(timer);
    }
  }, APP_STABLE_TIMEOUT_MS);
}

/**
 * Waits for every finite animation and transition on the element and its
 * descendants to finish, so that a geometry read measures the final layout
 * rather than a frame of the entrance motion (an enter animation that
 * slides a panel in reports a box that is still moving). Infinite
 * animations, such as a spinner's, are ignored: they never finish.
 */
export async function animationsSettled(locator: Locator): Promise<void> {
  await locator.evaluate(async (element) => {
    const finite = element
      .getAnimations({ subtree: true })
      .filter((animation) => animation.effect?.getComputedTiming().endTime !== Infinity);
    // A cancelled animation (its element removed) rejects `finished`; it is
    // over either way.
    await Promise.all(finite.map((animation) => animation.finished.catch(() => undefined)));
  });
}

/**
 * Waits until the web fonts the page has asked for have loaded, so a
 * geometry read measures text in its final face rather than in the fallback
 * that `font-display: swap` shows first (a heading's width, a wrap point).
 * Call it once the text to measure is rendered: a face that no rendered
 * text has requested yet is not waited on.
 */
export async function fontsLoaded(page: Page): Promise<void> {
  await page.evaluate(async () => {
    await document.fonts.ready;
  });
}
