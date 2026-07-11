// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Builds every workspace package in DERIVED dependency order: packages and
 * their edges come from the same package.json files the packages declare
 * (dependencies/peerDependencies on other workspace packages), Kahn-sorted
 * with a name-sorted tie-break for determinism. ng-packagr libraries build
 * via `ng build`; plain Node packages (the MCP server) via `tsc -b`. There
 * is no hand-maintained project list. Run from anywhere; paths resolve
 * relative to this file.
 */
import { execFileSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

import { discoverLibraries, discoverNodePackages } from '../tools/workspace.mjs';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ng = require.resolve('@angular/cli/bin/ng.js', { paths: [clientDir] });
const tsc = require.resolve('typescript/bin/tsc', { paths: [clientDir] });

const run = (bin, args) =>
  execFileSync(process.execPath, [bin, ...args], { cwd: clientDir, stdio: 'inherit' });

const tsx = require.resolve('tsx/cli', { paths: [clientDir] });

// Generated assets first (token CSS + JSON schema ship as package assets).
run(tsx, ['tools/tokens/check.mts']);
run(tsx, ['tools/tokens/build-css.mts']);

const packages = [
  ...discoverLibraries(clientDir).map((pkg) => ({ ...pkg, kind: 'angular' })),
  ...discoverNodePackages(clientDir).map((pkg) => ({ ...pkg, kind: 'node' })),
];
const workspaceNames = new Set(packages.map((pkg) => pkg.name));
const remaining = new Map(packages.map((pkg) => [pkg.name, pkg]));
const built = new Set();

while (remaining.size > 0) {
  const ready = [...remaining.values()]
    .filter((pkg) => pkg.dependsOn.every((dep) => !workspaceNames.has(dep) || built.has(dep)))
    .sort((a, b) => a.name.localeCompare(b.name));
  if (ready.length === 0) {
    throw new Error(`dependency cycle among workspace packages: ${[...remaining.keys()].join(', ')}`);
  }
  for (const pkg of ready) {
    if (pkg.kind === 'angular') {
      run(ng, ['build', pkg.short]);
    } else {
      run(tsc, ['-b', relative(clientDir, pkg.dir)]);
    }
    built.add(pkg.name);
    remaining.delete(pkg.name);
  }
}
