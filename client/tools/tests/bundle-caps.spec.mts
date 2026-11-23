// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { describe, expect, it } from 'vitest';

import {
  capFailure,
  CAP_FLOOR_KB,
  CAP_GZIP_LEVEL,
  ceilingProblem,
  ENTRY_POINT_CAPS,
  readEntryPointCaps,
  unmeasuredCaps,
} from '../bundle-caps.mjs';

// The size gate's caps on whole entry points: the platform's level, a raise
// only with a note, the verdict on a measured weight at gzip level 9, every
// cap measured, and the ceilings' shape.

/** A library as the workspace discovery hands it to the gate. */
function library(capsInKb?: unknown, name = '@tellma/core'): Parameters<typeof readEntryPointCaps>[0] {
  return {
    name,
    tellma: capsInKb === undefined ? { budgetsInKb: { '.': 45 } } : { budgetsInKb: { '.': 45 }, capsInKb },
    entryPoints: [{ id: '.' }, { id: './shell' }],
  };
}

describe('the caps on whole entry points', () => {
  it("cap the @tellma/core primary at 50 KB, measured at gzip level 9", () => {
    expect(ENTRY_POINT_CAPS['@tellma/core']).toEqual({ '.': 50 });
    expect(CAP_GZIP_LEVEL).toBe(9);
    expect(readEntryPointCaps(library())).toEqual({ caps: [{ id: '.', kb: 50, platformKb: 50 }], problems: [] });
    // A library the platform caps nothing of has no caps.
    expect(readEntryPointCaps(library(undefined, '@tellma/core-ui'))).toEqual({ caps: [], problems: [] });
  });

  it('fail a weight over the cap, and pass one at or under it', () => {
    const [cap] = readEntryPointCaps(library()).caps;
    expect(capFailure('@tellma/core', cap!, 50 * 1024 + 1)).toBe('@tellma/core: 50.00 KB at gzip level 9 is over its cap of 50 KB.');
    expect(capFailure('@tellma/core', cap!, 50 * 1024)).toBeNull();
    expect(capFailure('@tellma/core', cap!, 44.9 * 1024)).toBeNull();
  });

  it('fail a capped entry point that weighs under the floor: an empty or truncated build', () => {
    const [cap] = readEntryPointCaps(library()).caps;
    expect(CAP_FLOOR_KB).toBe(1);
    // A truncated FESM still bundles to a few bytes of glue.
    expect(capFailure('@tellma/core', cap!, 20)).toBe(
      '@tellma/core: 0.02 KB at gzip level 9 is under the 1 KB a capped entry point weighs at least; its build is empty or truncated.',
    );
    expect(capFailure('@tellma/core', cap!, 0)).toMatch(/is under the 1 KB/);
    expect(capFailure('@tellma/core', cap!, 1024)).toBeNull();
  });

  it('fail every cap the gate never measured: a renamed package, or a capped entry point it skipped', () => {
    expect(unmeasuredCaps([{ name: '@tellma/core', id: '.' }])).toEqual([]);
    // The core package renamed: nothing of '@tellma/core' reaches the gate.
    expect(unmeasuredCaps([{ name: '@tellma/core-renamed', id: '.' }])).toEqual([
      '@tellma/core: the platform caps this package, but the gate measured none of its caps; no library has that name, or none of them was measured.',
    ]);
    expect(unmeasuredCaps([])).toHaveLength(1);
    // The package measured, but another entry point than the capped one.
    expect(unmeasuredCaps([{ name: '@tellma/core', id: './shell' }])).toEqual([
      "@tellma/core: the platform caps '.', but the gate never measured it.",
    ]);
    // A renamed library has no caps to read, so only the after-loop check catches it.
    expect(readEntryPointCaps(library(undefined, '@tellma/core-renamed')).caps).toEqual([]);
  });

  it('take a raise with its note, and refuse one without', () => {
    const noted = readEntryPointCaps(library({ '.': { error: 52, note: 'The launcher grew the shell share.' } }));
    expect(noted).toEqual({ caps: [{ id: '.', kb: 52, platformKb: 50 }], problems: [] });
    expect(capFailure('@tellma/core', noted.caps[0]!, 51 * 1024)).toBeNull();

    for (const note of [undefined, '', '   ']) {
      const bare = readEntryPointCaps(library({ '.': { error: 52, ...(note === undefined ? {} : { note }) } }));
      expect(bare.problems, String(note)).toEqual(['@tellma/core: the cap on \'.\' is raised from 50 to 52 KB without a "note" saying why.']);
    }
  });

  it('take a lower cap without a note', () => {
    expect(readEntryPointCaps(library({ '.': { error: 48 } }))).toEqual({ caps: [{ id: '.', kb: 48, platformKb: 50 }], problems: [] });
  });

  it('refuse a malformed setting, or a cap the platform does not set', () => {
    expect(readEntryPointCaps(library([])).problems).toEqual(['@tellma/core: "tellma".capsInKb must be an object of caps by entry point.']);
    expect(readEntryPointCaps(library({ '.': { error: '52', note: 'x' } })).problems).toEqual([
      '@tellma/core: "tellma".capsInKb[\'.\'].error must be a positive number of KB.',
    ]);
    expect(readEntryPointCaps(library({ './shell': { error: 2, note: 'x' } })).problems).toEqual([
      "@tellma/core: \"tellma\".capsInKb names './shell', which has no cap to set.",
    ]);
  });

  it('refuse a platform cap on an entry point the library does not have', () => {
    const missing = { name: '@tellma/core', tellma: {}, entryPoints: [{ id: './shell' }] };
    expect(readEntryPointCaps(missing)).toEqual({ caps: [], problems: ["@tellma/core: the platform caps '.', which is not an entry point."] });
  });
});

describe('the ceilings a library declares', () => {
  it('take a positive number of KB', () => {
    expect(ceilingProblem('@tellma/core', "budgetsInKb['.']", 45)).toBeNull();
    expect(ceilingProblem('@tellma/core', "budgetsInKb['.']", 0.5)).toBeNull();
  });

  it('refuse a ceiling that is not a positive number, which would pass every weight', () => {
    expect(ceilingProblem('@tellma/core', "budgetsInKb['.']", 'abc')).toBe(
      `@tellma/core: "tellma".budgetsInKb['.'] must be a positive number of KB, not "abc".`,
    );
    for (const kb of ['45', 0, -1, Number.NaN, Number.POSITIVE_INFINITY, null, {}]) {
      expect(ceilingProblem('@tellma/core', "budgetsInKb['.']", kb), String(kb)).toMatch(/must be a positive number of KB/);
    }
    expect(ceilingProblem('@tellma/core', "lazyChunkBudgetsInKb['./x']['echarts']", 'abc')).toMatch(
      /^@tellma\/core: "tellma"\.lazyChunkBudgetsInKb\['\.\/x'\]\['echarts'\] must be/,
    );
  });
});
