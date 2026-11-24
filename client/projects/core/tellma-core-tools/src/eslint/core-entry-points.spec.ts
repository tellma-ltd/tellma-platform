// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { describe, expect, it } from 'vitest';

import { tmAllowedImports, tmCheckEntryPoints, TM_CORE_ENTRY_POINTS, type TmEntryPointRule } from './core-entry-points.js';

describe('the @tellma/core entry-point table', () => {
  it('is sound: no unknown target, no eager import of lazy code and no cycle', () => {
    expect(tmCheckEntryPoints(TM_CORE_ENTRY_POINTS)).toEqual([]);
  });

  it('keeps the eager graph the app framework fixes', () => {
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, '.')]).toEqual([]);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './attention')].sort()).toEqual([
      '.',
      './private',
      './search-normalize',
    ]);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './shell')].sort()).toEqual([
      '.',
      './attention',
      './private',
      './search-normalize',
    ]);
  });

  it('keeps the shared entry points off the primary and out of each other', () => {
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './private')]).toEqual(['.']);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './search-normalize')]).toEqual([]);
  });

  it('lets lazy entry points import every eager and shared one', () => {
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './search')].sort()).toEqual([
      '.',
      './attention',
      './column-filter',
      './entity',
      './memory',
      './private',
      './search-normalize',
      './shell',
    ]);
  });

  it("lets the page hosts import the tab's memory, and the home page's the recent pages", () => {
    for (const host of ['./search', './details', './admin', './reports', './report-studio']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, host).has('./memory')).toBe(true);
    }
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './dashboards').has('./recents')).toBe(true);
    // No eager entry point reaches either.
    for (const eager of ['.', './attention', './shell']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./memory')).toBe(false);
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./recents')).toBe(false);
    }
  });

  it('keeps the page-context snapshot off the first frame, for the support dump to load', () => {
    expect(TM_CORE_ENTRY_POINTS['./page-snapshot']).toEqual({ load: 'lazy', imports: [] });
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './support').has('./page-snapshot')).toBe(true);
    for (const eager of ['.', './attention', './shell']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./page-snapshot')).toBe(false);
    }
  });

  it('keeps the column filter control out of the entity entry point, for the search page to load', () => {
    expect(TM_CORE_ENTRY_POINTS['./column-filter']).toEqual({ load: 'lazy', imports: ['./entity'] });
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './search').has('./column-filter')).toBe(true);
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './entity').has('./column-filter')).toBe(false);
    for (const eager of ['.', './attention', './shell']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./column-filter')).toBe(false);
    }
  });

  it('names an import of an unknown entry point', () => {
    const table: Record<string, TmEntryPointRule> = {
      '.': { load: 'eager', imports: [] },
      './a': { load: 'lazy', imports: ['./missing'] },
    };
    expect(tmCheckEntryPoints(table)).toEqual(["'./a' imports './missing', which is not an entry point."]);
  });

  it('names an eager entry point that may import a lazy one', () => {
    const table: Record<string, TmEntryPointRule> = {
      '.': { load: 'eager', imports: ['./a'] },
      './a': { load: 'lazy', imports: [] },
    };
    expect(tmCheckEntryPoints(table)).toContain("'.' loads with the app but imports the lazy './a'.");
  });

  it('names a cycle among lazy entry points', () => {
    const table: Record<string, TmEntryPointRule> = {
      '.': { load: 'eager', imports: [] },
      './a': { load: 'lazy', imports: ['./b'] },
      './b': { load: 'lazy', imports: ['./a'] },
    };
    expect(tmCheckEntryPoints(table)).toEqual(['The imports form a cycle: ./a → ./b → ./a.']);
  });
});
