// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { ADDITIONS_STORIES, type AdditionsProbe } from '../support/additions-stories';
import { armPaintProbe, paintProbeResult, type PaintProbeResult } from '../support/paint-probe';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * Instant feedback, per story: a key's feedback reaches the screen in the
 * first frame the browser paints after the key. The pull-request tier
 * checks the order of events inside that frame, which no runner's speed can
 * change: the feedback is in the DOM when the frame's layout is done.
 * The performance tier adds the clock: the frame is laid out within 50 ms
 * of the key event the control acts on, times a CI multiplier.
 */

/** The longest a key may take to reach its painted frame, on a quiet machine. */
const KEY_TO_PAINT_MS = 50;

/**
 * The timing budget's multiplier: `TM_PERF_MULTIPLIER` when set, else 2 on
 * CI, whose shared runners are slower and noisier than a quiet machine.
 */
const PERF_MULTIPLIER = Number(process.env['TM_PERF_MULTIPLIER'] ?? (process.env['CI'] ? 2 : 1));

/** The budget the performance tier enforces. */
const KEY_TO_PAINT_BUDGET_MS = KEY_TO_PAINT_MS * PERF_MULTIPLIER;

/** Loads a story, focuses the probe's control and presses its key under the probe. */
async function probeKey(
  page: Page,
  storyId: string,
  probe: AdditionsProbe,
): Promise<PaintProbeResult> {
  await probe.before?.(page);
  await page.goto(storyUrl(storyId));
  const story = ADDITIONS_STORIES.find((candidate) => candidate.id === storyId)!;
  await story.ready(page);
  await probe.focus(page);
  // A settled page: no render left over from the focus lands in the probe's frame.
  await appStable(page);
  await armPaintProbe(page, probe.painted, probe.trigger);
  await page.keyboard.press(probe.key);
  return paintProbeResult(page);
}

test('every story either has a key to probe or says why it has none', () => {
  for (const story of ADDITIONS_STORIES) {
    if ('none' in story.probes) {
      expect(story.probes.none, story.id).not.toBe('');
    } else {
      expect(story.probes.length, story.id).toBeGreaterThan(0);
    }
  }
});

for (const story of ADDITIONS_STORIES) {
  const probes = story.probes;
  if ('none' in probes) {
    continue;
  }
  test.describe(story.id, () => {
    for (const probe of probes) {
      test(`${probe.name}: the feedback is in the first frame after the key`, async ({ page }) => {
        const result = await probeKey(page, story.id, probe);
        expect(result.shownInFrame, 'the feedback was in the first frame after the key').toBe(true);
      });

      test(`${probe.name}: the first frame is laid out soon after the key @perf`, async ({
        page,
      }) => {
        const result = await probeKey(page, story.id, probe);
        expect(result.shownInFrame).toBe(true);
        // From the event the control acts on: for a keyup trigger, the time
        // between Playwright's keydown and keyup is the driver's, not the
        // component's.
        expect(result.prePaintAt - result.triggerAt).toBeLessThanOrEqual(KEY_TO_PAINT_BUDGET_MS);
      });
    }
  });
}
