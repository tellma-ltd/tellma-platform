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
      './error-paths',
      './private',
      './search-normalize',
    ]);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './shell')].sort()).toEqual([
      '.',
      './attention',
      './error-paths',
      './private',
      './search-normalize',
    ]);
  });

  it('keeps the shared entry points off the primary and out of each other', () => {
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './private')]).toEqual(['.']);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './search-normalize')]).toEqual([]);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './error-paths')]).toEqual([]);
  });

  it('lets lazy entry points import every eager and shared one', () => {
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './search')].sort()).toEqual([
      '.',
      './attention',
      './column-filter',
      './entity',
      './error-paths',
      './expression-editor',
      './memory',
      './path-picker',
      './private',
      './search-normalize',
      './search-view',
      './shell',
    ]);
  });

  it("lets the page hosts import the tab's memory, and the home page's the recent pages", () => {
    for (const host of ['./search', './details', './reports']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, host).has('./memory')).toBe(true);
    }
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './dashboards').has('./recents')).toBe(true);
    // No eager entry point reaches either.
    for (const eager of ['.', './attention', './shell']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./memory')).toBe(false);
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./recents')).toBe(false);
    }
  });

  it('lets only the hosts that edit records import the drafts, never the search page', () => {
    expect(TM_CORE_ENTRY_POINTS['./drafts']).toEqual({ load: 'lazy', imports: [] });
    for (const host of ['./details', './admin', './report-studio']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, host).has('./drafts')).toBe(true);
    }
    for (const other of ['.', './attention', './shell', './search', './memory', './entity', './reports']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, other).has('./drafts')).toBe(false);
    }
  });

  it('keeps the page-context snapshot off the first frame, for the support dump to load', () => {
    expect(TM_CORE_ENTRY_POINTS['./page-snapshot']).toEqual({ load: 'lazy', imports: [] });
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './support').has('./page-snapshot')).toBe(true);
    for (const eager of ['.', './attention', './shell']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./page-snapshot')).toBe(false);
    }
  });

  it('keeps the expression editor and the path picker out of the entity entry point, for their hosts to load', () => {
    expect(TM_CORE_ENTRY_POINTS['./expression-editor']).toEqual({ load: 'lazy', imports: ['./entity'] });
    expect(TM_CORE_ENTRY_POINTS['./path-picker']).toEqual({ load: 'lazy', imports: ['./entity'] });
    for (const host of ['./search', './admin', './report-studio']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, host).has('./expression-editor')).toBe(true);
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, host).has('./path-picker')).toBe(true);
    }
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './reports').has('./expression-editor')).toBe(true);
    for (const other of ['.', './attention', './shell', './entity', './column-filter']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, other).has('./expression-editor')).toBe(false);
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, other).has('./path-picker')).toBe(false);
    }
  });

  it('keeps the edit side out of the entity entry point and the page hosts, for an editing page or dialog to load', () => {
    expect(TM_CORE_ENTRY_POINTS['./entity-editing']).toEqual({ load: 'lazy', imports: ['./entity'] });
    for (const other of ['.', './attention', './shell', './entity', './search', './details']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, other).has('./entity-editing')).toBe(false);
    }
  });

  it('keeps the column filter control out of the entity entry point, for the search page to load', () => {
    expect(TM_CORE_ENTRY_POINTS['./column-filter']).toEqual({ load: 'lazy', imports: ['./entity', './entity-editing'] });
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './search').has('./column-filter')).toBe(true);
    expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, './entity').has('./column-filter')).toBe(false);
    for (const eager of ['.', './attention', './shell']) {
      expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has('./column-filter')).toBe(false);
    }
  });

  it('keeps the worker updates and the error presenter off the first frame', () => {
    for (const lazy of ['./updates', './errors']) {
      expect(TM_CORE_ENTRY_POINTS[lazy]).toEqual({ load: 'lazy', imports: [] });
      for (const eager of ['.', './attention', './shell']) {
        expect(tmAllowedImports(TM_CORE_ENTRY_POINTS, eager).has(lazy)).toBe(false);
      }
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
