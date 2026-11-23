// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { describe, expect, it } from 'vitest';

import { capFailure, CAP_GZIP_LEVEL, ENTRY_POINT_CAPS, readEntryPointCaps } from '../bundle-caps.mjs';

// The size gate's caps on whole entry points: the platform's level, a raise
// only with a note, and the verdict on a measured weight at gzip level 9.

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
