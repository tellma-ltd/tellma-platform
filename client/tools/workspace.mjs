// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Workspace discovery. There is no central list of packages or entry points:
 * a LIBRARY is any projects/<area>/<name> folder carrying both package.json
 * and ng-package.json, and every subfolder with its own ng-package.json is a
 * secondary entry point — the same facts the build itself runs on, so the
 * scripts that import this can never disagree with what actually builds.
 *
 * Per-package POLICY (the things that are decisions, not structure) lives in
 * each library package.json's "tellma" field, next to the code it governs:
 *
 *   "tellma": {
 *     "budgetsInKb": { ".": 4, "./select": 8 },      // KB gzipped per entry point
 *     "docs": { "globalStyles": { "./input": "…" } } // directive-owned stylesheet
 *   }
 */
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

/**
 * @typedef {object} TmEntryPoint
 * @property {string} id          '.' for the primary, './<folder>' otherwise
 * @property {string} importPath  e.g. '@tellma/core-ui/select'
 * @property {string} dir         absolute folder of the entry point
 * @property {string} publicApi   absolute path of its public-api.ts
 * @property {string} fesm        client-relative path of the built FESM
 * @property {string} dts         client-relative path of the flattened d.ts
 * @property {string} report      API-golden filename, e.g. 'core-ui.select.api.md'
 */

/** Every projects/<area>/<name> folder carrying a package.json. */
function packageDirs(clientDir) {
  const dirs = [];
  const projectsDir = join(clientDir, 'projects');
  for (const area of readdirSync(projectsDir, { withFileTypes: true })) {
    if (!area.isDirectory()) {
      continue;
    }
    for (const pkg of readdirSync(join(projectsDir, area.name), { withFileTypes: true })) {
      const dir = join(projectsDir, area.name, pkg.name);
      if (pkg.isDirectory() && existsSync(join(dir, 'package.json'))) {
        dirs.push({ dir, folder: pkg.name });
      }
    }
  }
  return dirs;
}

function packageBasics(dir, folder) {
  const packageJson = JSON.parse(readFileSync(join(dir, 'package.json'), 'utf8'));
  return {
    name: packageJson.name,
    short: folder.replace(/^tellma-/, ''),
    dir,
    tellma: packageJson.tellma,
    // Declared package names this one depends on (any section) — the build
    // orders workspace packages by these edges.
    dependsOn: [
      ...Object.keys(packageJson.dependencies ?? {}),
      ...Object.keys(packageJson.peerDependencies ?? {}),
      ...Object.keys(packageJson.devDependencies ?? {}),
    ],
  };
}

/**
 * Discovers every ng-packagr library and its entry points.
 *
 * @param {string} clientDir absolute path of the client workspace root
 * @returns {{ name: string, short: string, dir: string, tellma: any, dependsOn: string[], entryPoints: TmEntryPoint[] }[]}
 */
export function discoverLibraries(clientDir) {
  const libraries = [];
  for (const { dir, folder } of packageDirs(clientDir)) {
    if (!existsSync(join(dir, 'ng-package.json'))) {
      continue;
    }
    const basics = packageBasics(dir, folder);
    const entry = (id, sub) => ({
      id,
      importPath: sub ? `${basics.name}/${sub}` : basics.name,
      dir: sub ? join(dir, sub) : dir,
      publicApi: join(dir, sub ?? '', 'public-api.ts'),
      fesm: `dist/${basics.short}/fesm2022/${folder}${sub ? `-${sub}` : ''}.mjs`,
      dts: `dist/${basics.short}/types/${folder}${sub ? `-${sub}` : ''}.d.ts`,
      report: `${basics.short}${sub ? `.${sub}` : ''}.api.md`,
    });
    const entryPoints = [entry('.')];
    for (const sub of readdirSync(dir, { withFileTypes: true })) {
      if (sub.isDirectory() && existsSync(join(dir, sub.name, 'ng-package.json'))) {
        entryPoints.push(entry(`./${sub.name}`, sub.name));
      }
    }
    libraries.push({ ...basics, entryPoints });
  }
  return libraries;
}

/**
 * Discovers the plain Node packages (package.json, no ng-package.json —
 * currently the MCP server), built with `tsc -b` rather than ng-packagr.
 * Apps carry no package.json, so they never appear here.
 *
 * @param {string} clientDir absolute path of the client workspace root
 * @returns {{ name: string, short: string, dir: string, tellma: any, dependsOn: string[] }[]}
 */
export function discoverNodePackages(clientDir) {
  return packageDirs(clientDir)
    .filter(({ dir }) => !existsSync(join(dir, 'ng-package.json')))
    .map(({ dir, folder }) => packageBasics(dir, folder));
}
