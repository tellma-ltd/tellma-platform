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
 * showcase, both ways: a library folder that nobody has classified, an
 * additions entry point without a story, or an additions story missing from
 * the registry fails here instead of going unscanned.
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

/**
 * The library's folders that are not additions: the entry points that came
 * before them or stand outside them (the pivot and the chart, whose own
 * suites, pivot.spec.ts and chart.spec.ts, run their axe scans, the
 * multi-select, which the select story shows and select.spec.ts scans, and
 * the progress ring, split out of the progress entry point so that a bar
 * alone never loads it, which the progress story shows beside the bar and
 * this battery scans with it), the shared folders with no entry point of
 * their own, the image's blob-fetching
 * seam, split out of the image's entry point with no UI to scan, the avatar's
 * initials and tone helpers, split out of the avatar's entry point with no UI
 * of their own (the avatar's story scans the faces they draw), l10n's input
 * side, its parsers, split out of l10n with no UI either, and the keys
 * grammar, split out of kbd's entry point with no UI of its own (the kbd
 * story draws and matches the combinations it defines), and the search
 * input's directive, split out of the search field's entry point so that a
 * page or a launcher with chrome of its own takes it without the field, with
 * no UI of its own (the search field's story scans the input it drives). A
 * new folder must join this list or the additions, so that none is skipped
 * unclassified.
 */
const OLDER_FOLDERS: readonly string[] = [
  'alert',
  'blob-fetcher',
  'button',
  'cache',
  'calendar-ethiopic',
  'calendar-umalqura',
  'chart',
  'checkbox',
  'contracts',
  'date-picker',
  'entity-picker',
  'file-preview',
  'files',
  'fonts',
  'form-field',
  'forms',
  'grid',
  'grid-editing',
  'grid-engine',
  'i18n',
  'image',
  'initials',
  'input',
  'keys',
  'l10n',
  'l10n-facade',
  'l10n-input',
  'layout',
  'menu',
  'modal',
  'multi-select',
  'number',
  'overlay',
  'pivot',
  'popover',
  'private',
  'progress-ring',
  'providers',
  'search-input',
  'select',
  'spinner',
  'styles',
  'tabs',
  'theme',
  'tooltip',
  'tree-grid',
];

/** The library's folder names. */
function libraryFolders(): string[] {
  return readdirSync(join(CLIENT, 'projects', 'core', 'tellma-core-ui'), { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name);
}

/** The showcase's story ids, read from its story list. */
function showcaseStoryIds(): string[] {
  const source = readFileSync(
    join(CLIENT, 'projects', 'internal', 'showcase', 'src', 'app', 'stories.ts'),
    'utf8',
  );
  return [...source.matchAll(/\bid: '([^']+)'/g)].map((match) => match[1]!);
}

/**
 * The library folder a showcase story belongs to: the longest folder name
 * that is the story id or a dash-separated prefix of it (`tree-grid-large`
 * belongs to `tree-grid`, not `tree`), or none.
 */
function owningFolder(storyId: string, folders: readonly string[]): string | undefined {
  return folders
    .filter((folder) => storyId === folder || storyId.startsWith(`${folder}-`))
    .sort((a, b) => b.length - a.length)[0];
}

test.describe('registry', () => {
  test('every library folder is either an additions entry point or an older folder', () => {
    const additions = new Set(ADDITIONS_ENTRY_POINTS);
    const older = new Set(OLDER_FOLDERS);
    for (const folder of libraryFolders()) {
      expect(
        additions.has(folder) !== older.has(folder),
        `the library folder ${folder} is classified exactly once`,
      ).toBe(true);
    }
  });

  test('every additions entry point is a library entry point with a story in the battery', () => {
    const library = new Set(libraryFolders());
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
    const showcase = new Set(showcaseStoryIds());
    for (const story of ADDITIONS_STORIES) {
      expect(showcase.has(story.id), `the showcase lists the story ${story.id}`).toBe(true);
    }
  });

  test('every showcase story of an additions entry point is in the battery', () => {
    const folders = libraryFolders();
    const additions = new Set(ADDITIONS_ENTRY_POINTS);
    const registered = new Set(ADDITIONS_STORIES.map((story) => story.id));
    for (const id of showcaseStoryIds()) {
      const owner = owningFolder(id, folders);
      if (owner !== undefined && additions.has(owner)) {
        expect(registered.has(id), `the battery has the ${owner} story ${id}`).toBe(true);
      }
    }
  });
});

// Section: one test per story state and combination; the rotation picks the
// pull-request combination, and the rest are nightly.
/** The running count of states, whose remainder picks each state's pull-request combination. */
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
