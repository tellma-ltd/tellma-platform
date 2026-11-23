// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { randomBytes } from 'node:crypto';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { fixtureSourceMap, type FixtureRun } from '../../fixtures/source-maps.js';
import { tmCompressedSize } from '../common/files.js';
import { TmBuildFailure } from '../common/problems.js';
import { TmBuildGraph, type TmBuildModule } from '../output/stats.js';
import {
  TM_BUDGETS,
  TM_LAZY_BUDGET_PREFIX,
  tmBootScriptBudget,
  tmBudgetProblems,
  tmFormatBudgetReport,
  tmMeasureBudgets,
  tmReadBudgetLevels,
  type TmBudgetFigure,
} from './budgets.js';

describe('the budget levels', () => {
  it("are the platform's, with the boot script's growing per further string table", () => {
    const { levels, problems } = tmReadBudgetLevels(undefined, 2, 'acme');
    expect(problems).toEqual([]);
    expect(levels.get('initialJs')).toEqual({ warn: 230, error: 260 });
    expect(levels.get('workerShell')).toEqual({ warn: 1536, error: null });
    expect(levels.get('corePrimary')).toEqual({ warn: null, error: 50 });
    expect(levels.get('bootScript')).toEqual({ warn: null, error: 3.4 });
    expect(tmBootScriptBudget(1)).toBe(3);
    expect(tmBootScriptBudget(3)).toBeCloseTo(3.8);
    expect([...levels.keys()]).toEqual(TM_BUDGETS.map((budget) => budget.name));
  });

  it('take a raise with its note, and report a raise without one', () => {
    const noted = tmReadBudgetLevels({ initialJs: { error: 280, note: 'The report studio moved into the shell.' } }, 1, 'acme');
    expect(noted.problems).toEqual([]);
    expect(noted.levels.get('initialJs')).toEqual({ warn: 230, error: 280 });

    const bare = tmReadBudgetLevels({ initialJs: { error: 280 }, initialCss: { error: 41, note: '  ' }, workerShell: { warn: 2048 } }, 1, 'acme');
    expect(bare.problems.map((problem) => [problem.code, problem.severity, problem.key, problem.feature])).toEqual([
      ['budget-note-missing', 'error', 'budget:initialJs', 'acme'],
      ['budget-note-missing', 'error', 'budget:initialCss', 'acme'],
      ['budget-note-missing', 'error', 'budget:workerShell', 'acme'],
    ]);
  });

  it("raise the @tellma/core primary's level only with a note", () => {
    const noted = tmReadBudgetLevels({ corePrimary: { error: 52, note: "Spec 0027's share grew by 2 KB for the launcher." } }, 1, 'acme');
    expect(noted.problems).toEqual([]);
    expect(noted.levels.get('corePrimary')).toEqual({ warn: null, error: 52 });

    const bare = tmReadBudgetLevels({ corePrimary: { error: 52 } }, 1, 'acme');
    expect(bare.problems).toEqual([
      {
        code: 'budget-note-missing',
        severity: 'error',
        key: 'budget:corePrimary',
        feature: 'acme',
        message: "The budget 'corePrimary' is raised above the platform's level without a \"note\" saying why.",
      },
    ]);
    // A lower level, or a warn level where the platform sets none, needs no note.
    expect(tmReadBudgetLevels({ corePrimary: { warn: 45, error: 48 } }, 1, 'acme').problems).toEqual([]);
  });

  it('take a lowered level or a new level without a note', () => {
    const { levels, problems } = tmReadBudgetLevels({ initialJs: { warn: 200, error: 220 }, workerShell: { error: 2048 } }, 1, 'acme');
    expect(problems).toEqual([]);
    expect(levels.get('initialJs')).toEqual({ warn: 200, error: 220 });
    expect(levels.get('workerShell')).toEqual({ warn: 1536, error: 2048 });
  });

  it('refuse a malformed setting or an unknown budget', () => {
    expect(() => tmReadBudgetLevels([], 1, 'acme')).toThrow(/must be an object/);
    expect(() => tmReadBudgetLevels({ initialJS: { error: 300, note: 'x' } }, 1, 'acme')).toThrow(/unknown budget 'initialJS'/);
    expect(() => tmReadBudgetLevels({ initialJs: { error: '300' } }, 1, 'acme')).toThrow(/positive number of KB/);
  });
});

describe('the budget figures', () => {
  let dir: string | undefined;
  afterEach(() => {
    if (dir !== undefined) {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it('measure the initial set, each route beyond it, the shell group and the coded reports', () => {
    dir = mkdtempSync(join(tmpdir(), 'tm-budgets-'));
    // Incompressible content, so each figure is about its raw size.
    const files: Record<string, number> = { 'main-A.js': 4000, 'shared-B.js': 2000, 'search-C.js': 3000, 'entity-D.js': 1000, 'echarts-E.js': 5000, 'styles-F.css': 600, 'tm-boot.js': 900 };
    for (const [name, size] of Object.entries(files)) {
      writeFileSync(join(dir, name), randomBytes(size));
    }
    const graph = new TmBuildGraph([
      { path: 'main-A.js', inputs: ['src/main.ts'], imports: [{ path: 'shared-B.js', kind: 'static' }, { path: 'search-C.js', kind: 'dynamic' }] },
      { path: 'shared-B.js', inputs: ['node_modules/@angular/core/fesm2022/core.mjs'], imports: [] },
      {
        path: 'search-C.js',
        inputs: ['node_modules/@tellma/core/fesm2022/tellma-core-search.mjs'],
        imports: [{ path: 'entity-D.js', kind: 'static' }, { path: 'shared-B.js', kind: 'static' }],
      },
      { path: 'entity-D.js', inputs: ['../tellma-core/entity/src/x.ts'], imports: [] },
      { path: 'echarts-E.js', inputs: ['node_modules/.pnpm/echarts@6.0.0/node_modules/echarts/lib/echarts.js'], imports: [] },
    ]);
    const figures = tmMeasureBudgets({
      browserDir: dir,
      sourceMapsDir: join(dir, 'maps'),
      graph,
      initialJs: graph.staticClosure(['main-A.js']),
      initialCss: ['styles-F.css'],
      shell: ['main-A.js', 'shared-B.js'],
      codedReports: [{ key: 'trial-balance' }],
    });
    const byName = new Map(figures.map((figure) => [figure.name, figure]));
    const near = (name: string, bytes: number, count: number): void => {
      const figure = byName.get(name)!;
      expect(figure.files, name).toBe(count);
      expect(figure.gzip, name).toBeGreaterThanOrEqual(bytes);
      expect(figure.gzip, name).toBeLessThan(bytes + 60 * Math.max(count, 1));
      expect(figure.brotli, name).toBeGreaterThan(0);
    };
    near('initialJs', 6000, 2);
    near('initialCss', 600, 1);
    near('searchRoute', 4000, 2);
    near('detailsRoute', 1000, 1);
    near('echartsEngine', 5000, 1);
    near('workerShell', 6000, 2);
    // No module of the @tellma/core primary is in this build.
    expect(byName.get('corePrimary')!.files).toBe(0);
    near('bootScript', 900, 1);
    expect(byName.get('codedReports')!.files).toBe(1);
    expect(figures.map((figure) => figure.name)).toEqual(TM_BUDGETS.map((budget) => budget.name));
  });

  it('give each lazily loaded chunk no budget covers a line of its own, with no level', () => {
    dir = mkdtempSync(join(tmpdir(), 'tm-budgets-'));
    const files: Record<string, number> = { 'main-A.js': 1000, 'search-B.js': 1000, 'editor-C.js': 3000, 'page-D.js': 2000, 'shared-E.js': 500, 'tm-boot.js': 900 };
    for (const [name, size] of Object.entries(files)) {
      writeFileSync(join(dir, name), randomBytes(size));
    }
    const graph = new TmBuildGraph([
      // main imports the search route and a page lazily, and its own shared chunk lazily too.
      {
        path: 'main-A.js',
        inputs: ['src/main.ts'],
        imports: [{ path: 'search-B.js', kind: 'dynamic' }, { path: 'page-D.js', kind: 'dynamic' }, { path: 'shared-E.js', kind: 'static' }, { path: 'shared-E.js', kind: 'dynamic' }],
      },
      // The search route defers an editor, which the route's own figure leaves out.
      { path: 'search-B.js', inputs: ['node_modules/@tellma/core/fesm2022/tellma-core-search.mjs'], imports: [{ path: 'editor-C.js', kind: 'dynamic' }] },
      { path: 'editor-C.js', inputs: ['src/editor.ts'], imports: [{ path: 'shared-E.js', kind: 'static' }], entryPoint: 'src/editor.ts' },
      { path: 'page-D.js', inputs: ['src/page.ts'], imports: [], entryPoint: 'src/page.ts' },
      { path: 'shared-E.js', inputs: ['src/shared.ts'], imports: [] },
    ]);
    const figures = tmMeasureBudgets({
      browserDir: dir,
      sourceMapsDir: join(dir, 'maps'),
      graph,
      initialJs: graph.staticClosure(['main-A.js']),
      initialCss: [],
      shell: [],
      codedReports: undefined,
    });
    const lazy = figures.filter((figure) => figure.name.startsWith(TM_LAZY_BUDGET_PREFIX));
    expect(lazy.map((figure) => [figure.name, figure.files])).toEqual([
      ['lazy:src/editor.ts', 1],
      ['lazy:src/page.ts', 1],
    ]);
    expect(lazy[0]!.gzip).toBeGreaterThanOrEqual(3000);
    expect(figures.find((figure) => figure.name === 'searchRoute')!.files).toBe(1);
    // A line with no level is printed and never a problem.
    const { levels } = tmReadBudgetLevels(undefined, 1, 'acme');
    expect(tmBudgetProblems(lazy, levels, 'acme')).toEqual([]);
    expect(tmFormatBudgetReport(lazy, levels)).toMatch(/^lazy:src\/editor\.ts +\d+\.\d\d +\d+\.\d\d +- +- +1$/m);
  });

  describe("the @tellma/core primary's figure", () => {
    const core = '../../client/projects/core/tellma-core';
    const angular = 'node_modules/@angular/core/fesm2022/core.mjs';
    /** A random run of code with no line break, which gzip barely shrinks. */
    const piece = (bytes: number): string => randomBytes(bytes).toString('base64');
    const pieces = { a1: piece(3000), a2: piece(2000), b: piece(1500), lazy: piece(4000), app: piece(1000), ng: piece(2500) };

    /** The build: the app's main file mixes app, Angular and primary code; a static chunk is primary only; a lazy chunk holds primary code too. */
    function build(): { readonly graph: TmBuildGraph; readonly dir: string } {
      const root = mkdtempSync(join(tmpdir(), 'tm-budgets-'));
      dir = root;
      const modules: TmBuildModule[] = [
        { path: `${core}/public-api.ts`, imports: [{ path: `${core}/src/a.ts`, relative: true }, { path: `${core}/src/b.ts`, relative: true }] },
        { path: `${core}/src/a.ts`, imports: [{ path: `${core}/src/lazy.ts`, relative: true }, { path: angular, relative: false }] },
        { path: `${core}/src/b.ts`, imports: [] },
        { path: `${core}/src/lazy.ts`, imports: [] },
        { path: 'src/main.ts', imports: [{ path: `${core}/public-api.ts`, relative: false }, { path: angular, relative: false }] },
        { path: 'src/other.ts', imports: [] },
        { path: angular, imports: [] },
      ];
      const graph = new TmBuildGraph(
        [
          {
            path: 'main-A.js',
            inputs: ['src/main.ts', `${core}/src/a.ts`, angular],
            imports: [{ path: 'chunk-B.js', kind: 'static' }, { path: 'other-D.js', kind: 'static' }, { path: 'lazy-C.js', kind: 'dynamic' }],
          },
          // The public API is a list of re-exports: a module with no code of its own in the file.
          { path: 'chunk-B.js', inputs: [`${core}/src/b.ts`, `${core}/public-api.ts`], imports: [] },
          { path: 'lazy-C.js', inputs: [`${core}/src/lazy.ts`], imports: [] },
          { path: 'other-D.js', inputs: ['src/other.ts'], imports: [] },
        ],
        modules,
      );
      // --- The files and their maps; the file with no primary module has no map, and none is read ---
      const sources = ['src/main.ts', `${core}/src/a.ts`, angular, `${core}/src/b.ts`, `${core}/src/lazy.ts`];
      const files: Record<string, { code: string; lines: FixtureRun[][] }> = {
        'main-A.js': {
          code: `${pieces.app}${pieces.a1}\n${pieces.a2}${pieces.ng}`,
          lines: [
            [[0, 0], [pieces.app.length, 1]],
            [[0, 1], [pieces.a2.length, 2]],
          ],
        },
        'chunk-B.js': { code: pieces.b, lines: [[[0, 3]]] },
        'lazy-C.js': { code: pieces.lazy, lines: [[[0, 4]]] },
      };
      mkdirSync(join(root, 'maps'));
      for (const [name, file] of Object.entries(files)) {
        writeFileSync(join(root, name), file.code);
        writeFileSync(join(root, 'maps', `${name}.map`), fixtureSourceMap(sources, file.lines));
      }
      writeFileSync(join(root, 'other-D.js'), piece(500));
      writeFileSync(join(root, 'tm-boot.js'), 'void 0;');
      return { graph, dir: root };
    }

    /** Measures the build with its initial set: main and its static imports. */
    const measure = (graph: TmBuildGraph, root: string): TmBudgetFigure =>
      tmMeasureBudgets({
        browserDir: root,
        sourceMapsDir: join(root, 'maps'),
        graph,
        initialJs: graph.staticClosure(['main-A.js']),
        initialCss: [],
        shell: [],
        codedReports: undefined,
      }).find((figure) => figure.name === 'corePrimary')!;

    it("is the primary's code in the initial files alone, compressed together", () => {
      const { graph, dir: root } = build();
      // In file order: the static chunk, then main's two runs; the lazy chunk's
      // code is not on the first frame, and the app's and Angular's code is not the primary's.
      expect(measure(graph, root)).toEqual({ name: 'corePrimary', ...tmCompressedSize(`${pieces.b}${pieces.a1}${pieces.a2}`), files: 2 });
    });

    it('fails the build when a file holding primary code has no source map', () => {
      const { graph, dir: root } = build();
      rmSync(join(root, 'maps', 'chunk-B.js.map'));
      expect(() => measure(graph, root)).toThrow(TmBuildFailure);
      expect(() => measure(graph, root)).toThrow(/chunk-B\.js\.map does not exist/);
    });

    it('fails the build when the maps name the modules otherwise than the stats', () => {
      const { graph, dir: root } = build();
      for (const name of ['main-A.js', 'chunk-B.js']) {
        writeFileSync(join(root, 'maps', `${name}.map`), fixtureSourceMap(['/elsewhere/a.ts', '/elsewhere/b.ts'], [[[0, 0], [10, 1]]]));
      }
      expect(() => measure(graph, root)).toThrow(/The source maps of chunk-B\.js, main-A\.js attribute none of their code to the @tellma\/core primary/);
    });
  });

  it('report a figure over its error level as an error, over its warn level as a warning', () => {
    const { levels } = tmReadBudgetLevels(undefined, 1, 'acme');
    const figure = (name: string, kb: number): TmBudgetFigure => ({ name, gzip: kb * 1024, brotli: kb * 900, files: 1 });
    const problems = tmBudgetProblems([figure('initialJs', 240), figure('initialCss', 41), figure('bootScript', 3), figure('workerShell', 1600)], levels, 'acme');
    expect(problems).toEqual([
      { code: 'budget-warning', severity: 'warning', key: 'budget:initialJs', feature: 'acme', message: "240.00 KB gzip is over the budget's warn level of 230 KB." },
      { code: 'budget-exceeded', severity: 'error', key: 'budget:initialCss', feature: 'acme', message: "41.00 KB gzip is over the budget's error level of 40 KB." },
      { code: 'budget-warning', severity: 'warning', key: 'budget:workerShell', feature: 'acme', message: "1600.00 KB gzip is over the budget's warn level of 1536 KB." },
    ]);
    expect(tmBudgetProblems([figure('initialJs', 261)], levels, 'acme').map((problem) => problem.code)).toEqual(['budget-exceeded']);
  });

  it("fail the @tellma/core primary over 50 KB, and pass it at 50 KB or under", () => {
    const { levels } = tmReadBudgetLevels(undefined, 1, 'acme');
    const primary = (bytes: number): TmBudgetFigure => ({ name: 'corePrimary', gzip: bytes, brotli: bytes, files: 2 });
    expect(tmBudgetProblems([primary(50 * 1024 + 1)], levels, 'acme')).toEqual([
      { code: 'budget-exceeded', severity: 'error', key: 'budget:corePrimary', feature: 'acme', message: "50.00 KB gzip is over the budget's error level of 50 KB." },
    ]);
    expect(tmBudgetProblems([primary(50 * 1024)], levels, 'acme')).toEqual([]);
    expect(tmBudgetProblems([primary(37 * 1024)], levels, 'acme')).toEqual([]);
    // A distribution's noted raise moves the line.
    const raised = tmReadBudgetLevels({ corePrimary: { error: 52, note: 'A share grew.' } }, 1, 'acme').levels;
    expect(tmBudgetProblems([primary(51 * 1024)], raised, 'acme')).toEqual([]);
  });

  it('print gzip and Brotli beside the levels', () => {
    const { levels } = tmReadBudgetLevels(undefined, 1, 'acme');
    const report = tmFormatBudgetReport(
      [
        { name: 'initialJs', gzip: 102_400, brotli: 92_160, files: 3 },
        { name: 'echartsEngine', gzip: 0, brotli: 0, files: 0 },
      ],
      levels,
    );
    expect(report.split('\n')).toEqual([
      'Budget         Gzip KB  Brotli KB  Warn  Error  Files',
      'initialJs       100.00      90.00   230    260      3',
      'echartsEngine        -          -     -    170      0',
      '',
    ]);
  });
});
