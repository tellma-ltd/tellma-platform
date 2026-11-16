// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { expect, test, type Page } from '@playwright/test';

import { ADDITIONS_STORIES, type AdditionsStory } from '../support/additions-stories';
import { ALLOWED_MOTION, recordMotion, recordedMotion, type MotionRecord } from '../support/motion';
import { appStable } from '../support/settle';
import { storyUrl } from '../support/story-map';

/**
 * The reduced-motion battery over every story of the library additions.
 * Each story is loaded, put through every state the accessibility battery
 * opens and the key its paint probe presses, while a recorder notes every
 * animation and transition the page starts. Under
 * `prefers-reduced-motion: reduce` nothing may move; without the preference
 * only the motion the library allows may run. The pins at the end prove
 * that each allowed motion does run without the preference, so the
 * recorder is watching, and stops under `reduce`.
 */

/** A motion preference as Playwright emulates it. */
type Preference = 'no-preference' | 'reduce';

/** Puts a story through its states and its probe keys, each from a fresh load, and returns what moved. */
async function exercise(
  page: Page,
  story: AdditionsStory,
  preference: Preference,
): Promise<MotionRecord[]> {
  await page.emulateMedia({ reducedMotion: preference });
  await recordMotion(page);
  const moved: MotionRecord[] = [];
  /** Loads the story fresh, runs one step, and keeps what moved. */
  const run = async (step: (page: Page) => Promise<void>): Promise<void> => {
    await page.goto(storyUrl(story.id));
    await story.ready(page);
    await step(page);
    await appStable(page);
    moved.push(...(await recordedMotion(page)).filter((record) => record.motion));
  };
  await run(() => Promise.resolve());
  for (const state of story.states) {
    await state.before?.(page);
    await run(state.enter);
  }
  if (!('none' in story.probes)) {
    for (const probe of story.probes) {
      await probe.before?.(page);
      await run(async () => {
        await probe.focus(page);
        await page.keyboard.press(probe.key);
      });
    }
  }
  return moved;
}

for (const story of ADDITIONS_STORIES) {
  test.describe(story.id, () => {
    test('nothing moves under reduced motion', async ({ page }) => {
      expect(await exercise(page, story, 'reduce')).toEqual([]);
    });

    test('only the allowed motion runs without the preference', async ({ page }) => {
      const moved = await exercise(page, story, 'no-preference');
      const unexpected = moved.filter(
        (record) => !ALLOWED_MOTION.some((allowed) => allowed.matches(record)),
      );
      expect(unexpected).toEqual([]);
    });
  });
}

// Section: each allowed motion runs without the preference and stops under
// reduce. The record names the motion; the step triggers it.
/** The allowed motions, each with the story and step that start it. */
const ALLOWED_PINS: readonly {
  /** The motion, in words, as the test names it. */
  readonly motion: string;
  /** The story that shows the motion. */
  readonly story: string;
  /** Starts the motion once the story is ready. */
  readonly trigger: (page: Page) => Promise<void>;
  /** Whether a recorded animation or transition is this motion. */
  readonly matches: (record: MotionRecord) => boolean;
}[] = [
  {
    motion: 'the switch knob’s travel',
    story: 'switch',
    trigger: (page) => page.getByRole('switch', { name: 'Notify me by email' }).click(),
    matches: (record) => record.kind === 'transition' && record.target.includes('tm-switch__knob'),
  },
  {
    motion: 'the progress bar’s sweep',
    story: 'progress',
    trigger: () => Promise.resolve(),
    matches: (record) => record.name.startsWith('tm-progress-sweep'),
  },
  {
    motion: 'the progress ring’s spin',
    story: 'progress',
    trigger: () => Promise.resolve(),
    matches: (record) => record.name === 'tm-progress-ring-spin',
  },
  {
    motion: 'the confirm dialog’s fade',
    story: 'confirm',
    trigger: async (page) => {
      await page.getByTestId('open-leave').click();
      await expect(page.getByRole('alertdialog')).toBeVisible();
    },
    matches: (record) => record.name === 'tm-modal-enter',
  },
  {
    motion: 'the busy overlay’s spin',
    story: 'busy-overlay',
    trigger: async (page) => {
      await page.getByTestId('import').click();
      await expect(page.locator('.tm-busy-overlay')).toHaveClass(/tm-busy-overlay--scrim/);
    },
    matches: (record) => record.name === 'tm-spinner-spin',
  },
];

for (const pin of ALLOWED_PINS) {
  for (const preference of ['no-preference', 'reduce'] as const) {
    const runs = preference === 'no-preference';
    test(`${pin.motion} ${runs ? 'runs without the preference' : 'is static under reduced motion'}`, async ({
      page,
    }) => {
      await page.emulateMedia({ reducedMotion: preference });
      await recordMotion(page);
      await page.goto(storyUrl(pin.story));
      const story = ADDITIONS_STORIES.find((candidate) => candidate.id === pin.story)!;
      await story.ready(page);
      await pin.trigger(page);
      await appStable(page);
      // A started animation reports at its first frame; poll for it.
      if (runs) {
        await expect.poll(async () => (await recordedMotion(page)).some(pin.matches)).toBe(true);
      } else {
        expect((await recordedMotion(page)).filter(pin.matches)).toEqual([]);
      }
    });
  }
}
