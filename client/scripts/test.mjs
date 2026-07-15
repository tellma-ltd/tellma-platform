// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Runs every unit suite, derived from angular.json: any project with a test
 * target AND at least one *.spec.ts under its root runs, in declaration
 * order — adding a project never requires editing a script. (A project with
 * a test target but no specs — e.g. a harness-only package exercised through
 * its consumers — is skipped, since its runner would fail on zero tests.)
 *
 * Usage: node scripts/test.mjs
 */
import { spawnSync } from 'node:child_process';
import { readFileSync, readdirSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const clientDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const ng = require.resolve('@angular/cli/bin/ng.js', { paths: [clientDir] });

function hasSpecs(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name.startsWith('.')) {
      continue;
    }
    if (entry.isFile() && entry.name.endsWith('.spec.ts')) {
      return true;
    }
    if (entry.isDirectory() && hasSpecs(join(dir, entry.name))) {
      return true;
    }
  }
  return false;
}

const { projects } = JSON.parse(readFileSync(join(clientDir, 'angular.json'), 'utf8'));
for (const [name, project] of Object.entries(projects)) {
  if (!project.architect?.test) {
    continue;
  }
  if (!hasSpecs(join(clientDir, project.root))) {
    console.log(`[test] ${name}: no spec files — skipped`);
    continue;
  }
  console.log(`[test] ${name}`);
  const result = spawnSync(process.execPath, [ng, 'test', name, '--watch=false'], {
    cwd: clientDir,
    stdio: 'inherit',
  });
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}
