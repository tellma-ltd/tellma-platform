// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { beforeAll, describe, expect, it } from 'vitest';

/** The client folder, two levels above this spec. */
const clientDir = resolve(import.meta.dirname, '..', '..');
/** The UI library's source, one folder per entry point. */
const coreUiDir = join(clientDir, 'projects', 'core', 'tellma-core-ui');
/** Where the docs build writes components.json and llms.txt. */
const generatedDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-mcp', 'generated');
/** The testing library's source, where the harnesses live. */
const testingDir = join(clientDir, 'projects', 'core', 'tellma-core-ui-testing');

/** The fields of a components.json entry this spec reads. */
interface DocEntry {
  /** The selector of a component or directive; the class name of a service. */
  readonly name: string;
  /** The selector, or `null` for a service. */
  readonly selector: string | null;
  /** The `@tellma/core-ui/<id>` import path. */
  readonly entryPoint: string;
  /** The harness class, or `null`. */
  readonly harness: string | null;
  /** The examples, by title. */
  readonly examples: readonly { readonly title: string; readonly language: string }[];
  /** The outputs, by name. */
  readonly outputs: readonly { readonly name: string; readonly type: string }[];
  /** The public members beyond inputs and outputs, by name. */
  readonly members?: readonly { readonly name: string }[];
}

/**
 * The library additions' entry points, each with the harnesses
 * @tellma/core-ui-testing ships for it.
 */
const ADDITIONS: Readonly<Record<string, readonly string[]>> = {
  icon: [],
  kbd: [],
  copy: ['TmCopyButtonHarness', 'TmReferenceHarness'],
  badge: [],
  counter: ['TmCounterHarness'],
  avatar: ['TmAvatarHarness'],
  chip: ['TmChipHarness', 'TmChipSetHarness'],
  progress: ['TmProgressBarHarness'],
  skeleton: [],
  'relative-time': ['TmRelativeTimeHarness'],
  toast: ['TmToastHarness'],
  banner: ['TmBannerBarHarness'],
  'empty-state': ['TmEmptyStateHarness'],
  'busy-overlay': ['TmBusyOverlayHarness'],
  confirm: ['TmConfirmHarness'],
  'error-dialog': ['TmErrorDialogHarness'],
  radio: ['TmRadioGroupHarness', 'TmRadioHarness'],
  segmented: ['TmSegmentedHarness'],
  switch: ['TmSwitchHarness'],
  'search-field': ['TmSearchFieldHarness'],
  'date-range': ['TmDateRangeHarness'],
  'date-time': ['TmTimeFieldHarness', 'TmDateTimeFieldHarness'],
  card: ['TmCardHarness'],
  'page-header': [],
  'description-list': [],
  splitter: ['TmSplitterHarness'],
  pager: ['TmPagerHarness'],
  tree: ['TmTreeHarness', 'TmTreeNodeHarness'],
  'drag-drop': [],
  'reorder-list': ['TmReorderListHarness'],
  tiles: ['TmTilesHarness', 'TmTileHarness'],
  'tiles-tree': [],
};

/**
 * The additions harnesses that drive no documented entry of their own, each
 * with the harness that reaches them: a tile is markup the tiles component
 * renders from its template, not a component.
 */
const REACHED_THROUGH: Readonly<Record<string, string>> = {
  TmTileHarness: 'TmTilesHarness',
};

/**
 * The additions entry points shown in another entry point's showcase story:
 * the counter, split out of the badges so a first frame can draw a count
 * without tm-badge, keeps its place in the badges and counters story.
 */
const STORY_OF: Readonly<Record<string, string>> = {
  counter: 'badge',
};

/**
 * The generated library docs, checked from the consumer's side: every entry
 * point of the additions is in components.json and llms.txt, names its
 * harnesses, and has a showcase story; and no examples file anywhere in the
 * library is left out of the docs.
 */
describe('the library docs', () => {
  /** The components.json entries. */
  let entries: readonly DocEntry[];
  /** The llms.txt text. */
  let llms: string;
  /** Every class the testing library's harness files export. */
  let harnessExports: Set<string>;

  beforeAll(() => {
    // Build the docs if this spec runs standalone.
    if (!existsSync(join(generatedDir, 'components.json'))) {
      execFileSync(
        process.execPath,
        ['node_modules/tsx/dist/cli.mjs', 'tools/docs/build-docs.mts'],
        {
          cwd: clientDir,
        },
      );
    }
    entries = (
      JSON.parse(readFileSync(join(generatedDir, 'components.json'), 'utf8')) as {
        components: DocEntry[];
      }
    ).components;
    llms = readFileSync(join(generatedDir, 'llms.txt'), 'utf8');
    // Every class the testing library's harness files export.
    harnessExports = new Set(
      readdirSync(testingDir)
        .filter((name) => name.endsWith('-harness.ts'))
        .flatMap((name) =>
          [...readFileSync(join(testingDir, name), 'utf8').matchAll(/export class (\w+)/g)].map(
            (match) => match[1],
          ),
        ),
    );
  }, 120_000);

  it('documents every additions entry point in components.json and llms.txt', () => {
    for (const id of Object.keys(ADDITIONS)) {
      const documented = entries.filter((entry) => entry.entryPoint === `@tellma/core-ui/${id}`);
      expect(documented.length, `components.json has ${id}`).toBeGreaterThan(0);
      for (const entry of documented) {
        const heading = `## ${entry.name} (`;
        expect(llms, `llms.txt has a section for ${entry.name}`).toContain(heading);
      }
    }
  });

  it('names each additions harness, on the entry it drives', () => {
    for (const [id, harnesses] of Object.entries(ADDITIONS)) {
      const documented = entries.filter((entry) => entry.entryPoint === `@tellma/core-ui/${id}`);
      for (const harness of harnesses) {
        expect(harnessExports.has(harness), `@tellma/core-ui-testing exports ${harness}`).toBe(
          true,
        );
        // The harness is named on the entry it drives, or reached through
        // a harness that is.
        const named = REACHED_THROUGH[harness] ?? harness;
        expect(
          documented.some((entry) => entry.harness === named),
          `an entry of ${id} names ${named}`,
        ).toBe(true);
      }
      // And no entry names a harness the testing library does not ship.
      for (const entry of documented) {
        if (entry.harness !== null) {
          expect(harnessExports.has(entry.harness), `${entry.name}'s harness exists`).toBe(true);
        }
      }
    }
  });

  it('has a showcase story for every additions entry point', () => {
    const stories = readFileSync(
      join(clientDir, 'projects', 'internal', 'showcase', 'src', 'app', 'stories.ts'),
      'utf8',
    );
    for (const id of Object.keys(ADDITIONS)) {
      const story = STORY_OF[id] ?? id;
      expect(stories, `the showcase has the ${story} story`).toContain(`id: '${story}'`);
    }
  });

  it('files an output bridged from an observable as an output, not a member', () => {
    const chip = entries.find((entry) => entry.name === 'tm-chip');
    expect(chip?.outputs.map((output) => output.name)).toContain('activate');
    expect(chip?.members?.map((member) => member.name) ?? []).not.toContain('activate');
    // No output keeps the bridge's type in place of what it emits.
    const bridged = entries.flatMap((entry) =>
      entry.outputs.filter((output) => output.type.startsWith('OutputRef<')),
    );
    expect(bridged).toEqual([]);
  });

  it('leaves the members tagged as internal wiring out of the docs', () => {
    const members = (selector: string): readonly string[] =>
      entries.find((entry) => entry.name === selector)?.members?.map((member) => member.name) ?? [];
    expect(members('tm-option')).not.toContain('derivedText');
    expect(members('tm-option')).not.toContain('effectiveLabel');
    expect(members('tm-option-group')).not.toContain('headingId');
    expect(members('tm-grid-column')).not.toContain('generatedId');
    expect(members('button[tmFilePicker]')).not.toContain('awaitingDialog');
  });

  it('carries every exported example of every examples file into the docs', () => {
    const entryPoints = readdirSync(coreUiDir, { withFileTypes: true }).filter((entry) =>
      entry.isDirectory(),
    );
    let files = 0;
    for (const entryPoint of entryPoints) {
      const dir = join(coreUiDir, entryPoint.name);
      for (const file of readdirSync(dir).filter((name) => name.endsWith('.examples.ts'))) {
        files += 1;
        const titles = [
          ...readFileSync(join(dir, file), 'utf8').matchAll(/^export const (\w+)/gm),
        ].map((match) => match[1]);
        const documented = new Set(
          entries
            .filter((entry) => entry.entryPoint === `@tellma/core-ui/${entryPoint.name}`)
            .flatMap((entry) => entry.examples.map((example) => example.title)),
        );
        for (const title of titles) {
          expect(documented.has(title), `${entryPoint.name}/${file} ${title} is in the docs`).toBe(
            true,
          );
        }
      }
    }
    expect(files).toBeGreaterThan(0);
  });
});
