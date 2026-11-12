// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';

import { tmReadBudgetLevels, tmBudgetProblems, tmFormatBudgetReport, tmMeasureBudgets, type TmBudgetLevels } from '../checks/budgets.js';
import { tmCheckBrandFiles, tmResolveBrandFiles, type TmBrandFile } from '../checks/brand.js';
import { tmRunConfigCheck } from '../checks/config-check.js';
import { tmCheckHashedNames } from '../checks/hashed-names.js';
import { tmCheckIndexHtml } from '../checks/index-html.js';
import { TM_MANIFEST_FILE, tmCheckManifest, tmLocalizedNames, type TmLocalizedNames } from '../checks/manifest.js';
import { tmListFiles, tmRelativePosix } from '../common/files.js';
import { TmBuildFailure, tmHasErrors, type TmBuildProblem } from '../common/problems.js';
import { tmRunEmit, type TmEmitOptions } from '../emit/emit.js';
import { tmReadEmittedMetadata, type TmEmittedMetadata } from '../emit/emitted-metadata.js';
import { tmBuiltFontUrls, tmFontFragments, tmIsFontForFragments } from '../fonts/index.js';
import { tmBootConfig, tmBuildBootScript } from '../output/boot.js';
import { tmAddPreloads, tmReadInitialFiles } from '../output/preloads.js';
import { tmMoveSourceMaps } from '../output/source-maps.js';
import { TmBuildGraph, tmEntryPointModule } from '../output/stats.js';
import { tmOnDemandChunks, tmWorkerConfig, tmWorkerGroups, TM_WORKER_FILES } from '../output/worker-config.js';
import { tmReadAppProject, tmRequirePackageDir, type TmAppProject } from '../project/app-project.js';
import { tmLogStep, tmReportProblems, type TmCommandContext } from './context.js';

// `tellma-client build` (spec 0024 §2.6): the production build and its
// checks, in six steps.
//
//  1. The emit, then the source checks (config, index.html, manifest, brand
//     files, budget notes), then `ng build --stats-json`. A source problem
//     stops here, before the slow part.
//  2. The brand files, tm-boot.js and the manifest's localized names.
//  3. The preloads: fonts, the Core chunk and default string pack, and the
//     /shell and /attention chunks.
//  4. The service worker's scripts and configuration, through ngsw-config.
//  5. The hidden source maps, moved out of the deployable output.
//  6. The output checks: budgets, hashed names and the built index.html.

/** The outcome of a build: the exit code's meaning, and every problem found. */
export interface TmBuildResult {
  /** Whether the build passed: no error, warnings allowed. */
  readonly passed: boolean;
  /** Every problem, warnings included. */
  readonly problems: readonly TmBuildProblem[];
}

/** What the source step hands the later steps. */
interface SourceStep {
  readonly metadata: TmEmittedMetadata;
  readonly slug: string;
  readonly coreDir: string;
  readonly brand: readonly TmBrandFile[];
  readonly names: TmLocalizedNames;
  readonly levels: ReadonlyMap<string, TmBudgetLevels>;
  readonly bootScript: string;
}

/** The angular.json convention: the emitted assets are copied to this output folder. */
const METADATA_OUTPUT = 'tm-metadata';

/** Reads a file's text, or null when it is missing. */
function readText(file: string): string | null {
  return existsSync(file) ? readFileSync(file, 'utf8') : null;
}

/** Step 1, before ng build: the emit and the source checks. */
async function sourceStep(
  context: TmCommandContext,
  project: TmAppProject,
  options: TmEmitOptions,
  problems: TmBuildProblem[],
): Promise<SourceStep> {
  tmLogStep(context, 'Emitting the client metadata');
  await tmRunEmit(project, options, context.runner);
  const metadata = tmReadEmittedMetadata(project.generatedDir);
  const slug = metadata.core.value.distribution.slug;
  const coreDir = tmRequirePackageDir(project, '@tellma/core');
  // The boot script renders now, so a string pack it cannot use fails before the build.
  const bootConfig = tmBootConfig(metadata);
  const bootScript = tmBuildBootScript(coreDir, bootConfig);

  tmLogStep(context, 'Checking the sources');
  problems.push(...(await tmRunConfigCheck(project, metadata, context.runner)));

  // --- index.html, the manifest and the brand files ---
  const indexKey = tmRelativePosix(project.dir, project.indexHtml);
  const index = readText(project.indexHtml);
  if (index === null) {
    throw new TmBuildFailure(`${indexKey} does not exist.`);
  }
  problems.push(...tmCheckIndexHtml(index, 'source', indexKey, slug));
  const names = tmLocalizedNames(metadata);
  problems.push(
    ...tmCheckManifest(readText(join(project.publicDir, TM_MANIFEST_FILE)), project.publicDir, names, `public/${TM_MANIFEST_FILE}`, slug),
  );
  const defaults = join(coreDir, 'brand');
  const brand = tmResolveBrandFiles(project.publicDir, existsSync(defaults) ? defaults : null);
  problems.push(
    ...tmCheckBrandFiles(
      brand,
      (file) => (file.override ? `public/brand/${file.name}` : file.source !== null ? `@tellma/core/brand/${file.name}` : `brand/${file.name}`),
      slug,
    ),
  );

  // --- The budgets a distribution raised ---
  const budgets = tmReadBudgetLevels(project.budgets, Object.keys(bootConfig.strings).length, slug);
  problems.push(...budgets.problems);
  return { metadata, slug, coreDir, brand, names, levels: budgets.levels, bootScript };
}

/** Step 1's end: the Angular build, with its chunk graph. */
async function angularBuild(context: TmCommandContext, project: TmAppProject): Promise<void> {
  tmLogStep(context, `Building ${project.angularProject}`);
  const ng = join(tmRequirePackageDir(project, '@angular/cli'), 'bin', 'ng.js');
  rmSync(project.outputDir, { recursive: true, force: true });
  // Angular's chunk optimizer merges chunks after esbuild has written the
  // metafile, which leaves the metafile describing chunks the output does
  // not hold and names some chunks outside the hashed-name pattern; with it
  // off, the metafile is the output's true graph.
  const code = await context.runner.run(process.execPath, [ng, 'build', '--stats-json'], {
    cwd: project.dir,
    env: { NG_BUILD_OPTIMIZE_CHUNKS: 'false' },
  });
  if (code !== 0) {
    throw new TmBuildFailure(`ng build exited with code ${code}.`);
  }
  if (!existsSync(join(project.browserDir, 'index.html'))) {
    throw new TmBuildFailure(`ng build wrote no ${tmRelativePosix(project.dir, join(project.browserDir, 'index.html'))}.`);
  }
}

/** Step 2: the brand files, tm-boot.js and the manifest's localized names. */
function assetStep(project: TmAppProject, source: SourceStep): void {
  mkdirSync(join(project.browserDir, 'brand'), { recursive: true });
  for (const file of source.brand) {
    copyFileSync(file.source!, join(project.browserDir, 'brand', file.name));
  }
  writeFileSync(join(project.browserDir, 'tm-boot.js'), source.bootScript);

  const manifestFile = join(project.browserDir, TM_MANIFEST_FILE);
  const manifest = JSON.parse(readFileSync(manifestFile, 'utf8')) as Record<string, unknown>;
  // A language without a key gets no entry; with no entries the member stays out.
  for (const [member, names] of Object.entries(source.names)) {
    if (Object.keys(names as object).length > 0) {
      manifest[member] = names;
    }
  }
  writeFileSync(manifestFile, `${JSON.stringify(manifest, null, 2)}\n`);
}

/**
 * Runs the production build of the client project in the working directory.
 *
 * @param context - The project folder, process seam and streams.
 * @param options - The emit's options.
 * @returns The outcome, once the build has passed or stopped at a problem.
 * @throws TmBuildFailure when a step cannot run.
 */
export async function tmBuild(context: TmCommandContext, options: TmEmitOptions): Promise<TmBuildResult> {
  const project = tmReadAppProject(context.cwd);
  const problems: TmBuildProblem[] = [];

  // --- Step 1: the emit, the source checks and ng build ---
  const source = await sourceStep(context, project, options, problems);
  tmReportProblems(context, problems);
  if (tmHasErrors(problems)) {
    return { passed: false, problems };
  }
  await angularBuild(context, project);
  const browser = project.browserDir;
  const { core, strings } = source.metadata;
  const languages = core.value.languages.map((language) => language.code);

  // --- Step 2: brand files, boot script, manifest names ---
  tmLogStep(context, 'Writing the brand files, tm-boot.js and the manifest names');
  assetStep(project, source);

  // --- Step 3: preloads ---
  tmLogStep(context, 'Adding the preloads');
  const graph = TmBuildGraph.read(project.statsFile);
  const indexFile = join(browser, 'index.html');
  const initial = tmReadInitialFiles(readFileSync(indexFile, 'utf8'));
  const loaded = graph.staticClosure([...initial.scripts, ...initial.modulePreloads]);
  const shellAndAttention = graph.outputsHolding(
    (input) => tmEntryPointModule('tellma-core', 'shell')(input) || tmEntryPointModule('tellma-core', 'attention')(input),
  );
  const modules = [...graph.staticClosure(shellAndAttention)].filter((file) => !loaded.has(file)).sort();
  const fontFragments = tmFontFragments(languages);
  const fetches = [core.name, strings.get(languages[0]!)?.name]
    .filter((name): name is string => name !== undefined)
    .map((name) => `${METADATA_OUTPUT}/${name}`);
  for (const file of fetches) {
    if (!existsSync(join(browser, ...file.split('/')))) {
      throw new TmBuildFailure(`The output lacks ${file}: angular.json must copy generated/assets/ to ${METADATA_OUTPUT}/.`);
    }
  }
  writeFileSync(
    indexFile,
    tmAddPreloads(readFileSync(indexFile, 'utf8'), {
      fonts: tmBuiltFontUrls(browser).filter((url) => tmIsFontForFragments(url, fontFragments)),
      fetches,
      modules,
    }),
  );
  const initialJs = new Set([...loaded, ...modules]);

  // --- Step 4: the service worker ---
  tmLogStep(context, 'Writing the service worker configuration');
  const workerPackage = tmRequirePackageDir(project, '@angular/service-worker');
  for (const script of TM_WORKER_FILES.filter((file) => file.endsWith('.js'))) {
    copyFileSync(join(workerPackage, script), join(browser, script));
  }
  const groups = tmWorkerGroups(tmListFiles(browser), tmOnDemandChunks(graph, initial.scripts), fontFragments);
  const configFile = join(project.outputDir, 'ngsw-config.json');
  writeFileSync(configFile, `${JSON.stringify(tmWorkerConfig(core.value, groups), null, 2)}\n`);
  // ngsw-config joins both paths to its working directory, so they go relative.
  const ngswCode = await context.runner.run(process.execPath, [join(workerPackage, 'ngsw-config.js'), '.', relative(browser, configFile)], {
    cwd: browser,
  });
  if (ngswCode !== 0 || !existsSync(join(browser, 'ngsw.json'))) {
    throw new TmBuildFailure(`ngsw-config exited with code ${ngswCode} without writing ngsw.json.`);
  }

  // --- Step 5: source maps ---
  tmLogStep(context, 'Moving the source maps');
  tmMoveSourceMaps(browser, project.sourceMapsDir);

  // --- Step 6: the output checks ---
  tmLogStep(context, 'Checking the output');
  const figures = tmMeasureBudgets({
    browserDir: browser,
    graph,
    initialJs,
    initialCss: initial.stylesheets,
    shell: groups.shell,
    codedReports: core.value.codedReports,
  });
  context.streams.out(tmFormatBudgetReport(figures, source.levels));
  const output = [
    ...tmBudgetProblems(figures, source.levels, source.slug),
    ...tmCheckHashedNames(tmListFiles(browser), source.slug),
    ...tmCheckIndexHtml(readFileSync(indexFile, 'utf8'), 'built', tmRelativePosix(project.dir, indexFile), source.slug),
  ];
  tmReportProblems(context, output);
  problems.push(...output);
  return { passed: !tmHasErrors(problems), problems };
}
