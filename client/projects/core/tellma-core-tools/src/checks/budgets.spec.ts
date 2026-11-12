// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { randomBytes } from 'node:crypto';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

import { afterEach, describe, expect, it } from 'vitest';

import { TmBuildGraph } from '../output/stats.js';
import {
  TM_BUDGETS,
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
    near('bootScript', 900, 1);
    expect(byName.get('codedReports')!.files).toBe(1);
    expect(figures.map((figure) => figure.name)).toEqual(TM_BUDGETS.map((budget) => budget.name));
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
