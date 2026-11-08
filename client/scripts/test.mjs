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
 * A project may also declare `test-<name>` targets: each runs as a run of
 * its own after `test`, in declaration order. The unit runner shares one
 * browser page between the files of a run, so a suite that changes the page
 * irreversibly — `test-csp` installs a Content Security Policy, which can
 * never be lifted — must not share a run with anything else.
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
  // `test` first, then every `test-<name>` target, each in a run of its own.
  const targets = Object.keys(project.architect).filter(
    (target) => target === 'test' || target.startsWith('test-'),
  );
  for (const target of targets) {
    const label = target === 'test' ? name : `${name}:${target}`;
    console.log(`[test] ${label}`);
    const result = spawnSync(process.execPath, [ng, 'run', `${name}:${target}`, '--watch=false'], {
      cwd: clientDir,
      stdio: 'inherit',
    });
    if (result.status !== 0) {
      process.exit(result.status ?? 1);
    }
  }
}
