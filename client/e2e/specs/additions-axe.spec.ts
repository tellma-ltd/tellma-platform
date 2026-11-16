// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { expect, test } from '@playwright/test';

import { ADDITIONS_ENTRY_POINTS, ADDITIONS_STORIES } from '../support/additions-stories';
import { expectNoAxeViolations } from '../support/axe';
import { storyUrl } from '../support/story-map';

/**
 * The accessibility battery over every story of the library additions: each
 * story in the states its own spec does not scan — toasts and dialogs open,
 * trees collapsed, expanded, loading and failed, tooltips and popups shown,
 * fields in error — in light and dark, left-to-right and right-to-left. The
 * pull-request tier scans each state in one theme and direction, rotating
 * through the four so that every combination is covered across the states;
 * the nightly tier scans every state in all four.
 *
 * It also checks the battery's own registry against the library and the
 * showcase, so an entry point added without a story, or a story without an
 * entry in the registry, fails here instead of going unscanned.
 */

/** The four theme and direction combinations, in the order the rotation takes them. */
const COMBINATIONS = [
  { theme: 'light', dir: 'ltr' },
  { theme: 'dark', dir: 'rtl' },
  { theme: 'dark', dir: 'ltr' },
  { theme: 'light', dir: 'rtl' },
] as const;

/** The client folder, two levels above this spec. */
const CLIENT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

test.describe('registry', () => {
  test('every additions entry point is a library entry point with a story in the battery', () => {
    const library = new Set(
      readdirSync(join(CLIENT, 'projects', 'core', 'tellma-core-ui'), { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => entry.name),
    );
    const stories = new Set(ADDITIONS_STORIES.map((story) => story.id));
    for (const id of ADDITIONS_ENTRY_POINTS) {
      expect(library.has(id), `the library has the entry point ${id}`).toBe(true);
      expect(stories.has(id), `the battery has a story for ${id}`).toBe(true);
    }
    // Every story covers known entry points, and every entry point is covered.
    const covered = new Set(ADDITIONS_STORIES.flatMap((story) => story.entryPoints));
    expect([...covered].sort()).toEqual([...ADDITIONS_ENTRY_POINTS].sort());
  });

  test('every story in the battery is a showcase story', () => {
    const source = readFileSync(
      join(CLIENT, 'projects', 'internal', 'showcase', 'src', 'app', 'stories.ts'),
      'utf8',
    );
    const showcase = new Set([...source.matchAll(/\bid: '([^']+)'/g)].map((match) => match[1]));
    for (const story of ADDITIONS_STORIES) {
      expect(showcase.has(story.id), `the showcase lists the story ${story.id}`).toBe(true);
    }
  });
});

// Section: one test per story state and combination; the rotation picks the
// pull-request combination, and the rest are nightly.
let rotation = 0;
for (const story of ADDITIONS_STORIES) {
  test.describe(story.id, () => {
    for (const state of story.states) {
      const pick = rotation;
      rotation += 1;
      COMBINATIONS.forEach(({ theme, dir }, index) => {
        const nightly = index !== pick % COMBINATIONS.length;
        test(`is axe-clean with ${state.name} (${theme}, ${dir})${nightly ? ' @nightly' : ''}`, async ({
          page,
        }) => {
          await state.before?.(page);
          await page.goto(storyUrl(story.id, { theme, dir }));
          await story.ready(page);
          await state.enter(page);
          await expectNoAxeViolations(page);
        });
      });
    }
  });
}
