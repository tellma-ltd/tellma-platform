// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { tmCompressedSize, type TmCompressedSize } from '../common/files.js';
import { TmBuildFailure, type TmBuildProblem } from '../common/problems.js';
import { tmEchartsModule, tmEntryPointModule, type TmBuildGraph } from '../output/stats.js';

// The application's budgets (spec 0024 §12), measured gzip level 9 on the
// output, with Brotli reported beside. A distribution raises one in its
// package.json, `"tellma": { "budgets": { "<name>": { "error": 280, "note":
// "…" } } }`, and a raise needs its note. The boot script's ceiling grows
// with its string tables: 3 KB for the code with English, which every boot
// script embeds, and 0.4 KB for each further table. Every lazily loaded
// chunk no budget covers, a `@defer` block's or a distribution's route,
// gets a line of its own with no level: the metafile records a dynamic
// import, not whether a route or a `@defer` block made it.

/** The name prefix of a lazily loaded chunk's own line, followed by the module it starts at. */
export const TM_LAZY_BUDGET_PREFIX = 'lazy:';

/** The build problem code of a figure over its error level. */
export const TM_BUDGET_EXCEEDED = 'budget-exceeded';
/** The build problem code of a figure over its warn level; a warning. */
export const TM_BUDGET_WARNING = 'budget-warning';
/** The build problem code of a raised budget without a note. */
export const TM_BUDGET_NOTE_MISSING = 'budget-note-missing';

/** A budget's levels in KB (1,024 bytes); null where the budget has none. */
export interface TmBudgetLevels {
  /** Over this, the build warns. */
  readonly warn: number | null;
  /** Over this, the build fails. */
  readonly error: number | null;
}

/** One of the application's budgets. */
export interface TmBudgetDefinition extends TmBudgetLevels {
  /** The budget's name, as package.json raises it. */
  readonly name: string;
  /** What it measures. */
  readonly label: string;
}

/** The application's budgets, with the platform's levels. */
export const TM_BUDGETS: readonly TmBudgetDefinition[] = Object.freeze([
  { name: 'initialJs', label: 'Initial JavaScript, with the modulepreloaded /shell and /attention', warn: 230, error: 260 },
  { name: 'initialCss', label: 'Initial CSS, fonts excluded', warn: null, error: 40 },
  { name: 'searchRoute', label: 'The search route beyond the initial set', warn: null, error: 110 },
  { name: 'detailsRoute', label: 'The details route beyond the initial set', warn: null, error: 80 },
  { name: 'echartsEngine', label: 'The ECharts engine beyond the initial set', warn: null, error: 170 },
  { name: 'workerShell', label: "The worker's shell group", warn: 1536, error: null },
  { name: 'codedReports', label: "The Core chunk's codedReports section", warn: null, error: 16 },
  { name: 'bootScript', label: 'tm-boot.js', warn: null, error: 3 },
]);

/**
 * The boot script's error level for a number of string tables.
 *
 * @param tables - The tables TM_BOOT carries, English included.
 * @returns The level in KB.
 */
export function tmBootScriptBudget(tables: number): number {
  return 3 + 0.4 * Math.max(0, tables - 1);
}

/** A member of a parsed JSON object, or undefined. */
function member(value: unknown, name: string): unknown {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>)[name] : undefined;
}

/**
 * Reads the budgets a distribution sets in package.json over the platform's
 * levels, reporting each raise without a note.
 *
 * @param raw - The `tellma.budgets` member of package.json, or undefined.
 * @param bootTables - The boot script's string tables, which set its default level.
 * @param feature - The distribution's slug.
 * @returns The levels by budget name, and the problems.
 * @throws TmBuildFailure when the member is malformed or names an unknown budget.
 */
export function tmReadBudgetLevels(
  raw: unknown,
  bootTables: number,
  feature: string,
): { readonly levels: ReadonlyMap<string, TmBudgetLevels>; readonly problems: TmBuildProblem[] } {
  const levels = new Map<string, TmBudgetLevels>(
    TM_BUDGETS.map((budget) => [
      budget.name,
      { warn: budget.warn, error: budget.name === 'bootScript' ? tmBootScriptBudget(bootTables) : budget.error },
    ]),
  );
  const problems: TmBuildProblem[] = [];
  if (raw === undefined) {
    return { levels, problems };
  }
  if (typeof raw !== 'object' || raw === null || Array.isArray(raw)) {
    throw new TmBuildFailure('package.json: "tellma.budgets" must be an object of budgets by name.');
  }
  for (const [name, setting] of Object.entries(raw)) {
    const platform = levels.get(name);
    if (platform === undefined) {
      throw new TmBuildFailure(`package.json: "tellma.budgets" names the unknown budget '${name}'; the budgets are ${TM_BUDGETS.map((budget) => budget.name).join(', ')}.`);
    }
    const level = (kind: 'warn' | 'error'): number | null => {
      const value = member(setting, kind);
      if (value === undefined) {
        return platform[kind];
      }
      if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) {
        throw new TmBuildFailure(`package.json: "tellma.budgets.${name}.${kind}" must be a positive number of KB.`);
      }
      return value;
    };
    const set = { warn: level('warn'), error: level('error') };
    levels.set(name, set);
    // --- A raise needs its reason ---
    const raised = (kind: 'warn' | 'error'): boolean => platform[kind] !== null && set[kind] !== null && set[kind] > platform[kind];
    const note = member(setting, 'note');
    if ((raised('warn') || raised('error')) && (typeof note !== 'string' || note.trim() === '')) {
      problems.push({
        code: TM_BUDGET_NOTE_MISSING,
        severity: 'error',
        key: `budget:${name}`,
        feature,
        message: `The budget '${name}' is raised above the platform's level without a "note" saying why.`,
      });
    }
  }
  return { levels, problems };
}

/** One measured figure. */
export interface TmBudgetFigure extends TmCompressedSize {
  /** The budget's name. */
  readonly name: string;
  /** The files measured; zero means the build has nothing the budget measures. */
  readonly files: number;
}

/** What the budgets measure, from the finished output. */
export interface TmBudgetInputs {
  /** The deployable output. */
  readonly browserDir: string;
  /** The build's chunk graph. */
  readonly graph: TmBuildGraph;
  /** The initial JavaScript: the scripts, their static imports and the modulepreloads. */
  readonly initialJs: ReadonlySet<string>;
  /** The stylesheets `index.html` links. */
  readonly initialCss: readonly string[];
  /** The worker's shell group. */
  readonly shell: readonly string[];
  /** The Core chunk's codedReports section, or undefined when it has none. */
  readonly codedReports: readonly unknown[] | undefined;
}

/**
 * Measures every budget's figure.
 *
 * @param inputs - What the budgets measure.
 * @returns One figure per budget, in TM_BUDGETS's order, then one per lazily
 * loaded chunk no budget covers, in its file's ordinal order.
 */
export function tmMeasureBudgets(inputs: TmBudgetInputs): TmBudgetFigure[] {
  const sizes = new Map<string, TmCompressedSize>();
  const sizeOf = (file: string): TmCompressedSize => {
    let size = sizes.get(file);
    if (size === undefined) {
      size = tmCompressedSize(readFileSync(join(inputs.browserDir, ...file.split('/'))));
      sizes.set(file, size);
    }
    return size;
  };
  const sum = (name: string, files: Iterable<string>): TmBudgetFigure => {
    let gzip = 0;
    let brotli = 0;
    let count = 0;
    for (const file of files) {
      const size = sizeOf(file);
      gzip += size.gzip;
      brotli += size.brotli;
      count += 1;
    }
    return { name, gzip, brotli, files: count };
  };
  // A route's or engine's figure: the static closure of the chunks holding
  // its modules, beyond what the initial set already loaded.
  const beyondInitial = (test: (input: string) => boolean): string[] =>
    [...inputs.graph.staticClosure(inputs.graph.outputsHolding(test))].filter((file) => !inputs.initialJs.has(file));
  const entity = tmEntryPointModule('tellma-core', 'entity');
  const search = tmEntryPointModule('tellma-core', 'search');
  const details = tmEntryPointModule('tellma-core', 'details');
  const reports = inputs.codedReports === undefined ? [] : [inputs.codedReports];
  const searchRoute = beyondInitial((input) => search(input) || entity(input));
  const detailsRoute = beyondInitial((input) => details(input) || entity(input));
  const echartsEngine = beyondInitial(tmEchartsModule);

  const figures = [
    sum('initialJs', inputs.initialJs),
    sum('initialCss', inputs.initialCss),
    sum('searchRoute', searchRoute),
    sum('detailsRoute', detailsRoute),
    sum('echartsEngine', echartsEngine),
    sum('workerShell', inputs.shell),
    { name: 'codedReports', ...tmCompressedSize(JSON.stringify(inputs.codedReports ?? [])), files: reports.length },
    sum('bootScript', ['tm-boot.js']),
  ];

  // --- Each other lazily loaded chunk: its static closure beyond the initial set ---
  const covered = new Set([...inputs.initialJs, ...searchRoute, ...detailsRoute, ...echartsEngine]);
  for (const root of inputs.graph.dynamicTargets()) {
    if (!covered.has(root)) {
      const files = [...inputs.graph.staticClosure([root])].filter((file) => !inputs.initialJs.has(file));
      figures.push(sum(`${TM_LAZY_BUDGET_PREFIX}${inputs.graph.outputs.get(root)?.entryPoint ?? root}`, files));
    }
  }
  return figures;
}

/**
 * Compares the figures with their levels.
 *
 * @param figures - The measured figures.
 * @param levels - The levels by budget name.
 * @param feature - The distribution's slug.
 * @returns A `budget-exceeded` error or a `budget-warning` per figure over a level.
 */
export function tmBudgetProblems(
  figures: readonly TmBudgetFigure[],
  levels: ReadonlyMap<string, TmBudgetLevels>,
  feature: string,
): TmBuildProblem[] {
  const problems: TmBuildProblem[] = [];
  for (const figure of figures) {
    const level = levels.get(figure.name);
    const kb = figure.gzip / 1024;
    if (level?.error != null && kb > level.error) {
      problems.push({
        code: TM_BUDGET_EXCEEDED,
        severity: 'error',
        key: `budget:${figure.name}`,
        feature,
        message: `${kb.toFixed(2)} KB gzip is over the budget's error level of ${level.error} KB.`,
      });
    } else if (level?.warn != null && kb > level.warn) {
      problems.push({
        code: TM_BUDGET_WARNING,
        severity: 'warning',
        key: `budget:${figure.name}`,
        feature,
        message: `${kb.toFixed(2)} KB gzip is over the budget's warn level of ${level.warn} KB.`,
      });
    }
  }
  return problems;
}

/**
 * Formats the figures as a table: gzip and Brotli beside each budget's levels.
 *
 * @param figures - The measured figures.
 * @param levels - The levels by budget name.
 * @returns The table's lines, each ending with a line break.
 */
export function tmFormatBudgetReport(figures: readonly TmBudgetFigure[], levels: ReadonlyMap<string, TmBudgetLevels>): string {
  const rows = [['Budget', 'Gzip KB', 'Brotli KB', 'Warn', 'Error', 'Files']];
  for (const figure of figures) {
    const level = levels.get(figure.name);
    rows.push([
      figure.name,
      figure.files === 0 ? '-' : (figure.gzip / 1024).toFixed(2),
      figure.files === 0 ? '-' : (figure.brotli / 1024).toFixed(2),
      level?.warn == null ? '-' : String(level.warn),
      level?.error == null ? '-' : String(level.error),
      String(figure.files),
    ]);
  }
  const widths = rows[0]!.map((_, column) => Math.max(...rows.map((row) => row[column]!.length)));
  return rows.map((row) => `${row.map((cell, column) => (column === 0 ? cell.padEnd(widths[column]!) : cell.padStart(widths[column]!))).join('  ')}\n`).join('');
}
