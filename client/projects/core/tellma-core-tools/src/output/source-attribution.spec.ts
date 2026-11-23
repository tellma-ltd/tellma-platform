// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { fixtureMappings, fixtureSourceMap } from '../../fixtures/source-maps.js';
import { TmBuildFailure } from '../common/problems.js';
import { tmCodeFromSources, tmReadSourceMap, type TmSourceMap } from './source-attribution.js';

/** A map over the sources with the runs of each generated line. */
const map = (sources: string[], lines: Parameters<typeof fixtureMappings>[0], sourceRoot?: string): TmSourceMap =>
  JSON.parse(fixtureSourceMap(sources, lines, sourceRoot)) as TmSourceMap;

describe('the code a source map attributes to some sources', () => {
  it('takes each run of an accepted source up to the next run or the line end', () => {
    // Line 0: a.ts, then glue with no source, then b.ts, then a.ts again; line 1: b.ts, then a.ts.
    const code = 'AAAA;;BBBaa\nbbbAAA';
    const sources = map(['src/a.ts', 'src/b.ts'], [
      [[0, 0], [4, null], [6, 1], [9, 0]],
      [[0, 1], [3, 0]],
    ]);
    expect(tmCodeFromSources(code, sources, (source) => source === 'src/a.ts')).toBe('AAAAaaAAA');
    expect(tmCodeFromSources(code, sources, (source) => source === 'src/b.ts')).toBe('BBBbbb');
    expect(tmCodeFromSources(code, sources, () => false)).toBe('');
  });

  it('decodes hand-written mappings: a relative column within a line and a relative source across lines', () => {
    // AAAA: column 0, source 0. GAAC: column 3, source 0. ACAE: next line, column 0, source 1.
    const sources: TmSourceMap = { sources: ['x.ts', 'y.ts'], mappings: 'AAAA,GAAC;ACAE' };
    expect(tmCodeFromSources('xxxXX\nyy', sources, (source) => source === 'x.ts')).toBe('xxxXX');
    expect(tmCodeFromSources('xxxXX\nyy', sources, (source) => source === 'y.ts')).toBe('yy');
  });

  it('counts columns in UTF-16 code units, as the bundler writes them', () => {
    // 'é' is one code unit and '𝟘' two, so b.ts starts at column 4.
    const code = 'é𝟘xb';
    const sources = map(['a.ts', 'b.ts'], [[[0, 0], [4, 1]]]);
    expect(tmCodeFromSources(code, sources, (source) => source === 'a.ts')).toBe('é𝟘x');
    expect(tmCodeFromSources(code, sources, (source) => source === 'b.ts')).toBe('b');
  });

  it('tests each source with the source root before it and forward slashes', () => {
    const sources = map(['src\\a.ts', null as unknown as string], [[[0, 0], [2, 1]]], '../pkg');
    const seen: string[] = [];
    tmCodeFromSources('aabb', sources, (source) => {
      seen.push(source);
      return true;
    });
    expect(seen).toEqual(['../pkg/src/a.ts']);
  });

  it('refuse malformed mappings', () => {
    const bad = (mappings: string): (() => string) => () => tmCodeFromSources('x', { sources: ['a.ts'], mappings }, () => true);
    expect(bad('A!AA')).toThrow(TmBuildFailure);
    // A segment of two or three fields, and a VLQ left unfinished.
    expect(bad('AA')).toThrow(/malformed mappings/);
    expect(bad('AAA')).toThrow(/malformed mappings/);
    expect(bad('AAAg')).toThrow(/malformed mappings/);
    // A source index outside the sources.
    expect(bad('ACAA')).toThrow(/malformed mappings/);
  });
});

describe('reading a source map', () => {
  let dir: string | undefined;
  afterEach(() => {
    if (dir !== undefined) {
      rmSync(dir, { recursive: true, force: true });
      dir = undefined;
    }
  });

  it('reads a version 3 map, and refuses a missing file, bad JSON or another shape', () => {
    const root = mkdtempSync(join(tmpdir(), 'tm-source-map-'));
    dir = root;
    const file = (name: string, text: string): string => {
      writeFileSync(join(root, name), text);
      return join(root, name);
    };
    expect(tmReadSourceMap(file('ok.map', fixtureSourceMap(['a.ts'], [[[0, 0]]], 'root')))).toEqual({ sources: ['a.ts'], mappings: 'AAAA', sourceRoot: 'root' });
    expect(() => tmReadSourceMap(join(root, 'missing.map'))).toThrow(/does not exist/);
    expect(() => tmReadSourceMap(file('bad.map', '{'))).toThrow(/not valid JSON/);
    expect(() => tmReadSourceMap(file('v2.map', JSON.stringify({ version: 2, sources: [], mappings: '' })))).toThrow(/not a version 3 source map/);
    // An indexed map, whose sections hold the mappings, is not one the bundler writes.
    expect(() => tmReadSourceMap(file('sections.map', JSON.stringify({ version: 3, sections: [] })))).toThrow(/not a version 3 source map/);
  });
});
