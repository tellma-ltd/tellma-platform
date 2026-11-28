// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, type Page } from '@playwright/test';

/**
 * The keydown-to-paint probe: does the feedback of a key reach the screen
 * in the first frame the browser paints after the key?
 *
 * A component meets the instant-feedback rule when it updates its model in
 * the key's own handler, so the render that the handler schedules runs
 * before the next paint. The probe proves it without timing anything. It
 * runs a requestAnimationFrame loop from before the key, so its callback is
 * the first of each frame's callbacks; on the first frame after the key it
 * resizes an off-screen sentinel, whose ResizeObserver then reports in
 * that same frame, after every animation-frame callback and after layout
 * but before paint. At that moment the probe checks the DOM for the
 * feedback: what the DOM holds then is what the frame paints.
 *
 * Because the verdict rests on the order of events within the browser's
 * own frame, not on durations, a slow or loaded runner cannot fail it: a
 * slower machine paints later, but in the same order.
 *
 * Most controls act on `keydown`. A native control activated by Space (a
 * checkbox, a button) acts on `keyup`; its probe checks the first frame
 * after the keyup.
 *
 * A chord's modifiers are not its key: Playwright presses Alt, then
 * ArrowDown, as separate events, and a frame may fall between them. The
 * probe skips the modifiers' own events and starts at the key itself.
 */

/**
 * The feedback a key must paint, as a check on the DOM: the elements a CSS
 * selector matches, and optionally what the first of them shows.
 */
export interface PaintedFeedback {
  /** The CSS selector of the feedback (`:focus` and `:checked` work). */
  readonly selector: string;
  /** How many elements must match; by default at least one. */
  readonly count?: number;
  /** Text the first match must contain. */
  readonly contains?: string;
  /** The first match's whole text, whitespace collapsed. */
  readonly text?: string;
  /** A class the first match must carry. */
  readonly className?: string;
  /** The value the first match (a text input) must hold. */
  readonly value?: string;
}

/** What the page records for one probe, as `performance.now()` times. */
export interface PaintProbeResult {
  /** When the key went down (the key itself, not a modifier held with it). */
  readonly keydownAt: number;
  /** When the key event the control acts on was dispatched. */
  readonly triggerAt: number;
  /** When the first frame after the trigger began: its first animation-frame callback. */
  readonly frameAt: number;
  /** When that frame's layout was done, just before it painted. */
  readonly prePaintAt: number;
  /** Whether the feedback was in the DOM at that moment, so in the painted frame. */
  readonly shownInFrame: boolean;
}

/** The page-side record the probe fills in. */
interface ProbeWindow {
  /** The probe's measurements, as they are taken. */
  tmPaintProbe?: {
    keydownAt: number | null;
    triggerAt: number | null;
    frameAt: number | null;
    prePaintAt: number | null;
    shownInFrame: boolean | null;
  };
  /** Whether the DOM shows the feedback now. */
  tmPaintProbeShows?: () => boolean;
}

/**
 * Arms the probe: the next keydown starts it, the next `trigger` event
 * marks the frame that must show the feedback. Call it once the story is
 * ready and the control is focused, then press the key with Playwright's
 * keyboard, then read the verdict with {@link paintProbeResult}. Fails if
 * the feedback already shows, since the probe could then prove nothing.
 */
export async function armPaintProbe(
  page: Page,
  feedback: PaintedFeedback,
  trigger: 'keydown' | 'keyup' = 'keydown',
): Promise<void> {
  const shownBefore = await page.evaluate(
    ({ feedback, trigger }) => {
      // The check on the DOM, run at the frame's pre-paint moment and again
      // by the test once the page has settled.
      const shows = (): boolean => {
        const matches = document.querySelectorAll(feedback.selector);
        if (
          feedback.count !== undefined ? matches.length !== feedback.count : matches.length === 0
        ) {
          return false;
        }
        const first = matches[0] as HTMLElement | undefined;
        const text = (first?.textContent ?? '').replace(/\s+/g, ' ').trim();
        return (
          (feedback.contains === undefined || text.includes(feedback.contains)) &&
          (feedback.text === undefined || text === feedback.text) &&
          (feedback.className === undefined || !!first?.classList.contains(feedback.className)) &&
          (feedback.value === undefined ||
            (first as HTMLInputElement | undefined)?.value === feedback.value)
        );
      };
      (window as ProbeWindow).tmPaintProbeShows = shows;
      const probe: NonNullable<ProbeWindow['tmPaintProbe']> = {
        keydownAt: null,
        triggerAt: null,
        frameAt: null,
        prePaintAt: null,
        shownInFrame: null,
      };
      (window as ProbeWindow).tmPaintProbe = probe;

      // The sentinel sits outside <body>, off screen, so it changes nothing
      // the feedback check or the story can see.
      const sentinel = document.createElement('div');
      sentinel.setAttribute('aria-hidden', 'true');
      sentinel.style.cssText =
        'position:fixed;inset-block-start:-10px;inset-inline-start:-10px;' +
        'inline-size:1px;block-size:1px;pointer-events:none;';
      document.documentElement.append(sentinel);

      // Reports after layout, before paint, in the frame that resized it.
      // Its first report (on observing) comes before any key and is ignored.
      new ResizeObserver(() => {
        if (probe.frameAt !== null && probe.prePaintAt === null) {
          probe.prePaintAt = performance.now();
          probe.shownInFrame = shows();
          sentinel.remove();
        }
      }).observe(sentinel);

      // The capture phase on window runs before any handler of the page.
      // A modifier's own events are skipped: the frame after Alt goes down
      // can come before the ArrowDown it is held for.
      const modifiers = new Set(['Alt', 'AltGraph', 'Control', 'Meta', 'Shift']);
      const stamp = (event: KeyboardEvent): void => {
        if (modifiers.has(event.key)) {
          return;
        }
        if (event.type === 'keydown' && probe.keydownAt === null) {
          probe.keydownAt = performance.now();
        }
        if (event.type === trigger && probe.keydownAt !== null && probe.triggerAt === null) {
          probe.triggerAt = performance.now();
        }
      };
      window.addEventListener('keydown', stamp, { capture: true });
      window.addEventListener('keyup', stamp, { capture: true });

      // Registered a frame ahead, this callback runs first in every frame,
      // before the callbacks a key's handler schedules.
      const loop = (): void => {
        if (probe.triggerAt !== null && probe.frameAt === null) {
          probe.frameAt = performance.now();
          sentinel.style.inlineSize = '2px';
        }
        if (probe.prePaintAt === null) {
          requestAnimationFrame(loop);
        }
      };
      requestAnimationFrame(loop);
      return shows();
    },
    { feedback, trigger },
  );
  expect(shownBefore, 'the feedback is not showing before the key').toBe(false);
  // Two frames, so the loop is running and the sentinel's first report is
  // spent before the key arrives.
  await page.evaluate(
    () =>
      new Promise<void>((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(() => resolve())),
      ),
  );
}

/**
 * Waits until the probe's frame has been laid out and the feedback shows,
 * and returns what the probe recorded. Fails if the feedback never shows:
 * then the key did not work at all, which is a different failure from a
 * key whose feedback came a frame late.
 */
export async function paintProbeResult(page: Page): Promise<PaintProbeResult> {
  await expect
    .poll(() => page.evaluate(() => (window as ProbeWindow).tmPaintProbe?.prePaintAt ?? null), {
      message: 'the first frame after the key was laid out (the key arrived)',
    })
    .not.toBeNull();
  await expect
    .poll(() => page.evaluate(() => (window as ProbeWindow).tmPaintProbeShows!()), {
      message: 'the key’s feedback shows',
    })
    .toBe(true);
  return page.evaluate(() => {
    const probe = (window as ProbeWindow).tmPaintProbe!;
    return {
      keydownAt: probe.keydownAt!,
      triggerAt: probe.triggerAt!,
      frameAt: probe.frameAt!,
      prePaintAt: probe.prePaintAt!,
      shownInFrame: probe.shownInFrame!,
    };
  });
}
