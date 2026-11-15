// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The production build of a workspace application (the showcase), as
 * `build:showcase` makes it: `ng build <project>` followed by the font-preload
 * injection, emitted under dist/<project>/browser.
 *
 * Two callers share it. scripts/e2e.mjs makes sure the bundle is current
 * BEFORE Playwright starts its web server, so a cold build never runs inside
 * the server's startup timeout; scripts/serve.mjs --prod builds on demand
 * when it is run on its own.
 */
import { spawnSync } from 'node:child_process';
import { readdirSync, statSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** The Angular workspace (client/), whose scripts and configs this module drives. */
const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');

/**
 * Everything the production build reads: the app compiles from SOURCE (the
 * workspace tsconfig maps every `@tellma/*` specifier at projects/), so dist/
 * is output, never input. Deletions count too — a folder's own mtime moves
 * when an entry disappears, hence directories are walked, not just files.
 * Paths are client-relative; the lockfile lives at the root of the pnpm
 * workspace, one level up.
 */
const BUILD_INPUTS = [
  'projects',
  'angular.json',
  'package.json',
  join('..', 'pnpm-lock.yaml'),
  'tsconfig.json',
];

/** Folders inside the source tree that are build OUTPUT, not build input. */
const NON_SOURCE_DIRS = new Set(['node_modules', 'dist', '.angular']);

/**
 * Whether a file inside the source tree is build OUTPUT: a TypeScript
 * build-info cache.
 *
 * @param {string} name The file's name.
 * @returns {boolean} True for a build-info cache.
 */
const isBuildCache = (name) => name.endsWith('.tsbuildinfo');

/**
 * The newest mtime at or under a path, skipping build output.
 *
 * @param {string} path A file or folder.
 * @returns {number} The newest mtime in milliseconds, or 0 when the path does
 *   not exist.
 */
function newestMtime(path) {
  const info = statSync(path, { throwIfNoEntry: false });
  if (info === undefined) {
    return 0;
  }
  if (!info.isDirectory()) {
    return info.mtimeMs;
  }
  let newest = info.mtimeMs;
  for (const entry of readdirSync(path, { withFileTypes: true })) {
    if (entry.isDirectory() ? NON_SOURCE_DIRS.has(entry.name) : isBuildCache(entry.name)) {
      continue;
    }
    newest = Math.max(newest, newestMtime(join(path, entry.name)));
  }
  return newest;
}

/**
 * The folder the application builder emits a project's browser bundle into.
 *
 * @param {string} project The Angular project, e.g. `showcase`.
 * @param {string} [root] The Angular workspace folder; defaults to client/.
 * @returns {string} The absolute path of dist/<project>/browser.
 */
export function prodBuildDir(project, root = clientDir) {
  return join(root, 'dist', project, 'browser');
}

/**
 * Whether an already-built bundle still matches the sources, so that building
 * again would only repeat a multi-minute build for bytes that already exist
 * (CI runs `build:showcase` and only then the e2e suite). Errs toward
 * rebuilding: a stale reuse would test the wrong code, a needless rebuild only
 * costs time.
 *
 * @param {string} project The Angular project, e.g. `showcase`.
 * @param {string} [root] The Angular workspace folder; defaults to client/.
 * @returns {boolean} True when the bundle's index.html is newer than every
 *   build input.
 */
export function isProdBuildFresh(project, root = clientDir) {
  const built = statSync(join(prodBuildDir(project, root), 'index.html'), {
    throwIfNoEntry: false,
  });
  return (
    built !== undefined &&
    BUILD_INPUTS.every((input) => newestMtime(join(root, input)) < built.mtimeMs)
  );
}

/**
 * Runs one build step in the client workspace with the output streamed to the
 * caller's terminal.
 *
 * @param {readonly string[]} argv The Node arguments: a script and its own
 *   arguments.
 * @param {string} logPrefix The tag that leads an error line, e.g. `[e2e]`.
 * @returns {number} The step's exit code; a step that never started or was
 *   killed by a signal counts as 1.
 */
function runStep(argv, logPrefix) {
  const step = spawnSync(process.execPath, argv, { cwd: clientDir, stdio: 'inherit' });
  if (step.error !== undefined) {
    console.error(`${logPrefix} could not start ${argv[0]}: ${step.error.message}`);
  }
  return step.status ?? 1;
}

/**
 * Makes sure dist/<project>/browser holds the production build of the current
 * sources, with its font preloads: a fresh bundle is reused, a stale or missing
 * one is built.
 *
 * @param {string} project The Angular project, e.g. `showcase`.
 * @param {object} [options] How to build.
 * @param {string} [options.logPrefix] The tag that leads each progress line,
 *   e.g. `[e2e]`.
 * @returns {number} 0 when the bundle is ready, else the exit code of the
 *   step that failed.
 */
export function ensureProdBuild(project, { logPrefix = '[build]' } = {}) {
  // Resolved here rather than at import time, so a caller that only checks
  // freshness never depends on the Angular CLI being installed.
  const ng = createRequire(import.meta.url).resolve('@angular/cli/bin/ng.js', {
    paths: [clientDir],
  });

  // Build only when the sources moved past the bundle.
  if (isProdBuildFresh(project)) {
    console.log(
      `${logPrefix} reusing the existing ${project} build (newer than every source file)`,
    );
  } else {
    console.log(`${logPrefix} building ${project} (production)…`);
    const started = Date.now();
    const status = runStep([ng, 'build', project], logPrefix);
    if (status !== 0) {
      return status;
    }
    console.log(`${logPrefix} built ${project} in ${Math.round((Date.now() - started) / 1000)} s`);
  }

  // The page that SHIPS is the build plus its font preloads (`build:showcase`
  // runs both), so e2e must serve that page and not a bare `ng build` output.
  // The step is idempotent, hence unconditional: it also covers a reused
  // bundle that was produced without it.
  return runStep(
    [join(clientDir, 'scripts', 'inject-font-preloads.mjs'), join('dist', project, 'browser')],
    logPrefix,
  );
}
