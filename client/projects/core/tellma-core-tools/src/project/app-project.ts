// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { existsSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

import ts from 'typescript';

import { TmBuildFailure } from '../common/problems.js';

// A distribution's client project as the commands read it (spec 0024 §2.1):
// its package.json names the Migrator and any raised budgets, its
// angular.json names the one application with its output, index and
// tsconfig, and the rest of the layout is fixed: src/app.config.ts, public/
// and generated/. The commands run in the project's folder.

/** The application's build options the commands read from angular.json. */
interface AngularBuildOptions {
  readonly outputPath?: string | { readonly base?: string; readonly browser?: string };
  readonly index?: string | { readonly input?: string };
  readonly tsConfig?: string;
}

/** A distribution's client project. */
export interface TmAppProject {
  /** The project's folder, absolute. */
  readonly dir: string;
  /** The package name in package.json. */
  readonly packageName: string;
  /** The Migrator project's folder, absolute; from `tellma.migrator` in package.json. */
  readonly migratorDir: string | null;
  /** The raw `tellma.budgets` member of package.json, which the budget check reads. */
  readonly budgets: unknown;
  /** The application's name in angular.json. */
  readonly angularProject: string;
  /** Whether angular.json gives the application a `serve` target. */
  readonly hasServeTarget: boolean;
  /** The build's output folder, `dist/<slug>` by convention, absolute. */
  readonly outputDir: string;
  /** The deployable output, the browser folder under the output folder, absolute. */
  readonly browserDir: string;
  /** Where the hidden source maps move to, outside the deployable output, absolute. */
  readonly sourceMapsDir: string;
  /** The esbuild metafile `ng build --stats-json` writes, absolute. */
  readonly statsFile: string;
  /** The source `index.html`, absolute. */
  readonly indexHtml: string;
  /** The application's tsconfig, absolute. */
  readonly tsconfig: string;
  /** The module that exports the application's configuration, absolute. */
  readonly appConfig: string;
  /** The folder Angular copies to the output root, absolute. */
  readonly publicDir: string;
  /** The folder the emit writes, absolute. */
  readonly generatedDir: string;
}

/** Reads a JSON file, failing the step with the file named when it is missing or malformed. */
function readJson(file: string): unknown {
  if (!existsSync(file)) {
    throw new TmBuildFailure(`${file} does not exist; run the command in a distribution's client project.`);
  }
  try {
    return JSON.parse(readFileSync(file, 'utf8')) as unknown;
  } catch (error) {
    throw new TmBuildFailure(`${file} is not valid JSON.`, { cause: error });
  }
}

/** A member of a parsed JSON object, or undefined. */
function member(value: unknown, name: string): unknown {
  return typeof value === 'object' && value !== null ? (value as Record<string, unknown>)[name] : undefined;
}

/**
 * Reads the client project in a folder.
 *
 * @param dir - The project's folder.
 * @returns The project.
 * @throws TmBuildFailure when package.json or angular.json is missing or does not name one application.
 */
export function tmReadAppProject(dir: string): TmAppProject {
  const appDir = resolve(dir);
  const manifest = readJson(join(appDir, 'package.json'));
  const tellma = member(manifest, 'tellma');
  const migrator = member(tellma, 'migrator');
  if (migrator !== undefined && typeof migrator !== 'string') {
    throw new TmBuildFailure('package.json: "tellma.migrator" must be the path of the Migrator project\'s folder.');
  }

  // --- The one application in angular.json ---
  const workspace = readJson(join(appDir, 'angular.json'));
  const projects = Object.entries((member(workspace, 'projects') ?? {}) as Record<string, unknown>).filter(
    ([, project]) => member(project, 'projectType') === 'application',
  );
  if (projects.length !== 1) {
    throw new TmBuildFailure(`angular.json must hold exactly one application; it holds ${projects.length}.`);
  }
  const [[name, project]] = projects as [[string, unknown]];
  const architect = member(project, 'architect') ?? member(project, 'targets');
  const build = member(architect, 'build');
  if (build === undefined) {
    throw new TmBuildFailure(`angular.json: the application '${name}' has no build target.`);
  }
  const options = (member(build, 'options') ?? {}) as AngularBuildOptions;

  // --- The output layout of the application builder ---
  const outputPath = options.outputPath;
  const base = typeof outputPath === 'string' ? outputPath : (outputPath?.base ?? `dist/${name}`);
  const browser = typeof outputPath === 'object' ? (outputPath.browser ?? 'browser') : 'browser';
  const outputDir = resolve(appDir, base);
  const index = typeof options.index === 'string' ? options.index : (options.index?.input ?? 'src/index.html');

  return {
    dir: appDir,
    packageName: String(member(manifest, 'name') ?? ''),
    migratorDir: typeof migrator === 'string' ? resolve(appDir, migrator) : null,
    budgets: member(tellma, 'budgets'),
    angularProject: name,
    hasServeTarget: member(architect, 'serve') !== undefined,
    outputDir,
    browserDir: browser === '' ? outputDir : join(outputDir, browser),
    sourceMapsDir: join(outputDir, 'sourcemaps'),
    statsFile: join(outputDir, 'stats.json'),
    indexHtml: resolve(appDir, index),
    tsconfig: resolve(appDir, options.tsConfig ?? 'tsconfig.json'),
    appConfig: join(appDir, 'src', 'app.config.ts'),
    publicDir: join(appDir, 'public'),
    generatedDir: join(appDir, 'generated'),
  };
}

/** The `paths` of a tsconfig, with the folder they are relative to. */
function tsconfigPaths(tsconfig: string): { readonly base: string; readonly paths: Readonly<Record<string, readonly string[]>> } {
  const read = ts.readConfigFile(tsconfig, (path) => ts.sys.readFile(path));
  if (read.error !== undefined) {
    return { base: dirname(tsconfig), paths: {} };
  }
  const parsed = ts.parseJsonConfigFileContent(read.config, ts.sys, dirname(tsconfig), undefined, tsconfig);
  // TypeScript resolves `paths` against the folder of the tsconfig that
  // declares them, which it records as pathsBasePath, unless baseUrl is set.
  const options = parsed.options as ts.CompilerOptions & { readonly pathsBasePath?: string };
  return { base: options.baseUrl ?? options.pathsBasePath ?? dirname(tsconfig), paths: options.paths ?? {} };
}

/** The nearest folder at or above `start` whose package.json is named `name`. */
function packageAbove(start: string, name: string): string | null {
  for (let current = start; ; current = dirname(current)) {
    const manifest = join(current, 'package.json');
    if (existsSync(manifest) && member(JSON.parse(readFileSync(manifest, 'utf8')) as unknown, 'name') === name) {
      return current;
    }
    if (dirname(current) === current) {
      return null;
    }
  }
}

/**
 * Finds a package's folder the way the application's build finds it: in a
 * `node_modules` folder at or above the project, else through the tsconfig's
 * `paths`, which point a workspace at a package's sources.
 *
 * @param project - The client project.
 * @param name - The package name, such as `@tellma/core`.
 * @returns The package's folder, or null when neither finds it.
 */
export function tmFindPackageDir(project: TmAppProject, name: string): string | null {
  // Node's lookup, which reads the folder and ignores the package's exports.
  for (let current = project.dir; ; current = dirname(current)) {
    const candidate = join(current, 'node_modules', ...name.split('/'));
    if (existsSync(join(candidate, 'package.json'))) {
      return candidate;
    }
    if (dirname(current) === current) {
      break;
    }
  }
  // A workspace's source mapping, such as "@tellma/core": ["./projects/core/tellma-core/public-api.ts"].
  const { base, paths } = tsconfigPaths(project.tsconfig);
  for (const target of paths[name] ?? []) {
    const found = packageAbove(dirname(resolve(base, target)), name);
    if (found !== null) {
      return found;
    }
  }
  return null;
}

/**
 * Finds a package's folder, failing the step when it is missing.
 *
 * @param project - The client project.
 * @param name - The package name.
 * @returns The package's folder.
 * @throws TmBuildFailure when the package is not installed.
 */
export function tmRequirePackageDir(project: TmAppProject, name: string): string {
  const dir = tmFindPackageDir(project, name);
  if (dir === null) {
    throw new TmBuildFailure(`The package ${name} is not installed in ${project.dir}; add it to the project's dependencies.`);
  }
  return dir;
}
