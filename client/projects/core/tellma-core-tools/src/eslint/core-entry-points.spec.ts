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
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './attention')].sort()).toEqual(['.', './private']);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './shell')].sort()).toEqual(['.', './attention', './private']);
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './private')]).toEqual(['.']);
  });

  it('lets lazy entry points import every eager one', () => {
    expect([...tmAllowedImports(TM_CORE_ENTRY_POINTS, './search')].sort()).toEqual([
      '.',
      './attention',
      './entity',
      './private',
      './shell',
    ]);
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
