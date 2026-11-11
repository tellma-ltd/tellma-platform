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
 * its own after `test`, in declaration order among themselves. The unit
 * runner shares one browser page between the files of a run, so a suite
 * that changes the page irreversibly — `test-csp` installs a Content
 * Security Policy, which can never be lifted — must not share a run with
 * anything else. A `test-<name>` target whose `include` names one file suffix
 * at any depth (the CSP target's `*.csp.spec.ts`) is skipped when the project
 * holds no such file.
 *
 * The CSP convention checks itself: a project that holds a `*.csp.spec.ts`
 * must exclude it from `test` and run it in `test-csp`, or the run fails —
 * so such a spec can neither leak its policy into the shared run nor be
 * silently skipped.
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

/** Whether `dir` holds, at any depth, a file whose name ends in `suffix`. */
function hasSpecs(dir, suffix = '.spec.ts') {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'node_modules' || entry.name.startsWith('.')) {
      continue;
    }
    if (entry.isFile() && entry.name.endsWith(suffix)) {
      return true;
    }
    if (entry.isDirectory() && hasSpecs(join(dir, entry.name), suffix)) {
      return true;
    }
  }
  return false;
}

/** The file suffix a target's one any-depth `include` glob names (`.csp.spec.ts`), or null. */
function includedSuffix(target) {
  const include = target.options?.include;
  if (!Array.isArray(include) || include.length !== 1) {
    return null;
  }
  const match = /^\*\*\/\*(\.[\w.]+)$/.exec(include[0]);
  return match === null ? null : match[1];
}

const CSP_SPEC = '**/*.csp.spec.ts';

const { projects } = JSON.parse(readFileSync(join(clientDir, 'angular.json'), 'utf8'));
for (const [name, project] of Object.entries(projects)) {
  if (!project.architect?.test) {
    continue;
  }
  if (!hasSpecs(join(clientDir, project.root))) {
    console.log(`[test] ${name}: no spec files — skipped`);
    continue;
  }
  if (hasSpecs(join(clientDir, project.root), '.csp.spec.ts')) {
    const excluded = project.architect.test.options?.exclude ?? [];
    if (!excluded.includes(CSP_SPEC) || includedSuffix(project.architect['test-csp'] ?? {}) !== '.csp.spec.ts') {
      console.error(
        `[test] ${name} holds a *.csp.spec.ts, so its test target must exclude '${CSP_SPEC}' ` +
          `and a test-csp target must include exactly '${CSP_SPEC}'.`,
      );
      process.exit(1);
    }
  }
  // `test` first, then every `test-<name>` target in declaration order, each
  // in a run of its own.
  const targets = [
    'test',
    ...Object.keys(project.architect).filter((target) => target.startsWith('test-')),
  ];
  for (const target of targets) {
    const label = target === 'test' ? name : `${name}:${target}`;
    const suffix = target === 'test' ? null : includedSuffix(project.architect[target]);
    if (suffix !== null && !hasSpecs(join(clientDir, project.root), suffix)) {
      console.log(`[test] ${label}: no *${suffix} files — skipped`);
      continue;
    }
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
