// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { readFileSync } from 'node:fs';
import { join } from 'node:path';

import { tmCompressedSize, type TmCompressedSize } from '../common/files.js';
import { TmBuildFailure, type TmBuildProblem } from '../common/problems.js';
import { tmCodeFromSources, tmReadSourceMap } from '../output/source-attribution.js';
import { tmCorePrimaryModules, tmEchartsModule, tmEntryPointModule, tmEntryPointRoot, type TmBuildGraph } from '../output/stats.js';

// The application's budgets (spec 0024 §12), measured gzip level 9 on the
// output, with Brotli reported beside. A distribution raises one in its
// package.json, `"tellma": { "budgets": { "<name>": { "error": 280, "note":
// "…" } } }`, and a raise needs its note. The boot script's ceiling grows
// with its string tables: 3 KB for the code with English, which every boot
// script embeds, and 0.4 KB for each further table. Every lazily loaded
// chunk no budget covers, a `@defer` block's or a distribution's route,
// gets a line of its own with no level: the metafile records a dynamic
// import, not whether a route or a `@defer` block made it.
//
// A route's figure is the static closure of the chunk its host loads with
// import() and of the distribution's layouts on it, beyond the initial set:
// what the first visit to the route loads before it renders. A chunk the
// platform loads on use from there, such as an entity's action dialog, is
// not on the route: it gets its own lazy line.
//
// The `@tellma/core` primary on the first frame: the code its modules
// contributed to the initial JavaScript, which it shares with the app and
// Angular, cut out of those files through their hidden source maps and
// compressed alone. It is what this app ships of the primary at once:
// primary code the app does not use is shaken out first, and primary code
// that loads only with import() later is not on the first frame. So it is a
// lower bound of the primary's own weight, the whole entry point, whose
// 55 KB cap the platform's size gate holds; a distribution checks it here
// against the same level on its own app. The product owner raised that cap
// from the 50 KB the client app foundation spec sets, and this level with it.

/** The name prefix of a lazily loaded chunk's own line, followed by the module it starts at. */
export const TM_LAZY_BUDGET_PREFIX = 'lazy:';

/** The build problem code of a figure over its error level. */
export const TM_BUDGET_EXCEEDED = 'budget-exceeded';
/** The build problem code of a figure over its warn level; a warning. */
export const TM_BUDGET_WARNING = 'budget-warning';
/** The build problem code of a raised budget without a note. */
export const TM_BUDGET_NOTE_MISSING = 'budget-note-missing';
/** The build problem code of a budget the output does not let the build measure. */
export const TM_BUDGET_UNMEASURABLE = 'budget-unmeasurable';

/** The name of the line that weighs the `@tellma/core` primary's code on the first frame. */
export const TM_CORE_PRIMARY_FIRST_FRAME = 'corePrimaryFirstFrame';

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
  // The primary's cap, which the product owner raised from 50 KB to 55 KB.
  { name: TM_CORE_PRIMARY_FIRST_FRAME, label: "The @tellma/core primary's code on the first frame, a lower bound of its own weight", warn: null, error: 55 },
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

/**
 * Whether a module is the platform's: a package's (under `node_modules`) or,
 * in a workspace, the sources of a Tellma package (`tellma-core/…`,
 * `tellma-core-ui/…`, `tellma-locale-ar/…`), rather than the distribution's own.
 *
 * @param input - The module's path, relative to the project folder with forward slashes.
 * @returns Whether it is the platform's.
 */
function isPlatformModule(input: string): boolean {
  return /(?:^|\/)node_modules\//.test(input) || /(?:^|\/)tellma-(?:core|locale)(?:-[a-z0-9]+)*\//.test(input);
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
  /** Why the output does not let the build measure the figure, when it does not; the figure then counts nothing. */
  readonly unmeasurable?: string;
}

/** What the budgets measure, from the finished output. */
export interface TmBudgetInputs {
  /** The deployable output. */
  readonly browserDir: string;
  /** The folder the hidden source maps moved to, each at its file's path with `.map` added. */
  readonly sourceMapsDir: string;
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
  const { graph } = inputs;
  const beyondInitial = (files: Iterable<string>): string[] => [...files].filter((file) => !inputs.initialJs.has(file));
  // The engine's figure: the static closure of the chunks holding its modules.
  const echartsEngine = beyondInitial(graph.staticClosure(graph.outputsHolding(tmEchartsModule)));
  // --- A route's figure: its host's chunk and the distribution's layouts on it ---
  const dynamic = graph.dynamicTargets();
  const route = (entryPoint: 'search' | 'details'): string[] => {
    const root = tmEntryPointRoot('tellma-core', entryPoint);
    const used = tmEntryPointModule('tellma-core', entryPoint);
    // The chunk the host's import() loads starts at the entry point, or holds its built bundle.
    const hosts = dynamic.filter((file) => {
      const output = graph.outputs.get(file);
      return (output?.entryPoint !== undefined && root(output.entryPoint)) || (output?.inputs.some(root) ?? false);
    });
    // A layout is a chunk of the distribution's own that the app loads with
    // import() and that uses the entry point, such as one injecting the
    // details context; the platform's own on-use chunks are not layouts.
    const layouts = dynamic.filter((file) => {
      const output = graph.outputs.get(file);
      if (output === undefined || hosts.includes(file) || isPlatformModule(output.entryPoint ?? output.inputs[0] ?? '')) {
        return false;
      }
      return [...graph.staticClosure([file])].some((reached) => graph.outputs.get(reached)?.inputs.some(used) ?? false);
    });
    return beyondInitial(graph.staticClosure([...hosts, ...layouts]));
  };
  const reports = inputs.codedReports === undefined ? [] : [inputs.codedReports];
  const searchRoute = route('search');
  const detailsRoute = route('details');

  const figures = [
    sum('initialJs', inputs.initialJs),
    sum('initialCss', inputs.initialCss),
    sum('searchRoute', searchRoute),
    sum('detailsRoute', detailsRoute),
    sum('echartsEngine', echartsEngine),
    sum('workerShell', inputs.shell),
    measurePrimary(inputs),
    { name: 'codedReports', ...tmCompressedSize(JSON.stringify(inputs.codedReports ?? [])), files: reports.length },
    sum('bootScript', ['tm-boot.js']),
  ];

  // --- Each other lazily loaded chunk: its static closure beyond the initial set ---
  const covered = new Set([...inputs.initialJs, ...searchRoute, ...detailsRoute, ...echartsEngine]);
  for (const root of dynamic) {
    if (!covered.has(root)) {
      const files = beyondInitial(graph.staticClosure([root]));
      figures.push(sum(`${TM_LAZY_BUDGET_PREFIX}${graph.outputs.get(root)?.entryPoint ?? root}`, files));
    }
  }
  return figures;
}

// How far a file's code attributed to the primary through its source map
// may stray from the bytes the stats give the primary's modules in it. With
// the chunks' trailing export statements left out, Acme's six files holding
// primary code read exact but one: the 65 KB shared chunk reads +294 bytes
// (+0.45%), constants the Angular compiler hoists out of a component with no
// mapping of their own, which the run before them takes. Single modules
// stray by up to 40 bytes either way where a run ends a little early or
// late, but within a file those shifts cancel, so no file reads under. The
// band is twice the drift seen over, half that under, and a few bytes for
// the files the primary has little code in. Past it the maps and the stats
// disagree on what the file holds, and the figure would be wrong.
/** The share of the stats' count the attribution may fall under it. */
const ATTRIBUTION_UNDER = 0.005;
/** The share of the stats' count the attribution may run over it. */
const ATTRIBUTION_OVER = 0.01;
/** The bytes the attribution may stray beyond those shares, for files the primary has little code in. */
const ATTRIBUTION_SLACK = 16;

/**
 * Weighs the `@tellma/core` primary's code on the first frame: in each
 * initial file holding any of its modules, the runs the file's source map
 * gives them, joined across files and compressed alone. Every app imports the
 * primary, so a build in which no module is the primary's, or no initial
 * file holds its code, is not measurable, and neither is a file whose map
 * disagrees with the stats.
 *
 * @param inputs - What the budgets measure.
 * @returns The figure, with why it is not measurable when it is not.
 * @throws TmBuildFailure when a file holding primary code has no readable source map.
 */
function measurePrimary(inputs: TmBudgetInputs): TmBudgetFigure {
  const unmeasurable = (reason: string): TmBudgetFigure => ({ name: TM_CORE_PRIMARY_FIRST_FRAME, gzip: 0, brotli: 0, files: 0, unmeasurable: reason });
  const primary = tmCorePrimaryModules(inputs.graph);
  if (primary.size === 0) {
    return unmeasurable("No module of the build is the @tellma/core primary's: neither its built bundle (fesm2022/tellma-core.mjs) nor its public API (tellma-core/public-api.ts).");
  }
  // --- Each initial file's primary code, checked against the stats' count ---
  const pieces: string[] = [];
  for (const file of inputs.graph.outputsHolding((input) => primary.has(input)).filter((path) => inputs.initialJs.has(path))) {
    const bytes = inputs.graph.outputs.get(file)?.bytes ?? {};
    const counted = Object.entries(bytes).reduce((total, [input, held]) => total + (primary.has(input) ? held : 0), 0);
    // A file can hold primary modules with no code there, such as a list of re-exports.
    if (counted === 0) {
      continue;
    }
    const map = tmReadSourceMap(join(inputs.sourceMapsDir, ...`${file}.map`.split('/')));
    const code = tmCodeFromSources(readFileSync(join(inputs.browserDir, ...file.split('/')), 'utf8'), map, (source) => primary.has(source));
    const attributed = Buffer.byteLength(code);
    if (attributed < counted * (1 - ATTRIBUTION_UNDER) - ATTRIBUTION_SLACK || attributed > counted * (1 + ATTRIBUTION_OVER) + ATTRIBUTION_SLACK) {
      return unmeasurable(
        `The source map of ${file} attributes ${attributed} bytes to the @tellma/core primary, where the build's stats count ${counted}; the two disagree on what the file holds.`,
      );
    }
    pieces.push(code);
  }
  if (pieces.length === 0) {
    return unmeasurable('No initial file holds code of the @tellma/core primary, which every app loads at startup.');
  }
  return { name: TM_CORE_PRIMARY_FIRST_FRAME, ...tmCompressedSize(pieces.join('')), files: pieces.length };
}

/**
 * Compares the figures with their levels.
 *
 * @param figures - The measured figures.
 * @param levels - The levels by budget name.
 * @param feature - The distribution's slug.
 * @returns A `budget-unmeasurable` error per figure the output does not let
 * the build measure, and a `budget-exceeded` error or a `budget-warning` per
 * figure over a level.
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
    if (figure.unmeasurable !== undefined) {
      problems.push({ code: TM_BUDGET_UNMEASURABLE, severity: 'error', key: `budget:${figure.name}`, feature, message: figure.unmeasurable });
    } else if (level?.error != null && kb > level.error) {
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
