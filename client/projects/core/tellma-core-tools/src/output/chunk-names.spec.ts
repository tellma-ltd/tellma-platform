// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { tmContentHash } from '../common/files.js';
import { TmBuildFailure } from '../common/problems.js';
import { tmSettleChunks } from './chunk-names.js';

// The settling of an optimized build's chunks, on small outputs written the
// way Angular's chunk optimizer writes them: Rollup's base64url names, lazy
// chunks importing the main entry, hidden source maps beside their files,
// and stats.json with leftovers of the chunks Rollup merged away.

const scratches: string[] = [];
afterEach(() => {
  for (const dir of scratches.splice(0)) {
    rmSync(dir, { recursive: true, force: true });
  }
});

/** One chunk of a synthetic output: its text and what the stats list among its imports. */
interface Chunk {
  /** The chunk's text. */
  readonly text: string;
  /** Its imports in the stats, with how; by default, every chunk its text names, statically. */
  readonly imports?: readonly { readonly path: string; readonly kind: string }[];
}

/** A synthetic optimized output. */
interface Output {
  /** The deployable folder. */
  readonly browser: string;
  /** stats.json. */
  readonly stats: string;
}

/** The index.html that loads a main entry. */
function index(main: string): string {
  return `<!doctype html><html><head><base href="/"><link rel="stylesheet" href="styles-ABCD1234.css"></head><body><script src="tm-boot.js"></script><script src="${main}" type="module"></script></body></html>`;
}

/**
 * Writes an output: each chunk with a hidden source map, index.html, a
 * stylesheet, and stats.json with the chunks and the leftovers given.
 */
function write(chunks: Record<string, Chunk>, options: { readonly main?: string; readonly leftovers?: Record<string, unknown> } = {}): Output {
  const dir = mkdtempSync(join(tmpdir(), 'tm-chunks-'));
  scratches.push(dir);
  const browser = join(dir, 'browser');
  mkdirSync(browser);
  const outputs: Record<string, unknown> = { 'styles-ABCD1234.css': { imports: [], inputs: {} } };
  for (const [name, chunk] of Object.entries(chunks)) {
    writeFileSync(join(browser, name), chunk.text);
    writeFileSync(join(browser, `${name}.map`), JSON.stringify({ version: 3, file: name, sources: ['src/a.ts'], mappings: 'AAAA' }));
    const named = [...chunk.text.matchAll(/'\.\/([^']+\.js)'/g)].map((match) => match[1]!);
    outputs[name] = {
      bytes: chunk.text.length,
      inputs: { 'src/a.ts': { bytesInOutput: chunk.text.length } },
      imports: chunk.imports ?? [...new Set(named)].map((path) => ({ path, kind: 'import-statement' })),
      exports: [],
    };
  }
  writeFileSync(join(browser, 'styles-ABCD1234.css'), 'a{}');
  writeFileSync(join(browser, 'tm-boot.js'), 'void 0;');
  writeFileSync(join(browser, 'index.html'), index(options.main ?? 'main-AW45UDH5.js'));
  const stats = join(dir, 'stats.json');
  writeFileSync(stats, JSON.stringify({ inputs: { 'src/a.ts': { bytes: 1, imports: [] } }, outputs: { ...outputs, ...options.leftovers } }, null, 2));
  return { browser, stats };
}

/** A small app: main imports a leaf statically and a lazy chunk with import(), and the lazy chunk imports main. */
function app(leaf = 'export const a=1;'): Record<string, Chunk> {
  return {
    'main-AW45UDH5.js': {
      text: "import{a}from'./chunk-Bb_-cc11.js';const l=()=>import('./chunk-Dd-Ee22F.js');export{a as b};",
      imports: [
        { path: 'chunk-Bb_-cc11.js', kind: 'import-statement' },
        { path: 'chunk-Dd-Ee22F.js', kind: 'dynamic-import' },
      ],
    },
    'chunk-Bb_-cc11.js': { text: leaf },
    'chunk-Dd-Ee22F.js': { text: "import{b}from'./main-AW45UDH5.js';export const d=b;" },
  };
}

/** The JavaScript files of a folder, in ordinal order. */
function scripts(browser: string): string[] {
  return readdirSync(browser)
    .filter((file) => file.endsWith('.js'))
    .sort();
}

describe('settling the chunks of an optimized build', () => {
  it('names every chunk the app loads by its content, and rewrites every reference to it', () => {
    const { browser, stats } = write(app());
    const length = (file: string): number => readFileSync(join(browser, file)).length;
    const before = ['main-AW45UDH5.js', 'chunk-Bb_-cc11.js', 'chunk-Dd-Ee22F.js'].map(length);
    const { renamed, removed } = tmSettleChunks(browser, stats);

    expect(removed).toEqual([]);
    expect([...renamed.keys()].sort()).toEqual(['chunk-Bb_-cc11.js', 'chunk-Dd-Ee22F.js', 'main-AW45UDH5.js']);
    const main = renamed.get('main-AW45UDH5.js')!;
    const leaf = renamed.get('chunk-Bb_-cc11.js')!;
    const lazy = renamed.get('chunk-Dd-Ee22F.js')!;
    for (const name of [main, leaf, lazy]) {
      expect(name).toMatch(/^(?:main|chunk)-[0-9A-Z]{8}\.js$/);
    }
    // The old files are gone, and tm-boot.js, which no chunk of the graph is, keeps its name.
    expect(scripts(browser)).toEqual([leaf, lazy, main, 'tm-boot.js'].sort());
    // Every reference: the imports, the dynamic import, the script, the maps and the stats.
    expect(readFileSync(join(browser, main), 'utf8')).toBe(`import{a}from'./${leaf}';const l=()=>import('./${lazy}');export{a as b};`);
    expect(readFileSync(join(browser, lazy), 'utf8')).toBe(`import{b}from'./${main}';export const d=b;`);
    expect(readFileSync(join(browser, 'index.html'), 'utf8')).toBe(index(main));
    for (const name of [main, leaf, lazy]) {
      expect(JSON.parse(readFileSync(join(browser, `${name}.map`), 'utf8'))).toMatchObject({ file: name, mappings: 'AAAA' });
    }
    expect(readdirSync(browser).filter((file) => file.endsWith('.map'))).toHaveLength(3);
    const settled = JSON.parse(readFileSync(stats, 'utf8')) as { outputs: Record<string, { imports: { path: string; kind: string }[] }> };
    expect(Object.keys(settled.outputs).sort()).toEqual([leaf, lazy, main, 'styles-ABCD1234.css'].sort());
    expect(settled.outputs[main]!.imports).toEqual([
      { path: leaf, kind: 'import-statement' },
      { path: lazy, kind: 'dynamic-import' },
    ]);
    // No byte moves, so the source maps and the stats' byte counts stay exact.
    expect([main, leaf, lazy].map(length)).toEqual(before);
  });

  it('names a chunk outside an import cycle by the hash of its final bytes', () => {
    const { browser, stats } = write(app());
    const { renamed } = tmSettleChunks(browser, stats);
    const leaf = renamed.get('chunk-Bb_-cc11.js')!;
    expect(leaf).toBe(`chunk-${tmContentHash(readFileSync(join(browser, leaf)), 8)}.js`);
  });

  it('gives an unchanged output the same names, and new names to a changed chunk and every chunk whose bytes name it', () => {
    const settle = (leaf?: string): Map<string, string> => {
      const chunks = app(leaf);
      // A second leaf, which nothing of the change reaches.
      const other = { 'chunk-Gg11Hh22.js': { text: 'export const g=2;' } };
      const main = chunks['main-AW45UDH5.js']!;
      const output = write({
        ...chunks,
        ...other,
        'main-AW45UDH5.js': { text: `import'./chunk-Gg11Hh22.js';${main.text}`, imports: [{ path: 'chunk-Gg11Hh22.js', kind: 'import-statement' }, ...main.imports!] },
      });
      return new Map(tmSettleChunks(output.browser, output.stats).renamed);
    };
    const first = settle();
    expect(settle()).toEqual(first);
    const changed = settle('export const a=3;');
    for (const old of ['main-AW45UDH5.js', 'chunk-Bb_-cc11.js', 'chunk-Dd-Ee22F.js']) {
      expect(changed.get(old), old).not.toBe(first.get(old));
    }
    expect(changed.get('chunk-Gg11Hh22.js')).toBe(first.get('chunk-Gg11Hh22.js'));
  });

  it('tells apart chunks in a cycle whose code differs only in the chunks it names', () => {
    const facade = (target: string): string => `export{v as default}from'./${target}';`;
    const { browser, stats } = write({
      'main-AW45UDH5.js': { text: "const a=()=>import('./chunk-Aa11Aa11.js'),b=()=>import('./chunk-Bb22Bb22.js');export const m=1;" },
      'chunk-Aa11Aa11.js': { text: facade('chunk-Cc33Cc33.js') },
      'chunk-Bb22Bb22.js': { text: facade('chunk-Dd44Dd44.js') },
      'chunk-Cc33Cc33.js': { text: "import{m}from'./main-AW45UDH5.js';export const v=m+1;" },
      'chunk-Dd44Dd44.js': { text: "import{m}from'./main-AW45UDH5.js';export const v=m+2;" },
    });
    const { renamed } = tmSettleChunks(browser, stats);
    expect(new Set(renamed.values()).size).toBe(5);
    expect(renamed.get('chunk-Aa11Aa11.js')).not.toBe(renamed.get('chunk-Bb22Bb22.js'));
    expect(readFileSync(join(browser, renamed.get('chunk-Aa11Aa11.js')!), 'utf8')).toBe(facade(renamed.get('chunk-Cc33Cc33.js')!));
  });

  it("deletes a chunk no import reaches, with its map and its stats entry, and drops the maps of chunks Rollup merged away", () => {
    const { browser, stats } = write(
      { ...app(), 'chunk-KECAAUBL.js': { text: "import{x}from'./chunk-FLU25SEK.js';export{x};", imports: [{ path: 'chunk-FLU25SEK.js', kind: 'import-statement' }] } },
      { leftovers: { 'chunk-FLU25SEK.js.map': { imports: [], inputs: {} }, 'chunk-KECAAUBL.js.map': { imports: [], inputs: {} } } },
    );
    const { removed } = tmSettleChunks(browser, stats);
    expect(removed).toEqual(['chunk-KECAAUBL.js']);
    expect(existsSync(join(browser, 'chunk-KECAAUBL.js'))).toBe(false);
    expect(existsSync(join(browser, 'chunk-KECAAUBL.js.map'))).toBe(false);
    const settled = JSON.parse(readFileSync(stats, 'utf8')) as { outputs: Record<string, unknown> };
    expect(Object.keys(settled.outputs).filter((file) => /KECAAUBL|FLU25SEK/.test(file))).toEqual([]);
    expect(Object.keys(settled.outputs)).toHaveLength(4);
  });

  it('refuses to delete a chunk no import reaches while a file of the output still names it', () => {
    const { browser, stats } = write({
      ...app(),
      'main-AW45UDH5.js': { ...app()['main-AW45UDH5.js']!, text: `${app()['main-AW45UDH5.js']!.text}const w="chunk-KECAAUBL.js";` },
      'chunk-KECAAUBL.js': { text: 'export const k=1;' },
    });
    expect(() => tmSettleChunks(browser, stats)).toThrow(TmBuildFailure);
    expect(() => tmSettleChunks(browser, stats)).toThrow(/^main-AW45UDH5\.js names chunk-KECAAUBL\.js, which no import in stats\.json reaches from index\.html/);
    expect(existsSync(join(browser, 'chunk-KECAAUBL.js'))).toBe(true);
  });

  it("refuses stats whose imports for a chunk are not the chunks its text names, and writes nothing", () => {
    const chunks = app();
    // The lazy chunk names the leaf, which main's import reaches, but the stats leave it out.
    const unlisted = write({
      ...chunks,
      'chunk-Dd-Ee22F.js': { text: "import{b}from'./main-AW45UDH5.js';import'./chunk-Bb_-cc11.js';", imports: [{ path: 'main-AW45UDH5.js', kind: 'import-statement' }] },
    });
    expect(() => tmSettleChunks(unlisted.browser, unlisted.stats)).toThrow(
      /^stats\.json disagrees with chunk-Dd-Ee22F\.js: it names chunk-Bb_-cc11\.js, which the stats do not list among its imports\.$/,
    );
    const unnamed = write({ ...chunks, 'chunk-Bb_-cc11.js': { text: 'export const a=1;', imports: [{ path: 'chunk-Dd-Ee22F.js', kind: 'import-statement' }] } });
    expect(() => tmSettleChunks(unnamed.browser, unnamed.stats)).toThrow(
      /^stats\.json disagrees with chunk-Bb_-cc11\.js: the stats list chunk-Dd-Ee22F\.js among its imports, which it does not name\.$/,
    );
    expect(scripts(unnamed.browser)).toEqual(['chunk-Bb_-cc11.js', 'chunk-Dd-Ee22F.js', 'main-AW45UDH5.js', 'tm-boot.js']);
  });

  it('refuses a chunk the app loads that the build did not write', () => {
    const { browser, stats } = write(app());
    rmSync(join(browser, 'chunk-Dd-Ee22F.js'));
    expect(() => tmSettleChunks(browser, stats)).toThrow(/^stats\.json says the app loads chunk-Dd-Ee22F\.js, which the build did not write\.$/);
  });

  it('refuses a chunk name with no eight-character hash to replace, and a name it cannot rename', () => {
    const plain = write({ 'main.js': { text: 'export const m=1;' } }, { main: 'main.js' });
    expect(() => tmSettleChunks(plain.browser, plain.stats)).toThrow(/^main\.js has no eight-character hash before its extension/);
    const chunks = app();
    const spelled = write({ ...chunks, 'chunk-Dd-Ee22F.js': { text: `${chunks['chunk-Dd-Ee22F.js']!.text}// main-AW45UDH5.js` } });
    expect(() => tmSettleChunks(spelled.browser, spelled.stats)).toThrow(/names main-AW45UDH5\.js outside a quoted import or attribute/);
  });
});
